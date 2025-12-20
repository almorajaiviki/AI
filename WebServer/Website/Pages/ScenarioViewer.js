let scenarioSocket = null;
let scenarioDomIndex = new Map();
// key = scenarioName|tradingSymbol
window.addEventListener("load", onPageLoad);

async function onPageLoad() {
    injectStyles();

    try {
        const resp = await fetch("/api/scenario/view");
        if (!resp.ok) throw new Error("Failed to fetch scenario view");

        const data = await resp.json();
        console.log("Scenario view snapshot:", data);

        renderScenarioViewer(data);
        connectScenarioWebSocket();   // 👈 ADD THIS (for websocket updates)
    } catch (err) {
        console.error("Failed to load scenario viewer", err);
    }
}

/* =========================================================
   Styling (JS-driven)
   ========================================================= */
function injectStyles() {
    const style = document.createElement("style");
    style.innerHTML = `
        body {
            font-family: Arial, sans-serif;
            margin: 12px;
        }

        h1 {
            margin-bottom: 16px;
        }

        .scenario-block {
            margin-bottom: 28px;
        }

        .scenario-title {
            font-size: 18px;
            font-weight: bold;
            margin-bottom: 6px;
        }

        table {
            border-collapse: collapse;
            width: 100%;
            max-width: 1200px;
        }

        th, td {
            border: 1px solid #ccc;
            padding: 4px 6px;
            text-align: right;
            font-size: 13px;
        }

        th {
            background: #eee;
            text-align: center;
        }

        td.symbol {
            text-align: left;
            font-weight: bold;
        }

        td.qty {
            text-align: center;
        }
    `;
    document.head.appendChild(style);
}

/* =========================================================
   Rendering
   ========================================================= */
function renderScenarioViewer(data) {
    const app = document.getElementById("app");
    app.innerHTML = "";

    const title = document.createElement("h1");
    title.innerText = "Scenario Viewer";
    app.appendChild(title);

    if (!data.scenarios || data.scenarios.length === 0) {
        const p = document.createElement("p");
        p.innerText = "No scenarios available.";
        app.appendChild(p);
        return;
    }

    data.scenarios.forEach(scenario => {
        const block = document.createElement("div");
        block.className = "scenario-block";

        // Scenario name
        const header = document.createElement("div");
        header.className = "scenario-title";
        header.innerText = scenario.scenarioName;
        block.appendChild(header);

        // Trades table
        const table = document.createElement("table");

        const hdr = document.createElement("tr");
        hdr.innerHTML = `
            <th>Trading Symbol</th>
            <th>Lots</th>
            <th>Qty</th>

            <th>NPV</th>
            <th>Delta</th>
            <th>Gamma</th>

            <th>Vega</th>
            <th>Vanna</th>
            <th>Volga</th>
            <th>Correl</th>

            <th>Theta</th>
            <th>Rho</th>
        `;
        table.appendChild(hdr);

        scenario.trades.forEach(t => {
            const tr = document.createElement("tr");

            tr.innerHTML = `
                <td class="symbol">${t.tradingSymbol}</td>
                <td class="qty">${t.lots}</td>
                <td class="qty">${t.quantity}</td>

                <td class="npv"></td>
                <td class="delta"></td>
                <td class="gamma"></td>

                <td class="vega"></td>
                <td class="vanna"></td>
                <td class="volga"></td>
                <td class="correl"></td>

                <td class="theta"></td>
                <td class="rho"></td>
            `;

            table.appendChild(tr);

            // 🔹 index cells
            const cells = tr.children;
            scenarioDomIndex.set(
                `${scenario.scenarioName}|${t.tradingSymbol}`,
                {
                    npv:    cells[3],
                    delta:  cells[4],
                    gamma:  cells[5],

                    vega:   cells[6],
                    vanna:  cells[7],
                    volga:  cells[8],
                    correl: cells[9],

                    theta:  cells[10],
                    rho:    cells[11]
                }
            );

            // initial values
            updateTradeCells(
                scenario.scenarioName,
                t.tradingSymbol,
                t
            );
        });

        block.appendChild(table);
        app.appendChild(block);
    });
}

/* =========================================================
   Helpers
   ========================================================= */
function fmt(x) {
    if (x === null || x === undefined) return "";
    return Number(x).toFixed(4);
}

function updateTradeCells(scenarioName, tradingSymbol, trade) {
    const key = `${scenarioName}|${tradingSymbol}`;
    const dom = scenarioDomIndex.get(key);
    if (!dom) return;

    dom.npv.textContent    = fmt(trade.npv);
    dom.delta.textContent  = fmt(trade.delta);
    dom.gamma.textContent  = fmt(trade.gamma);

    dom.vega.textContent   = fmt(trade.vega);
    dom.vanna.textContent  = fmt(trade.vanna);
    dom.volga.textContent  = fmt(trade.volga);
    dom.correl.textContent = fmt(trade.correl);

    dom.theta.textContent  = fmt(trade.theta);
    dom.rho.textContent    = fmt(trade.rho);
}

function connectScenarioWebSocket() {
    if (scenarioSocket) return;

    scenarioSocket = new WebSocket("ws://localhost:50001");

    scenarioSocket.onopen = () => {
        console.log("Scenario WS connected");
    };

    scenarioSocket.onclose = () => {
        console.log("Scenario WS closed");
        scenarioSocket = null;
    };

    scenarioSocket.onerror = err => {
        console.error("Scenario WS error", err);
    };

    scenarioSocket.onmessage = evt => {
        try {
            const msg = JSON.parse(evt.data);
            if (msg.type !== "scenario") return;

            applyScenarioUpdate(msg.data);
        } catch (err) {
            console.error("Scenario WS parse error", err);
        }
    };
}

function applyScenarioUpdate(snapshot) {
    if (!snapshot.scenarios) return;

    snapshot.scenarios.forEach(scenario => {
        scenario.trades.forEach(trade => {
            updateTradeCells(
                scenario.scenarioName,
                trade.tradingSymbol,
                trade
            );
        });
    });
}