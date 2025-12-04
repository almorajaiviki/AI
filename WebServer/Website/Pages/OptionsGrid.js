'use strict';

// === CONFIGURATION ===
const API_URL = "http://localhost:50000/api/snapshot";
const WS_URL  = "ws://localhost:50001";

// === GLOBALS ===
let optionsMap = new Map();      // tradingSymbol → record
let optionsArray = [];           // flat list of CE + PE
let currentSort = { col: null, asc: true };

// === INITIALIZATION ===
window.addEventListener("load", onPageLoad);

async function onPageLoad() {
    injectTableStyles();

    const container = document.getElementById("grid-container");
    container.innerHTML = "<p>Loading data...</p>";

    try {
        const res = await fetch(API_URL);
        if (!res.ok) throw new Error("Snapshot fetch failed");

        const snapshot = await res.json();

        buildGridSkeleton();
        populateInitial(snapshot.optionPairs);
        startWebSocket();

        container.style.border = "1px solid #ccc";
    }
    catch (err) {
        console.error("[OptionsGrid] Load error:", err);
        container.innerHTML = `<p style="color:red;">Failed to load data: ${err.message}</p>`;
    }
}

/* ---------------------------------------------------------
    STYLE
--------------------------------------------------------- */
function injectTableStyles() {
    const style = document.createElement("style");
    style.innerHTML = `
        #options-table {
            border-collapse: collapse;
            width: 100%;
            font-family: monospace;
            font-size: 12px;
        }
        #options-table th, #options-table td {
            border: 1px solid #ddd;
            padding: 4px 6px;
            text-align: right;
        }
        #options-table th {
            background: #f5f5f5;
            font-weight: 600;
            cursor: pointer;
            text-align: center;
            position: relative;
        }
        #options-table tr:hover {
            background-color: #f1f1f1;
        }
        #options-table td:first-child,
        #options-table td:nth-child(2) {
            text-align: left;
        }
        #options-table td:nth-child(3) {
            text-align: center;
        }
        .sort-indicator {
            margin-left: 4px;
        }
    `;
    document.head.appendChild(style);
}

/* ---------------------------------------------------------
    TABLE SKELETON
--------------------------------------------------------- */
function buildGridSkeleton() {
    const div = document.getElementById("grid-container");
    div.innerHTML = `
        <table id="options-table">
            <thead>
                <tr id="header-row">
                    <th data-col="symbol">Symbol<span class="sort-indicator">⇅</span></th>
                    <th data-col="type">Type<span class="sort-indicator">⇅</span></th>
                    <th data-col="strike">Strike<span class="sort-indicator">⇅</span></th>
                    <th data-col="bid">Bid<span class="sort-indicator">⇅</span></th>
                    <th data-col="bidSpread">BidSprd<span class="sort-indicator">⇅</span></th>
                    <th data-col="npv">NPV<span class="sort-indicator">⇅</span></th>
                    <th data-col="askSpread">AskSprd<span class="sort-indicator">⇅</span></th>
                    <th data-col="ask">Ask<span class="sort-indicator">⇅</span></th>
                    <th data-col="iv">IV%<span class="sort-indicator">⇅</span></th>
                    <th data-col="oi">OI<span class="sort-indicator">⇅</span></th>
                </tr>
            </thead>
            <tbody id="grid-body"></tbody>
        </table>
    `;

    document.querySelectorAll("#header-row th").forEach(th => {
        th.addEventListener("click", () => sortBy(th.dataset.col, th));
    });
}

/* ---------------------------------------------------------
    POPULATE INITIAL SNAPSHOT
    Compatible with new OptionPairDTO
--------------------------------------------------------- */
function populateInitial(optionPairs) {
    const tbody = document.getElementById("grid-body");
    tbody.innerHTML = "";
    optionsArray = [];
    optionsMap.clear();

    if (!optionPairs) return;

    for (const p of optionPairs) {
        addOrUpdateRow_fromPair(p, "C");
        addOrUpdateRow_fromPair(p, "P");
    }

    // Append rows to DOM
    for (const rec of optionsArray) tbody.appendChild(rec.row);
}

/* ---------------------------------------------------------
    Convert one side of OptionPairDTO into row
--------------------------------------------------------- */
function addOrUpdateRow_fromPair(pair, sideLetter) {
    const isCall = sideLetter === "C";
    const symbol = `${pair.strike}${isCall ? "CE" : "PE"}`;

    // Extract fields with default fallbacks to avoid undefined errors
    const bid        = Number(pair[sideLetter + "_bid"]        ?? 0);
    const ask        = Number(pair[sideLetter + "_ask"]        ?? 0);
    const oi         = Number(pair[sideLetter + "_oi"]         ?? 0);
    const iv         = Number(pair[sideLetter + "_iv"]         ?? 0);
    const npv        = Number(pair[sideLetter + "_npv"]        ?? 0);
    const bidSpread  = pair[sideLetter + "_bidSpread"];
    const askSpread  = pair[sideLetter + "_askSpread"];

    const rec = {
        symbol,
        type: isCall ? "CE" : "PE",
        strike: pair.strike,
        bid,
        bidSpread: (typeof bidSpread === "number") ? bidSpread : null,
        npv,
        askSpread: (typeof askSpread === "number") ? askSpread : null,
        ask,
        iv,
        oi,
        row: null
    };

    const tr = document.createElement("tr");
    tr.id = symbol;

    tr.innerHTML = `
        <td>${symbol}</td>
        <td>${rec.type}</td>
        <td>${rec.strike}</td>
        <td>${rec.bid.toFixed(2)}</td>
        <td>${rec.bidSpread !== null ? rec.bidSpread.toFixed(2) : "-"}</td>
        <td>${rec.npv.toFixed(2)}</td>
        <td>${rec.askSpread !== null ? rec.askSpread.toFixed(2) : "-"}</td>
        <td>${rec.ask.toFixed(2)}</td>
        <td>${(rec.iv * 100).toFixed(2)}</td>
        <td>${rec.oi.toLocaleString("en-IN")}</td>
    `;

    rec.row = tr;
    optionsArray.push(rec);
    optionsMap.set(symbol, rec);
}

/* ---------------------------------------------------------
    WEBSOCKET
--------------------------------------------------------- */
function startWebSocket() {
    const socket = new WebSocket(WS_URL);
    socket.onopen = () => console.log("[OptionsGrid] WebSocket connected.");
    socket.onclose = () => console.log("[OptionsGrid] WebSocket closed.");
    socket.onerror = e => console.error("[OptionsGrid] WebSocket error:", e);

    socket.onmessage = event => {
        const json = JSON.parse(event.data);
        if (!json || !json.optionPairs) return;

        for (const p of json.optionPairs) {
            applyUpdate(p, "C");
            applyUpdate(p, "P");
        }

        if (currentSort.col) sortBy(currentSort.col, null, false);
    };
}

function applyUpdate(pair, sideLetter) {
    const isCall = sideLetter === "C";
    const symbol = `${pair.strike}${isCall ? "CE" : "PE"}`;

    const rec = optionsMap.get(symbol);
    if (!rec) return;

    // Update data fields
    rec.bid = pair[sideLetter + "_bid"];
    rec.ask = pair[sideLetter + "_ask"];
    rec.npv = pair[sideLetter + "_npv"];
    rec.bidSpread = pair[sideLetter + "_bidSpread"];
    rec.askSpread = pair[sideLetter + "_askSpread"];
    rec.iv = pair[sideLetter + "_iv"];
    rec.oi = pair[sideLetter + "_oi"];

    // Update DOM cells
    const c = rec.row.children;
    c[3].textContent = rec.bid.toFixed(2);
    c[4].textContent = rec.bidSpread?.toFixed(2) ?? "-";
    c[5].textContent = rec.npv.toFixed(2);
    c[6].textContent = rec.askSpread?.toFixed(2) ?? "-";
    c[7].textContent = rec.ask.toFixed(2);
    c[8].textContent = (rec.iv * 100).toFixed(2);
    c[9].textContent = rec.oi.toLocaleString("en-IN");
}

/* ---------------------------------------------------------
    SORTING
--------------------------------------------------------- */
function sortBy(col, headerElem = null, toggle = true) {
    if (toggle) {
        if (currentSort.col === col) currentSort.asc = !currentSort.asc;
        else currentSort = { col, asc: true };
    }

    // Reset header UI
    document.querySelectorAll("#header-row th").forEach(th => th.classList.remove("active"));
    document.querySelectorAll("#header-row .sort-indicator").forEach(span => span.textContent = "⇅");

    if (headerElem) {
        headerElem.classList.add("active");
        headerElem.querySelector(".sort-indicator").textContent =
            currentSort.asc ? "▲" : "▼";
    }

    const dir = currentSort.asc ? 1 : -1;

    optionsArray.sort((a, b) => {
        const va = a[col], vb = b[col];

        if (["symbol", "type"].includes(col))
            return va.localeCompare(vb) * dir;

        return (va - vb) * dir;
    });

    const tbody = document.getElementById("grid-body");
    for (const r of optionsArray) tbody.appendChild(r.row);
}