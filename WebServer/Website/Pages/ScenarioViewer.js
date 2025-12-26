let scenarioSocket = null;
let scenarioDomIndex = new Map();
// key = scenarioName|tradingSymbol
let scenarioValuesDom = new Map();
// key = scenarioName -> valuesBody div
let scenarioTotalDom = new Map();
// key = scenarioName -> { npv, delta, gamma, vega, vanna, volga, correl, theta, rho }
// key = scenarioName|metric|tradingSymbol -> td
let scenarioPnLAttrDom = new Map();

const PNL_ATTR_ROWS = [
    ["actualPnL", "Actual PnL"],
    ["thetaPnL", "Theta PnL"],
    ["rfrPnL", "Rates (RFR) PnL"],

    ["deltaPnL", "Delta PnL"],
    ["gammaPnL", "Gamma PnL"],

    ["vegaPnL", "Vega PnL"],
    ["volgaPnL", "Volga PnL"],
    ["vannaPnL", "Vanna PnL"],

    ["fwdResidualPnL", "Fwd Residual"],
    ["volResidualPnL", "Vol Residual"],
    ["crossResidualPnL", "Cross Residual"],

    ["explainedPnL", "Explained PnL"],
    ["unexplainedPnL", "Unexplained PnL"]
];

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
        
        .scenario-grid-row {
            display: flex;
            gap: 16px;
            margin-bottom: 14px;
            align-items: flex-start;
        }

        .scenario-grid {
            flex: 1;
            min-width: 420px;
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

        // ---------- Header ----------
        const header = document.createElement("div");
        header.className = "scenario-title";
        header.style.cursor = "pointer";
        header.textContent = `▶ ${scenario.scenarioName}`;
        block.appendChild(header);

        // ---------- Body (collapsible) ----------
        const scenarioBody = document.createElement("div");
        scenarioBody.style.display = "none";
        scenarioBody.style.marginTop = "6px";

        header.onclick = () => {
            const open = scenarioBody.style.display === "none";
            scenarioBody.style.display = open ? "block" : "none";
            header.textContent = open
                ? `▼ ${scenario.scenarioName}`
                : `▶ ${scenario.scenarioName}`;
        };

        // ---------- Trades table ----------
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
                <td class="qty">${formatQty(t.lots)}</td>
                <td class="qty">${formatQty(t.quantity)}</td>
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

            const cells = tr.children;
            scenarioDomIndex.set(
                `${scenario.scenarioName}|${t.tradingSymbol}`,
                {
                    npv: cells[3],
                    delta: cells[4],
                    gamma: cells[5],
                    vega: cells[6],
                    vanna: cells[7],
                    volga: cells[8],
                    correl: cells[9],
                    theta: cells[10],
                    rho: cells[11]
                }
            );

            updateTradeCells(scenario.scenarioName, t.tradingSymbol, t);
        });

        // ---- TOTAL ROW ----
        const totalRow = document.createElement("tr");
        totalRow.style.fontWeight = "bold";
        totalRow.style.borderTop = "2px solid #444";

        totalRow.innerHTML = `
            <td class="symbol">Total</td>
            <td></td>
            <td></td>

            <td></td>
            <td></td>
            <td></td>

            <td></td>
            <td></td>
            <td></td>
            <td></td>

            <td></td>
            <td></td>
        `;

        table.appendChild(totalRow);

        // store total row cell refs
        const tc = totalRow.children;
        scenarioTotalDom.set(scenario.scenarioName, {
            npv:    tc[3],
            delta:  tc[4],
            gamma:  tc[5],

            vega:   tc[6],
            vanna:  tc[7],
            volga:  tc[8],
            correl: tc[9],

            theta:  tc[10],
            rho:    tc[11]
        });

        updateScenarioTotals(scenario);

        scenarioBody.appendChild(table);

        // ---------- Scenario Values (nested collapsible) ----------
        const valuesWrapper = document.createElement("div");

        const valuesHeader = document.createElement("div");
        valuesHeader.textContent = "▶ Scenario Values";
        valuesHeader.style.cursor = "pointer";
        valuesHeader.style.fontWeight = "bold";
        valuesHeader.style.marginTop = "10px";

        const valuesBody = document.createElement("div");
        scenarioValuesDom.set(scenario.scenarioName, valuesBody);

        if (scenario.scenarioValues?.length) {
            const groupedNPV = groupScenarioValues(scenario.scenarioValues);
            const groupedPNL = groupScenarioPnL(scenario.scenarioValues);

            // ---------- ROW 1: NPV ----------
            const npvRow = document.createElement("div");
            npvRow.className = "scenario-grid-row";

            groupedNPV.forEach((fwdMap, timeShift) => {
                const box = document.createElement("div");
                box.className = "scenario-grid";

                renderScenarioGrid(box, timeShift, fwdMap, "NPV");
                npvRow.appendChild(box);
            });

            valuesBody.appendChild(npvRow);

            // ---------- ROW 2: PnL ----------
            const pnlRow = document.createElement("div");
            pnlRow.className = "scenario-grid-row";

            groupedPNL.forEach((fwdMap, timeShift) => {
                const box = document.createElement("div");
                box.className = "scenario-grid";

                renderScenarioGrid(box, timeShift, fwdMap, "PNL");
                pnlRow.appendChild(box);
            });

            valuesBody.appendChild(pnlRow);
        }

        valuesBody.style.display = "none";
        valuesBody.style.marginTop = "6px";

        valuesHeader.onclick = () => {
            const open = valuesBody.style.display === "none";
            valuesBody.style.display = open ? "block" : "none";
            valuesHeader.textContent = open
                ? "▼ Scenario Values"
                : "▶ Scenario Values";
        };

        valuesWrapper.appendChild(valuesHeader);
        valuesWrapper.appendChild(valuesBody);
        scenarioBody.appendChild(valuesWrapper);

        // ---------- PnL Attribution (VERTICAL, collapsible) ----------
        if (scenario.tradePnL && scenario.tradePnL.length > 0) {

            const pnlHeader = document.createElement("div");
            pnlHeader.textContent = "▶ PnL Attribution";
            pnlHeader.style.cursor = "pointer";
            pnlHeader.style.fontWeight = "bold";
            pnlHeader.style.marginTop = "10px";

            const pnlBody = document.createElement("div");
            pnlBody.style.display = "none";
            pnlBody.style.marginTop = "6px";

            pnlHeader.onclick = () => {
                const open = pnlBody.style.display === "none";
                pnlBody.style.display = open ? "block" : "none";
                pnlHeader.textContent = open
                    ? "▼ PnL Attribution"
                    : "▶ PnL Attribution";
            };

            const table = document.createElement("table");

            // ---- Header row (symbols across) ----
            let html = "<tr><th>Metric</th>";
            scenario.tradePnL.forEach(p => {
                html += `<th>${p.tradingSymbol}</th>`;
            });
            html += "</tr>";

            // ---- One row per PnL component ----
            PNL_ATTR_ROWS.forEach(([field, label]) => {
                html += `<tr><th>${label}</th>`;

                scenario.tradePnL.forEach(p => {
                    const key = `${scenario.scenarioName}|${field}|${p.tradingSymbol}`;
                    html += `<td data-key="${key}"></td>`;
                });

                html += "</tr>";
            });

            table.innerHTML = html;
            pnlBody.appendChild(table);

            scenarioBody.appendChild(pnlHeader);
            scenarioBody.appendChild(pnlBody);

            // ---- Index the cells for live updates ----
            table.querySelectorAll("td[data-key]").forEach(td => {
                scenarioPnLAttrDom.set(td.dataset.key, td);
            });

            // ---- Initial fill ----
            scenario.tradePnL.forEach(pnl =>
                updatePnLAttributionCells(scenario.scenarioName, pnl)
            );
        }

        // ---------- Final assembly ----------
        block.appendChild(scenarioBody);
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

    setCellValue(dom.npv,    trade.npv);
    setCellValue(dom.delta,  trade.delta);
    setCellValue(dom.gamma,  trade.gamma);

    setCellValue(dom.vega,   trade.vega);
    setCellValue(dom.vanna,  trade.vanna);
    setCellValue(dom.volga,  trade.volga);
    setCellValue(dom.correl, trade.correl);

    setCellValue(dom.theta,  trade.theta);
    setCellValue(dom.rho,    trade.rho);
}

function updatePnLAttributionCells(scenarioName, pnl) {
    PNL_ATTR_ROWS.forEach(([field]) => {
        const key = `${scenarioName}|${field}|${pnl.tradingSymbol}`;
        const cell = scenarioPnLAttrDom.get(key);
        if (!cell) return;

        setCellValue(cell, pnl[field]);
    });
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

            console.log("scenario WS data received :", msg);
            applyScenarioUpdate(msg.data);
        } catch (err) {
            console.error("Scenario WS parse error", err);
        }
    };
}

function applyScenarioUpdate(snapshot) {
    if (!snapshot.scenarios) return;

    snapshot.scenarios.forEach(scenario => {

        // 1️⃣ Update live greeks (existing)
        scenario.trades.forEach(trade => {
            updateTradeCells(
                scenario.scenarioName,
                trade.tradingSymbol,
                trade
            );
        });
        updateScenarioTotals(scenario);

        // 1️⃣b Update PnL Attribution
        if (scenario.tradePnL) {
            scenario.tradePnL.forEach(pnl => {
                updatePnLAttributionCells(
                    scenario.scenarioName,
                    pnl
                );
            });
        }

        // 2️⃣ Update scenario value grids
        const valuesBody = scenarioValuesDom.get(scenario.scenarioName);
        if (!valuesBody) return;

        // clear old grids
        // clear old grids
        valuesBody.innerHTML = "";

        if (scenario.scenarioValues?.length) {
            const groupedNPV = groupScenarioValues(scenario.scenarioValues);
            const groupedPNL = groupScenarioPnL(scenario.scenarioValues);

            // ---------- ROW 1: NPV ----------
            const npvRow = document.createElement("div");
            npvRow.className = "scenario-grid-row";

            groupedNPV.forEach((fwdMap, timeShift) => {
                const box = document.createElement("div");
                box.className = "scenario-grid";

                renderScenarioGrid(box, timeShift, fwdMap, "NPV");
                npvRow.appendChild(box);
            });

            valuesBody.appendChild(npvRow);

            // ---------- ROW 2: PnL ----------
            const pnlRow = document.createElement("div");
            pnlRow.className = "scenario-grid-row";

            groupedPNL.forEach((fwdMap, timeShift) => {
                const box = document.createElement("div");
                box.className = "scenario-grid";

                renderScenarioGrid(box, timeShift, fwdMap, "PNL");
                pnlRow.appendChild(box);
            });

            valuesBody.appendChild(pnlRow);
        }
    });
}

function norm(x) {
    const n = Number(x);
    return Object.is(n, -0) ? 0 : n;
}

function groupScenarioValues(values) {
    const map = new Map();

    values.forEach(v => {
        const t = norm(v.timeShiftYears);
        const f = norm(v.forwardShiftPct);
        const vol = norm(v.volShiftAbs);

        if (!map.has(t)) map.set(t, new Map());

        const fwdMap = map.get(t);

        if (!fwdMap.has(f)) fwdMap.set(f, new Map());

        fwdMap.get(f).set(vol, Number(v.totalNPV));
    });

    return map;
}

function groupScenarioPnL(values) {
    const baseNPV = getBaseNPV(values);
    const map = new Map();

    values.forEach(v => {
        const t   = norm(v.timeShiftYears);
        const f   = norm(v.forwardShiftPct);
        const vol = norm(v.volShiftAbs);

        if (!map.has(t)) map.set(t, new Map());
        const fwdMap = map.get(t);

        if (!fwdMap.has(f)) fwdMap.set(f, new Map());

        const pnl = Number(v.totalNPV) - baseNPV;
        fwdMap.get(f).set(vol, pnl);
    });

    return map;
}

//helper function to render one scenario grid
function renderScenarioGrid(container, timeShift, fwdMap, mode) {
    const fwdShifts = Array.from(fwdMap.keys())
        .map(norm)
        .sort((a,b)=>a-b);
    const volSet = new Set();
    fwdMap.forEach((volMap) => {
        volMap.forEach((_, vol) => volSet.add(norm(vol)));
    });

    const volShifts = Array.from(volSet).sort((a,b)=>a-b);

    const title = document.createElement("div");
    title.textContent = `${mode} — Time Shift: ${timeShift}`;
    title.style.fontWeight = "bold";
    title.style.margin = "6px 0";
    container.appendChild(title);

    const table = document.createElement("table");

    // header row
    let html = "<tr><th>Fwd \\ Vol</th>";
    volShifts.forEach(v => {
        html += `<th>${(v*100).toFixed(1)}%</th>`;
    });
    html += "</tr>";

    // data rows
    fwdShifts.forEach(fwd => {
        html += `<tr><th>${(fwd*100).toFixed(1)}%</th>`;
        volShifts.forEach(vol => {
            const val = fwdMap.get(fwd).get(vol);
            if (val === undefined) {
                html += `<td></td>`;
            } else {
                const v = Math.round(val);
                const color = v < 0 ? 'style="color:#c00000"' : "";
                html += `<td ${color}>${fmtIndian0.format(v)}</td>`;
            }
        });
        html += "</tr>";
    });

    table.innerHTML = html;
    container.appendChild(table);
}

// Indian number formatter (no decimals)
const fmtIndianInt = new Intl.NumberFormat("en-IN", {
    maximumFractionDigits: 0
});

// Indian number formatter (0 decimals, finance)
const fmtIndian0 = new Intl.NumberFormat("en-IN", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0
});

function formatQty(x) {
    if (x === null || x === undefined) return "";
    return fmtIndianInt.format(Math.trunc(x));
}

function formatValue0(x) {
    if (x === null || x === undefined) return "";
    return fmtIndian0.format(Math.round(x));
}

// Apply value + Excel-style negative coloring
function setCellValue(cell, value) {
    if (!cell) return;

    if (value === null || value === undefined) {
        cell.textContent = "";
        cell.style.color = "";
        return;
    }

    const v = Math.round(value);
    cell.textContent = fmtIndian0.format(v);

    if (v < 0) {
        cell.style.color = "#c00000";   // Excel red
    } else {
        cell.style.color = "";
    }
}

function updateScenarioTotals(scenario) {
    const dom = scenarioTotalDom.get(scenario.scenarioName);
    if (!dom) return;

    let totals = {
        npv: 0, delta: 0, gamma: 0,
        vega: 0, vanna: 0, volga: 0,
        correl: 0, theta: 0, rho: 0
    };

    scenario.trades.forEach(t => {
        totals.npv    += t.npv    || 0;
        totals.delta  += t.delta  || 0;
        totals.gamma  += t.gamma  || 0;

        totals.vega   += t.vega   || 0;
        totals.vanna  += t.vanna  || 0;
        totals.volga  += t.volga  || 0;
        totals.correl += t.correl || 0;

        totals.theta  += t.theta  || 0;
        totals.rho    += t.rho    || 0;
    });

    setCellValue(dom.npv,    totals.npv);
    setCellValue(dom.delta,  totals.delta);
    setCellValue(dom.gamma,  totals.gamma);

    setCellValue(dom.vega,   totals.vega);
    setCellValue(dom.vanna,  totals.vanna);
    setCellValue(dom.volga,  totals.volga);
    setCellValue(dom.correl, totals.correl);

    setCellValue(dom.theta,  totals.theta);
    setCellValue(dom.rho,    totals.rho);
}

function getBaseNPV(values) {
    // find (0,0,0)
    for (const v of values) {
        if (
            norm(v.timeShiftYears) === 0 &&
            norm(v.forwardShiftPct) === 0 &&
            norm(v.volShiftAbs) === 0
        ) {
            return Number(v.totalNPV) || 0;
        }
    }
    return 0; // fallback (should not happen)
}