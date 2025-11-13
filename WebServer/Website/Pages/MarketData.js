'use strict';

/* ============================================================
   MarketData.js
   - Front-end client for displaying AtomicMarketSnapDTO data
   - Now supports multiple expiries (one Option Chain table per expiry)
   ============================================================ */

let currentAMS = null;

// === WebSocket connection for live streaming updates ===
const socket = new WebSocket("ws://localhost:50001");

socket.onmessage = function (event) {
    try {
        const newAMS = JSON.parse(event.data);
        updateMarketSnapshot(newAMS);
        currentAMS = newAMS;
    } catch (err) {
        console.error("WebSocket parse error:", err, event.data);
    }
};

// === Fetch initial snapshot on page load ===
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
        createSnapshotToolbar();
    }
});

// === Gracefully close socket before page unload ===
window.addEventListener("beforeunload", function () {
    if (socket.readyState === WebSocket.OPEN) socket.close();
});

// === Inject CSS for tables and toolbar ===
(function addHighlightStyles() {
    const style = document.createElement("style");
    style.innerHTML = `
        .highlight-put { background-color: rgba(220,220,220,0.45); }
        .highlight-call { background-color: rgba(220,220,220,0.45); }
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
        h3.expiry-header {
            margin-top: 1rem;
            margin-bottom: 0.25rem;
            font-size: 1rem;
            font-weight: 600;
            font-family: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial;
        }
        #ams-toolbar {
            position: fixed;
            top: 12px;
            right: 12px;
            z-index: 1000;
            display: flex;
            gap: 8px;
            align-items: center;
            font-family: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial;
        }
        #ams-toolbar button {
            background: #333; color: white; border: none;
            padding: 6px 10px; border-radius: 6px; cursor: pointer;
            font-size: 12px; box-shadow: 0 1px 2px rgba(0,0,0,0.12);
        }
        #ams-toolbar button:hover { background: #111; }
    `;
    document.head.appendChild(style);
})();

/* ============================================================
   BUILD STATIC HTML LAYOUT
   ============================================================ */
function buildMarketLayout(data) {
    // === Market Info (ImpliedFuture + Expiry removed) ===
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

    // === Futures ===
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
    }

    // === Option Chain (multi-expiry) ===
    buildOptionChainTables(data);
}

/* ============================================================
   BUILD MULTI-EXPIRY OPTION CHAINS
   ============================================================ */
function buildOptionChainTables(data) {
    const optionDiv = document.getElementById("option-chain");
    optionDiv.innerHTML = "";

    if (!data.optionPairs?.length) {
        optionDiv.innerHTML = "<p>No option data available.</p>";
        return;
    }

    // === Group optionPairs by expiry ===
    const groups = {};
    for (const pair of data.optionPairs) {
        const exp = pair.expiry;
        if (!groups[exp]) groups[exp] = [];
        groups[exp].push(pair);
    }

    // === Render a table for each expiry ===
    Object.keys(groups)
        .sort((a, b) => new Date(a) - new Date(b))
        .forEach(expiry => {
            const group = groups[expiry];

            const header = document.createElement("h3");
            header.className = "expiry-header";
            header.textContent = `Option Chain — Expiry: ${new Date(expiry).toLocaleString("en-GB")}`;
            optionDiv.appendChild(header);

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
                const strike = pair.strike.toLocaleString("en-IN");

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

                        <td id="${cToken}_strike" class="strike">${strike}</td>

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
            optionDiv.innerHTML += html;
        });
}

/* ============================================================
   UPDATE SNAPSHOT VALUES
   ============================================================ */
function updateMarketSnapshot(data) {
    // === Market Info ===
    updateCell("IndexSpot", data.spot, 2, "", true);
    updateCell("RFR", data.riskFreeRate * 100, 2, "%");
    updateCell("DivYield", data.divYield * 100, 2, "%");
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

    // === Option Chains ===
    const impliedFuture = typeof data.impliedFuture === "number" ? data.impliedFuture : 0;
    if (data.optionPairs) {
        for (const pair of data.optionPairs) {
            const cToken = pair.c_token;
            const pToken = pair.p_token;

            // Call side
            updateCell(`${cToken}_gamma`, pair.c_gamma, 6);
            updateCell(`${cToken}_delta`, pair.c_delta, 4);
            updateCell(`${cToken}_oi`, pair.c_oi, 0, "", true);
            updateCell(`${cToken}_bid`, pair.c_bid, 2);
            updateCell(`${cToken}_bidsprd`, pair.c_bidSpread, 2);
            updateCell(`${cToken}_npv`, pair.c_npv, 2);
            updateCell(`${cToken}_asksprd`, pair.c_askSpread, 2);
            updateCell(`${cToken}_ask`, pair.c_ask, 2);
            updateCell(`${cToken}_ivused`, pair.c_iv * 100, 2, "%");
            updateCell(`${cToken}_strike`, pair.strike, 0, "", true);

            // Put side
            updateCell(`${pToken}_ivused`, pair.p_iv * 100, 2, "%");
            updateCell(`${pToken}_bid`, pair.p_bid, 2);
            updateCell(`${pToken}_bidsprd`, pair.p_bidSpread, 2);
            updateCell(`${pToken}_npv`, pair.p_npv, 2);
            updateCell(`${pToken}_asksprd`, pair.p_askSpread, 2);
            updateCell(`${pToken}_ask`, pair.p_ask, 2);
            updateCell(`${pToken}_oi`, pair.p_oi, 0, "", true);
            updateCell(`${pToken}_delta`, pair.p_delta, 4);
            updateCell(`${pToken}_gamma`, pair.p_gamma, 6);

            // Highlight logic (same)
            const strike = pair.strike;
            const rowElem = document.getElementById(`row_${cToken}`);
            if (rowElem) {
                const cells = rowElem.querySelectorAll("td");
                cells.forEach(td => td.classList.remove("highlight-call", "highlight-put"));
                if (strike <= impliedFuture) {
                    for (let i = 10; i < 19 && i < cells.length; i++) cells[i].classList.add("highlight-put");
                } else {
                    for (let i = 0; i < 9 && i < cells.length; i++) cells[i].classList.add("highlight-call");
                }
            }
        }
    }
}

/* ============================================================
   Utility: safe, formatted DOM cell update
   ============================================================ */
function updateCell(id, value, digits = 2, suffix = "", commaSeparated = false) {
    const cell = document.getElementById(id);
    if (!cell) return;
    if (typeof value === "number" && !isNaN(value)) {
        const formatted = commaSeparated
            ? value.toLocaleString("en-IN", { minimumFractionDigits: digits, maximumFractionDigits: digits })
            : value.toFixed(digits);
        cell.innerHTML = formatted + suffix;
    } else if (typeof value === "string") {
        cell.innerHTML = value;
    } else {
        cell.innerHTML = "";
    }
}

/* ============================================================
   Snapshot toolbar (local save as ZIP)
   ============================================================ */
function createSnapshotToolbar() {
    if (document.getElementById("ams-toolbar")) return;

    if (typeof JSZip === "undefined") {
        const script = document.createElement("script");
        script.src = "https://cdn.jsdelivr.net/npm/jszip@3.10.1/dist/jszip.min.js";
        script.onload = createSnapshotToolbar;
        document.head.appendChild(script);
        return;
    }

    const toolbar = document.createElement("div");
    toolbar.id = "ams-toolbar";

    const downloadBtn = document.createElement("button");
    downloadBtn.textContent = "Download Snapshot (.zip)";
    downloadBtn.title = "Download the current market snapshot as ZIP";
    downloadBtn.onclick = async () => {
        if (!currentAMS) {
            alert("No current snapshot available to download.");
            return;
        }
        try {
            const zip = new JSZip();
            const jsonText = JSON.stringify(currentAMS, null, 2);
            zip.file("snapshot.json", jsonText);
            const blob = await zip.generateAsync({ type: "blob", compression: "DEFLATE" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            const now = new Date().toISOString().replace(/[:.]/g, "-");
            a.href = url;
            a.download = `ams-snapshot-${now}.zip`;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (err) {
            console.error("Error generating ZIP snapshot:", err);
            alert("Failed to create ZIP file.");
        }
    };

    toolbar.appendChild(downloadBtn);
    document.body.appendChild(toolbar);
}