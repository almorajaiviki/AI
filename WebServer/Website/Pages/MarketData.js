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
                <th>Index Spot</th><th>Implied Future</th>
                <th>RFR</th><th>DivYield</th><th>Last Update</th>
            </tr>
            <tr>
                <td id="IndexSpot"></td>
                <td id="ImpFut"></td>
                <td id="RFR"></td>
                <td id="DivYield"></td>
                <td id="LastUpdate"></td>
            </tr>
        </table>
    `;

    // Futures area
    const futuresDiv = document.getElementById("futures");
    if (data.futures?.length > 0) {
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
   - uses data.forwardByExpiry[expiry] for forward
   ============================================================ */
function buildOptionChainTables(data) {
    const optionDiv = document.getElementById("option-chain");
    optionDiv.innerHTML = "";

    if (!data.optionPairs?.length) {
        optionDiv.innerHTML = "<p>No option data available.</p>";
        return;
    }

    // Group optionPairs by expiry
    const groups = {};
    for (const pair of data.optionPairs) {
        const exp = pair.expiry;
        if (!groups[exp]) groups[exp] = [];
        groups[exp].push(pair);
    }

    // Render a table for each expiry (sorted ascending)
    Object.keys(groups)
        .sort((a, b) => new Date(a) - new Date(b))
        .forEach(expiry => {
            const group = groups[expiry];

            // Lookup forward for this expiry from DTO.forwardByExpiry
            let forward = null;
            if (data.forwardByExpiry && data.forwardByExpiry[expiry] !== undefined && data.forwardByExpiry[expiry] !== null) {
                forward = Number(data.forwardByExpiry[expiry]);
            }

            // Header with expiry + forward (formatted)
            const header = document.createElement("h3");
            header.className = "expiry-header";
            const expiryStr = new Date(expiry).toLocaleString("en-GB");
            const fwdStr = (forward !== null)
                ? ` | Forward: ${formatNumber(forward)}`
                : "";
            header.textContent = `Option Chain — Expiry: ${expiryStr}${fwdStr}`;
            optionDiv.appendChild(header);

            // Build table HTML
            let html = `
                <table>
                    <tr>
                        <th colspan="9">CALLS</th>
                        <th>Strike</th>
                        <th colspan="9">PUTS</th>
                    </tr>
                    <tr>
                        <th>Γ</th><th>Δ</th>
                        <th>OI</th><th>Bid</th><th>BidSprd</th><th>NPV</th>
                        <th>AskSprd</th><th>Ask</th><th>IV Used</th>
                        <th>Strike</th>
                        <th>IV Used</th><th>Bid</th><th>BidSprd</th>
                        <th>NPV</th><th>AskSprd</th><th>Ask</th><th>OI</th>
                        <th>Δ</th><th>Γ</th>
                    </tr>`;

            for (const pair of group) {
                const cToken = pair.c_token;
                const pToken = pair.p_token;
                // Strike formatting (keep as before)
                const strikeDisplay = Number(pair.strike).toLocaleString("en-IN");

                // Row id helps in updates
                html += `
                    <tr id="row_${cToken}">
                        <td id="${cToken}_gamma"></td>
                        <td id="${cToken}_delta"></td>
                        <td id="${cToken}_oi"></td>
                        <td id="${cToken}_bid"></td>
                        <td id="${cToken}_bidsprd"></td>
                        <td id="${cToken}_npv"></td>
                        <td id="${cToken}_asksprd"></td>
                        <td id="${cToken}_ask"></td>
                        <td id="${cToken}_ivused"></td>

                        <td id="${cToken}_strike" class="strike">${strikeDisplay}</td>

                        <td id="${pToken}_ivused"></td>
                        <td id="${pToken}_bid"></td>
                        <td id="${pToken}_bidsprd"></td>
                        <td id="${pToken}_npv"></td>
                        <td id="${pToken}_asksprd"></td>
                        <td id="${pToken}_ask"></td>
                        <td id="${pToken}_oi"></td>
                        <td id="${pToken}_delta"></td>
                        <td id="${pToken}_gamma"></td>
                    </tr>`;
            }

            html += "</table>";
            optionDiv.insertAdjacentHTML("beforeend", html);

            // After insertion, apply initial shading based on forward (if present)
            for (const pair of group) {
                const cToken = pair.c_token;
                const pToken = pair.p_token;
                const strikeNum = Number(pair.strike);

                const rowElem = document.getElementById(`row_${cToken}`);
                if (!rowElem) continue;

                applyShadingForRow(rowElem, strikeNum, forward, cToken, pToken);
            }
        });
}

/* ============================================================
   Update snapshot values in page
   - uses per-pair expiry to lookup forward
   ============================================================ */
function updateMarketSnapshot(data) {
    // Market info
    updateCell("IndexSpot", data.spot, 2, "", true);

    // ImpFut field was removed server-side; keep the cell empty or show dash
    const ImpFutElem = document.getElementById("ImpFut");
    if (ImpFutElem) ImpFutElem.textContent = "-";

    updateCell("RFR", (data.riskFreeRate ?? 0) * 100, 2, "%");
    updateCell("DivYield", (data.divYield ?? 0) * 100, 2, "%");
    updateCell("LastUpdate", new Date(data.snapTime).toLocaleString("en-GB", {
        year: "numeric", month: "short", day: "2-digit",
        hour: "2-digit", minute: "2-digit", second: "2-digit",
        fractionalSecondDigits: 3
    }));

    // Futures
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

    // Option pairs updates: use pair.expiry to lookup forward
    if (data.optionPairs) {
        for (const pair of data.optionPairs) {
            const cToken = pair.c_token;
            const pToken = pair.p_token;

            // Lookup forward for this pair's expiry
            let forward = null;
            if (data.forwardByExpiry && data.forwardByExpiry[pair.expiry] !== undefined && data.forwardByExpiry[pair.expiry] !== null) {
                forward = Number(data.forwardByExpiry[pair.expiry]);
            }

            // Update call side
            updateCell(`${cToken}_gamma`, pair.c_gamm ?? 0, 4);
            updateCell(`${cToken}_delta`, pair.c_delta ?? 0, 4);
            updateCell(`${cToken}_oi`, pair.c_oi ?? 0, 0, "", true);
            updateCell(`${cToken}_bid`, pair.c_bid ?? 0, 2);
            updateCell(`${cToken}_bidsprd`, pair.c_bidSpread ?? 0, 2);
            updateCell(`${cToken}_npv`, pair.c_npv ?? 0, 2);
            updateCell(`${cToken}_asksprd`, pair.c_askSpread ?? 0, 2);
            updateCell(`${cToken}_ask`, pair.c_ask ?? 0, 2);
            updateCell(`${cToken}_ivused`, pair.c_iv_used ?? 0, 2);

            // Update strike cell (string was already present but refresh it)
            const strikeCell = document.getElementById(`${cToken}_strike`);
            if (strikeCell) strikeCell.textContent = Number(pair.strike).toLocaleString("en-IN");

            // Update put side
            updateCell(`${pToken}_ivused`, pair.p_iv_used ?? 0, 2);
            updateCell(`${pToken}_bid`, pair.p_bid ?? 0, 2);
            updateCell(`${pToken}_bidsprd`, pair.p_bidSpread ?? 0, 2);
            updateCell(`${pToken}_npv`, pair.p_npv ?? 0, 2);
            updateCell(`${pToken}_asksprd`, pair.p_askSpread ?? 0, 2);
            updateCell(`${pToken}_ask`, pair.p_ask ?? 0, 2);
            updateCell(`${pToken}_oi`, pair.p_oi ?? 0, 0, "", true);
            updateCell(`${pToken}_delta`, pair.p_delta ?? 0, 4);
            updateCell(`${pToken}_gamma`, pair.p_gamm ?? 0, 4);

            // Apply shading based on forward (Option B rules)
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
    // Remove any previous shading classes
    rowElem.classList.remove("itm-call", "otm-call", "itm-put", "otm-put");

    if (!forward || forward <= 0) {
        // No forward available: do not apply ITM/OTM shading
        return;
    }

    // Determine ITM/OTM for calls and puts
    // Call is ITM if strike < forward
    // Put  is ITM if strike > forward
    const callIsITM = strike < forward;
    const putIsITM  = strike > forward;

    // Apply classes to the row element to style both sides.
    // We choose coloring so the row visually indicates which side is ITM/OTM.
    if (callIsITM) rowElem.classList.add("itm-call");
    else rowElem.classList.add("otm-call");

    if (putIsITM) rowElem.classList.add("itm-put");
    else rowElem.classList.add("otm-put");

    // If you want a stricter ATM highlight (optional), you can
    // also apply a special style when strike is very close to forward.
    const atmTolerance = 0.005; // 0.5% by default
    const moneyness = strike / forward;
    if (Math.abs(moneyness - 1.0) <= atmTolerance) {
        // Slightly emphasize ATM by making both sides stronger
        rowElem.style.boxShadow = "inset 0 0 4px rgba(0,0,0,0.06)";
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