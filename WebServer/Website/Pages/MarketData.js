'use strict';

/* ============================================================
   MarketData.js (updated)
   - All references to `impliedFuture` removed
   - Uses data.forwardByExpiry[expiry] for per-expiry forward lookups
   - Option B shading: ITM / OTM highlighting for calls/puts
   ============================================================ */

/* ----------------------
   Global AMS holder & WebSocket
   ---------------------- */
let currentAMS = null;
const socket = new WebSocket("ws://localhost:50001");

/* ----------------------
   WebSocket message handler
   ---------------------- */
socket.onmessage = function (event) {
    try {
        const newAMS = JSON.parse(event.data);
        if (newAMS.type !== "ams") return;
        updateMarketSnapshot(newAMS);
        currentAMS = newAMS;
    } catch (err) {
        console.error("WebSocket parse error:", err, event.data);
    }
};

socket.onopen = function () {
    console.log("MarketData WebSocket opened.");
};

socket.onerror = function (err) {
    console.error("MarketData WebSocket error:", err);
};

socket.onclose = function () {
    console.log("MarketData WebSocket closed.");
};

/* ----------------------
   Initial fetch on page load
   ---------------------- */
window.addEventListener("load", async () => {
    try {
        const response = await fetch("http://localhost:50000/api/snapshot");
        if (!response.ok) throw new Error("Snapshot fetch failed");
        const snapshot = await response.json();
        currentAMS = snapshot;
        buildMarketLayout(snapshot);
        updateMarketSnapshot(snapshot);
        createSnapshotToolbar();
    } catch (err) {
        console.error("Failed to load initial snapshot:", err);
        // Build layout anyway so toolbar appears
        buildMarketLayout({ futures: [], optionPairs: [], forwardByExpiry: {} });
        createSnapshotToolbar();
    }
});

/* ----------------------
   Graceful socket close
   ---------------------- */
window.addEventListener("beforeunload", function () {
    if (socket.readyState === WebSocket.OPEN) {
        socket.close();
    }
});

/* ----------------------
   CSS injection (highlights, table styles)
   ---------------------- */
(function addHighlightStyles() {
    const style = document.createElement("style");
    style.innerHTML = `
        .highlight-put { background-color: rgba(220,220,220,0.25); }
        .highlight-call { background-color: rgba(220,220,220,0.25); }

        .expiry-header button:hover {
           background: #e0e0e0;
}

        /* ITM/OTM classes for Option B shading */
        .itm-call { background-color: rgba(200, 240, 200, 0.35); }   /* greenish for ITM calls */
        .otm-call { background-color: rgba(240, 240, 240, 0.15); }   /* light grey for OTM calls */
        .itm-put  { background-color: rgba(240, 200, 200, 0.35); }   /* reddish for ITM puts */
        .otm-put  { background-color: rgba(240, 240, 240, 0.15); }   /* light grey for OTM puts */

        #option-chain table, #futures table {
            border-collapse: collapse; width: 100%;
        }
        #option-chain th, #option-chain td,
        #futures th, #futures td {
            padding: 4px 6px;
            border: 1px solid #ddd;
            text-align: right;
            font-family: monospace;
            font-size: 12px;
        }
        #option-chain th, #futures th {
            background: #f5f5f5;
            text-align: center;
            font-weight: 600;
        }
        #option-chain td.strike { text-align: center; font-weight: 600; }
        .expiry-header { margin: 10px 0 2px 0; font-family: system-ui, -apple-system, "Segoe UI", Roboto; }
        #ams-toolbar { position: fixed; top: 12px; right: 12px; z-index: 1000; display: flex; gap: 8px; align-items: center; }
        #ams-toolbar button { background: #333; color: white; border: none; padding: 6px 10px; border-radius: 6px; cursor: pointer; font-size: 12px; }
        #ams-toolbar button:hover { background: #111; }
    `;
    document.head.appendChild(style);
})();

/* ============================================================
   Build static page layout
   ============================================================ */
function buildMarketLayout(data) {
    const marketDiv = document.getElementById("market-info");
    marketDiv.innerHTML = `
        <table>
            <tr>
                <th>Index Spot</th><th>RFR</th><th>DivYield</th><th>Last Update</th>
            </tr>
            <tr>
                <td id="IndexSpot"></td>
                <td id="RFR"></td>
                <td id="DivYield"></td>
                <td id="LastUpdate"></td>
            </tr>
        </table>
    `;

    // Futures area
    const futuresDiv = document.getElementById("futures");
    if (data.futures?.length > 0) {

        // ✅ NEW: Sort futures by expiry ASC
        data.futures.sort((a, b) =>
            new Date(a.futureSnapshot.expiry) - new Date(b.futureSnapshot.expiry)
        );

        let html = "<table><tr><th>Symbol</th><th>Bid</th><th>NPV</th><th>Ask</th><th>OI</th></tr>";

        for (const f of data.futures) {
            const snap = f.futureSnapshot;
            html += `
                <tr>
                    <td>${snap.tradingSymbol}</td>
                    <td id="f_${snap.token}_Bid"></td>
                    <td id="f_${snap.token}_NPV"></td>
                    <td id="f_${snap.token}_Ask"></td>
                    <td id="f_${snap.token}_OI"></td>
                </tr>`;
        }

        html += "</table>";
        futuresDiv.innerHTML = html;
    } else {
        futuresDiv.innerHTML = "<p>No futures data available.</p>";
    }

    // Build option chain tables (if data present)
    buildOptionChainTables(data);
}

/* ============================================================
   Build multi-expiry option chain tables
   - Restored features:
       ✓ Collapsible expiry sections (default collapsed)
       ✓ Sorting per-expiry (strike ascending)
       ✓ Forward header with live updates
   ============================================================ */
function buildOptionChainTables(data) {
    const optionDiv = document.getElementById("option-chain");
    optionDiv.innerHTML = "";

    if (!data.optionPairs?.length) {
        optionDiv.innerHTML = "<p>No option data available.</p>";
        return;
    }

    // --- Group by expiry ---
    const groups = {};
    for (const pair of data.optionPairs) {
        if (!groups[pair.expiry]) groups[pair.expiry] = [];
        groups[pair.expiry].push(pair);
    }

    // --- Sort expiries ASC ---
    const sortedExpiries = Object.keys(groups).sort();

    for (const exp of sortedExpiries) {
        const group = groups[exp];
        group.sort((a, b) => a.strike - b.strike);

        const wrapper = document.createElement("div");
        wrapper.style.marginBottom = "12px";

        const forward =
            data.forwardByExpiry && data.forwardByExpiry[exp] != null
                ? Number(data.forwardByExpiry[exp])
                : null;

        const tableId = `table_${exp.replace(/[^A-Za-z0-9]/g, "_")}`;

        wrapper.innerHTML = `
            <div class="expiry-header" style="cursor:pointer; user-select:none;">
                <b>Expiry:</b> ${exp}
                <b style="margin-left:10px;">Forward:</b>
                <span id="expFwd_${exp}">
                    ${forward != null ? forward.toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : "-"}
                </span>
                <span id="toggle_${tableId}" style="margin-left:10px; font-weight:bold;">[+]</span>
            </div>
            <div id="${tableId}" style="display:none;"></div>
        `;

        const tableContainer = wrapper.querySelector(`#${tableId}`);

        let html = `
            <table>
                <tr>
                    <th colspan="12">CALLS</th>
                    <th rowspan="2">Strike<br/>(% Fwd)</th>
                    <th colspan="12">PUTS</th>
                </tr>
                <tr>
                    <th>Rho</th><th>Theta</th><th>Vega</th><th>Gamma</th><th>Delta</th>
                    <th>OI</th><th>Bid</th><th>BidSprd</th>
                    <th>NPV</th><th>AskSprd</th><th>Ask</th><th>IV</th>

                    <th>IV</th><th>Bid</th><th>BidSprd</th>
                    <th>NPV</th><th>AskSprd</th><th>Ask</th><th>OI</th>
                    <th>Delta</th><th>Gamma</th><th>Vega</th><th>Theta</th><th>Rho</th>
                </tr>
        `;

        for (const pair of group) {
            const c = pair.c_token;
            const p = pair.p_token;
            const strikeDisplay = Number(pair.strike).toLocaleString("en-IN");

            html += `
                <tr id="row_${c}">
                    <td id="${c}_rho"></td>
                    <td id="${c}_theta"></td>
                    <td id="${c}_vega"></td>
                    <td id="${c}_gamma"></td>
                    <td id="${c}_delta"></td>
                    <td id="${c}_oi"></td>
                    <td id="${c}_bid"></td>
                    <td id="${c}_bidsprd"></td>
                    <td id="${c}_npv"></td>
                    <td id="${c}_asksprd"></td>
                    <td id="${c}_ask"></td>
                    <td id="${c}_ivused"></td>

                    <td class="strike">${strikeDisplay}</td>

                    <td id="${p}_ivused"></td>
                    <td id="${p}_bid"></td>
                    <td id="${p}_bidsprd"></td>
                    <td id="${p}_npv"></td>
                    <td id="${p}_asksprd"></td>
                    <td id="${p}_ask"></td>
                    <td id="${p}_oi"></td>
                    <td id="${p}_delta"></td>
                    <td id="${p}_gamma"></td>
                    <td id="${p}_vega"></td>
                    <td id="${p}_theta"></td>
                    <td id="${p}_rho"></td>
                </tr>
            `;
        }

        html += "</table>";
        tableContainer.innerHTML = html;
        optionDiv.appendChild(wrapper);

        for (const pair of group) {
            const rowElem = document.getElementById(`row_${pair.c_token}`);
            if (rowElem) {
                applyShadingForRow(rowElem, Number(pair.strike), forward, pair.c_token, pair.p_token);
            }
        }

        const header = wrapper.querySelector(".expiry-header");
        const toggle = wrapper.querySelector(`#toggle_${tableId}`);
        header.addEventListener("click", () => {
            const hidden = tableContainer.style.display === "none";
            tableContainer.style.display = hidden ? "block" : "none";
            toggle.textContent = hidden ? "[-]" : "[+]";
        });
    }
}

/* ============================================================
   Update snapshot values in page
   - uses per-pair expiry to lookup forward
   ============================================================ */
function updateMarketSnapshot(data) {

    // === Market info ===
    updateCell("IndexSpot", data.spot, 2, "", true);

    updateCell("RFR", (data.riskFreeRate ?? 0) * 100, 2, "%");
    updateCell("DivYield", (data.divYield ?? 0) * 100, 2, "%");
    updateCell("LastUpdate", new Date(data.snapTime).toLocaleString("en-GB", {
        year: "numeric", month: "short", day: "2-digit",
        hour: "2-digit", minute: "2-digit", second: "2-digit",
        fractionalSecondDigits: 3
    }));

    // === Futures ===
    if (data.futures) {
        for (const f of data.futures) {
            const snap = f.futureSnapshot;
            const greeks = f.futureGreeks;
            const token = snap.token;

            updateCell(`f_${token}_Bid`, snap.bid, 2);
            updateCell(`f_${token}_NPV`, greeks?.npv ?? 0, 2);
            updateCell(`f_${token}_Ask`, snap.ask, 2);
            updateCell(`f_${token}_OI`, snap.oi, 0, "", true);
        }
    }

    // === Option updates ===
    if (data.optionPairs) {
        for (const pair of data.optionPairs) {
            const cToken = pair.c_token;
            const pToken = pair.p_token;

            // === NEW: Update expiry forward header ===
            if (data.forwardByExpiry && data.forwardByExpiry[pair.expiry] != null) {
                const fwdHeader = document.getElementById(`expFwd_${pair.expiry}`);
                if (fwdHeader) {
                    fwdHeader.textContent = Number(data.forwardByExpiry[pair.expiry])
                        .toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                }
            }

            // Lookup forward for shading
            let forward = null;
            if (data.forwardByExpiry && data.forwardByExpiry[pair.expiry] != null) {
                forward = Number(data.forwardByExpiry[pair.expiry]);
            }

            // === Update CALL side ===
            updateCell(`${cToken}_vega`, pair.c_vega ?? 0, 4);
            updateCell(`${cToken}_theta`, pair.c_theta ?? 0, 4);
            updateCell(`${cToken}_rho`, pair.c_rho ?? 0, 4);
            updateCell(`${cToken}_gamma`, pair.c_gamma ?? 0, 4);
            updateCell(`${cToken}_delta`, pair.c_delta ?? 0, 4);
            updateCell(`${cToken}_oi`, pair.c_oi ?? 0, 0, "", true);
            updateCell(`${cToken}_bid`, pair.c_bid ?? 0, 2);
            updateCell(`${cToken}_bidsprd`, pair.c_bidSpread ?? 0, 2);
            updateCell(`${cToken}_npv`, pair.c_npv ?? 0, 2);
            updateCell(`${cToken}_asksprd`, pair.c_askSpread ?? 0, 2);
            updateCell(`${cToken}_ask`, pair.c_ask ?? 0, 2);
            updateCell(`${cToken}_ivused`, (pair.c_iv ?? 0) * 100, 2, "%");

            const strikeCell = document.getElementById(`${cToken}_strike`);
            if (strikeCell)
                strikeCell.textContent = Number(pair.strike).toLocaleString("en-IN");

            // === Update PUT side ===
            updateCell(`${pToken}_ivused`, (pair.p_iv ?? 0) * 100, 2, "%");
            updateCell(`${pToken}_bid`, pair.p_bid ?? 0, 2);
            updateCell(`${pToken}_bidsprd`, pair.p_bidSpread ?? 0, 2);
            updateCell(`${pToken}_npv`, pair.p_npv ?? 0, 2);
            updateCell(`${pToken}_asksprd`, pair.p_askSpread ?? 0, 2);
            updateCell(`${pToken}_ask`, pair.p_ask ?? 0, 2);
            updateCell(`${pToken}_oi`, pair.p_oi ?? 0, 0, "", true);
            updateCell(`${pToken}_delta`, pair.p_delta ?? 0, 4);
            updateCell(`${pToken}_gamma`, pair.p_gamma ?? 0, 4);
            updateCell(`${pToken}_vega`, pair.p_vega ?? 0, 4);
            updateCell(`${pToken}_theta`, pair.p_theta ?? 0, 4);
            updateCell(`${pToken}_rho`, pair.p_rho ?? 0, 4);

            // === Apply shading ===
            const rowElem = document.getElementById(`row_${cToken}`);
            if (rowElem) {
                applyShadingForRow(rowElem, Number(pair.strike), forward, cToken, pToken);
            }
        }
    }
}

/* ============================================================
   Helper: apply shading to a row element (Option B rules)
   - rowElem: the <tr> element (id = row_cToken)
   - strike: numeric strike value
   - forward: numeric forward (or null if unavailable)
   - cToken, pToken: tokens for call/put (used only for locating elements)
   ============================================================ */
function applyShadingForRow(rowElem, strike, forward, cToken, pToken) {
    if (!forward || forward <= 0) return;

    // Determine ITM/OTM
    const callIsITM = strike < forward;
    const putIsITM = strike > forward;

    // Query all cells in this row
    const cells = rowElem.querySelectorAll("td");

    // CALL block = columns 0–8 (9 cells)
    const callCells = Array.from(cells).slice(0, 9);

    // STRIKE column = index 9 (we skip shading it)
    // cells[9]

    // PUT block = columns 10–18 (9 cells)
    const putCells = Array.from(cells).slice(10, 19);

    // --- Clear old shading ---
    for (const td of callCells) td.classList.remove("itm-call");
    for (const td of putCells) td.classList.remove("itm-put");

    // --- Apply Option B shading only to ITM side ---
    if (callIsITM) {
        for (const td of callCells) td.classList.add("itm-call");
    }

    if (putIsITM) {
        for (const td of putCells) td.classList.add("itm-put");
    }

    // Optional: ATM subtle highlight
    const moneyness = strike / forward;
    const atmTolerance = 0.005;
    if (Math.abs(moneyness - 1.0) <= atmTolerance) {
        rowElem.style.boxShadow = "inset 0 0 6px rgba(0,0,0,0.06)";
    } else {
        rowElem.style.boxShadow = "";
    }
}

/* ============================================================
   Utility: update a specific cell by id with formatted number
   - id: element id
   - value: numeric or string
   - digits: number of fraction digits (optional)
   - suffix: string suffix (optional)
   - doNotClearIfZero: if true and value is zero-ish, leave it (used for OI)
   ============================================================ */
function updateCell(id, value, digits = 2, suffix = "", doNotClearIfZero = false) {
    const el = document.getElementById(id);
    if (!el) return;

    // If value is undefined or null -> clear (unless doNotClearIfZero)
    if (value === undefined || value === null) {
        el.textContent = doNotClearIfZero ? el.textContent : "";
        return;
    }

    // If numeric, format
    if (typeof value === "number") {
        if (!isFinite(value)) {
            el.textContent = "";
            return;
        }
        // For OI or integer-like values, digits may be 0
        if (digits === 0) {
            el.textContent = Math.round(value).toLocaleString("en-IN") + suffix;
        } else {
            el.textContent = value.toLocaleString("en-IN", {
                minimumFractionDigits: digits,
                maximumFractionDigits: digits
            }) + suffix;
        }
        return;
    }

    // Non-numeric (string) case
    el.textContent = String(value) + suffix;
}

/* ============================================================
   Utility: format number using en-IN with 2 decimals
   ============================================================ */
function formatNumber(x) {
    if (x === null || x === undefined || !isFinite(x)) return "-";
    return Number(x).toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/* ============================================================
   Snapshot toolbar (save snapshot / download zip)
   - re-uses your previously defined functionality
   ============================================================ */
function createSnapshotToolbar() {
    // Avoid duplicate toolbar
    if (document.getElementById("ams-toolbar")) return;

    const toolbar = document.createElement("div");
    toolbar.id = "ams-toolbar";

    // Save JSON button
    const saveBtn = document.createElement("button");
    saveBtn.textContent = "Save AMS (JSON)";
    saveBtn.onclick = () => {
        try {
            const blob = new Blob([JSON.stringify(currentAMS, null, 2)], { type: "application/json" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = `ams-snapshot-${new Date().toISOString().replace(/[:.]/g, "-")}.json`;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (err) {
            console.error("Error saving AMS snapshot:", err);
            alert("Failed to save snapshot.");
        }
    };

    // Download ZIP placeholder (keeps previous UI behavior)
    const downloadBtn = document.createElement("button");
    downloadBtn.textContent = "Download AMS ZIP";
    downloadBtn.onclick = async () => {
        try {
            // Simple single-file zip using a tiny manual method (or you can implement server-side)
            const json = JSON.stringify(currentAMS ?? {}, null, 2);
            const blob = new Blob([json], { type: "application/json" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            const now = new Date().toISOString().replace(/[:.]/g, "-");
            a.href = url;
            a.download = `ams-snapshot-${now}.json`;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (err) {
            console.error("Error generating ZIP snapshot:", err);
            alert("Failed to create ZIP file.");
        }
    };

    toolbar.appendChild(saveBtn);
    toolbar.appendChild(downloadBtn);
    document.body.appendChild(toolbar);
}

/* ============================================================
   End of file
   ============================================================ */