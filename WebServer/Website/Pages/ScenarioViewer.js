window.addEventListener("load", onPageLoad);

async function onPageLoad() {
    injectStyles();

    try {
        const resp = await fetch("/api/scenario/view");
        if (!resp.ok) throw new Error("Failed to fetch scenario view");

        const data = await resp.json();
        console.log("Scenario view snapshot:", data);

        renderScenarioViewer(data);
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
                <td>${fmt(t.npv)}</td>
                <td>${fmt(t.delta)}</td>
                <td>${fmt(t.gamma)}</td>
                <td>${fmt(t.vega)}</td>
                <td>${fmt(t.theta)}</td>
                <td>${fmt(t.rho)}</td>
            `;

            table.appendChild(tr);
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