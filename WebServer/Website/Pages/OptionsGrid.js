'use strict';

// === CONFIGURATION ===
const API_URL = "http://localhost:50000/api/snapshot";
const WS_URL  = "ws://localhost:50001";

// === GLOBALS ===
let optionsMap = new Map();      // internal unique symbol -> record
let optionsArray = [];           // list of CE+PE rows
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

/* -------------------------------------------------------------
   STYLE
------------------------------------------------------------- */
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
        }
        #options-table tr:hover {
            background-color: #f1f1f1;
        }
        #options-table td:first-child {
            text-align: left;
        }
        #options-table td:nth-child(2),
        #options-table td:nth-child(3),
        #options-table td:nth-child(4) {
            text-align: center;
        }
    `;
    document.head.appendChild(style);
}

/* -------------------------------------------------------------
   TABLE SKELETON (now includes Type + Expiry columns)
------------------------------------------------------------- */
function buildGridSkeleton() {
    const div = document.getElementById("grid-container");
    div.innerHTML = `
        <table id="options-table">
            <thead>
                <tr id="header-row">
                    <th data-col="symbolDisplay">Symbol<span class="sort-indicator">⇅</span></th>
                    <th data-col="type">Type<span class="sort-indicator">⇅</span></th>
                    <th data-col="strike">Strike<span class="sort-indicator">⇅</span></th>
                    <th data-col="expiry">Expiry<span class="sort-indicator">⇅</span></th>
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

    addFilterRow();
}

function addFilterRow() {
    const headerRow = document.getElementById("header-row");
    if (!headerRow) return;

    // Determine how many columns exist
    const colCount = headerRow.children.length;

    // Create a new row for filters
    const filterRow = document.createElement("tr");
    filterRow.id = "filter-row";

    for (let i = 0; i < colCount; i++) {
        const th = headerRow.children[i];
        const colName = th.dataset.col || "";

        const td = document.createElement("td");
        td.style.padding = "2px 4px";
        td.style.textAlign = "center";
        td.style.background = "#fafafa";
        td.style.borderBottom = "1px solid #ddd";

        // Textbox
        const input = document.createElement("input");
        input.type = "text";
        input.placeholder = "";
        input.style.width = "75%";
        input.style.fontSize = "11px";

        // Dropdown
        const select = document.createElement("select");
        select.style.width = "75%";
        select.style.fontSize = "11px";
        select.style.marginTop = "2px";

        // Add empty option for now
        const emptyOption = document.createElement("option");
        emptyOption.value = "";
        emptyOption.textContent = "";
        select.appendChild(emptyOption);

        // Stack textbox + dropdown vertically
        td.appendChild(input);
        td.appendChild(document.createElement("br"));
        td.appendChild(select);

        filterRow.appendChild(td);
    }

    // Insert filter row directly AFTER header row
    headerRow.insertAdjacentElement("afterend", filterRow);
}

/* -------------------------------------------------------------
   Helpers (Case-insensitive DTO parsing)
------------------------------------------------------------- */
function normalizeKeys(obj) {
    const m = {};
    if (!obj) return m;
    for (const k of Object.keys(obj))
        m[k.toLowerCase()] = obj[k];
    return m;
}

function readNumeric(norm, key) {
    if (key in norm) {
        const n = Number(norm[key]);
        return { value: isFinite(n) ? n : 0, present: true };
    }
    return { value: 0, present: false };
}

function readAny(norm, key) {
    if (key in norm) return { value: norm[key], present: true };
    return { value: undefined, present: false };
}

/* -------------------------------------------------------------
   POPULATE INITIAL
------------------------------------------------------------- */
function populateInitial(optionPairs) {
    const tbody = document.getElementById("grid-body");
    optionsArray = [];
    optionsMap.clear();
    tbody.innerHTML = "";

    if (!optionPairs) return;

    for (const pair of optionPairs) {
        const n = normalizeKeys(pair);
        addRow(n, pair.strike, "c");
        addRow(n, pair.strike, "p");
    }

    for (const rec of optionsArray)
        tbody.appendChild(rec.row);
}

/* -------------------------------------------------------------
   ADD ROW FOR CE/PE
------------------------------------------------------------- */
function addRow(norm, strike, side) {
    const isCall = (side === "c");
    const type = isCall ? "CE" : "PE";

    // Read expiry
    const expiryInfo = readAny(norm, "expiry");
    let expiryDate = null, expiryStr = "-";
    if (expiryInfo.present) {
        expiryDate = new Date(expiryInfo.value);
        expiryStr = expiryDate.toLocaleDateString("en-GB", {
            day: "2-digit", month: "short", year: "numeric"
        });
    }

    // Unique internal key
    const symbolKey = `${type}_${strike}_${expiryStr.replace(/[^0-9A-Za-z]/g, "")}`;

    // User-facing display text
    const symbolDisplay = `${type} ${strike} ${expiryStr}`;

    // Extract numeric fields
    const bid        = readNumeric(norm, `${side}_bid`);
    const ask        = readNumeric(norm, `${side}_ask`);
    const npv        = readNumeric(norm, `${side}_npv`);
    const iv         = readNumeric(norm, `${side}_iv`);
    const oi         = readNumeric(norm, `${side}_oi`);
    const bidSpread  = readNumeric(norm, `${side}_bidspread`);
    const askSpread  = readNumeric(norm, `${side}_askspread`);

    const rec = {
        symbolKey,
        symbolDisplay,
        type,
        strike,
        expiry: expiryStr,
        expiryDate,

        bidVal: bid.value,         bidPresent: bid.present,
        askVal: ask.value,         askPresent: ask.present,
        npvVal: npv.value,         npvPresent: npv.present,
        ivVal: iv.value,           ivPresent: iv.present,
        oiVal: oi.value,           oiPresent: oi.present,
        bidSpreadVal: bidSpread.value,   bidSpreadPresent: bidSpread.present,
        askSpreadVal: askSpread.value,   askSpreadPresent: askSpread.present,

        row: null
    };

    // Build row HTML
    const tr = document.createElement("tr");
    tr.id = symbolKey;

    tr.innerHTML = `
        <td>${symbolDisplay}</td>
        <td>${type}</td>
        <td>${strike}</td>
        <td>${expiryStr}</td>
        <td>${rec.bidPresent ? rec.bidVal.toFixed(2) : "-"}</td>
        <td>${rec.bidSpreadPresent ? rec.bidSpreadVal.toFixed(2) : "-"}</td>
        <td>${rec.npvPresent ? rec.npvVal.toFixed(2) : "-"}</td>
        <td>${rec.askSpreadPresent ? rec.askSpreadVal.toFixed(2) : "-"}</td>
        <td>${rec.askPresent ? rec.askVal.toFixed(2) : "-"}</td>
        <td>${rec.ivPresent ? (rec.ivVal * 100).toFixed(2) : "-"}</td>
        <td>${rec.oiPresent ? rec.oiVal.toLocaleString("en-IN") : "0"}</td>
    `;

    rec.row = tr;

    optionsArray.push(rec);
    optionsMap.set(symbolKey, rec);
}

/* -------------------------------------------------------------
   WEBSOCKET UPDATES
------------------------------------------------------------- */
function startWebSocket() {
    const socket = new WebSocket(WS_URL);

    socket.onmessage = event => {
        let msg;
        try { msg = JSON.parse(event.data); }
        catch { return; }

        if (!msg.optionPairs) return;

        for (const pair of msg.optionPairs) {
            const n = normalizeKeys(pair);
            updateRow(n, pair.strike, "c");
            updateRow(n, pair.strike, "p");
        }

        if (currentSort.col)
            sortBy(currentSort.col, null, false);
    };
}

function updateRow(norm, strike, side) {
    const isCall = (side === "c");
    const type = isCall ? "CE" : "PE";

    // Resolve expiry for this row key
    const expiryInfo = readAny(norm, "expiry");
    let expiryDate = null, expiryStr = "-";
    if (expiryInfo.present) {
        expiryDate = new Date(expiryInfo.value);
        expiryStr = expiryDate.toLocaleDateString("en-GB", {
            day: "2-digit", month: "short", year: "numeric"
        });
    }

    const symbolKey = `${type}_${strike}_${expiryStr.replace(/[^0-9A-Za-z]/g, "")}`;
    const rec = optionsMap.get(symbolKey);
    if (!rec) return;

    // update numeric fields
    function upd(field, key) {
        const v = readNumeric(norm, key);
        rec[field + "Val"] = v.value;
        rec[field + "Present"] = v.present;
    }

    upd("bid", `${side}_bid`);
    upd("ask", `${side}_ask`);
    upd("npv", `${side}_npv`);
    upd("iv",  `${side}_iv`);
    upd("oi",  `${side}_oi`);
    upd("bidSpread", `${side}_bidspread`);
    upd("askSpread", `${side}_askspread`);

    // update DOM cells
    const c = rec.row.children;
    c[4].textContent = rec.bidPresent ? rec.bidVal.toFixed(2) : "-";
    c[5].textContent = rec.bidSpreadPresent ? rec.bidSpreadVal.toFixed(2) : "-";
    c[6].textContent = rec.npvPresent ? rec.npvVal.toFixed(2) : "-";
    c[7].textContent = rec.askSpreadPresent ? rec.askSpreadVal.toFixed(2) : "-";
    c[8].textContent = rec.askPresent ? rec.askVal.toFixed(2) : "-";
    c[9].textContent = rec.ivPresent ? (rec.ivVal * 100).toFixed(2) : "-";
    c[10].textContent = rec.oiPresent ? rec.oiVal.toLocaleString("en-IN") : "0";
}

/* -------------------------------------------------------------
   SORTING
------------------------------------------------------------- */
function sortBy(col, headerElem = null, toggle = true) {

    if (toggle) {
        if (currentSort.col === col) currentSort.asc = !currentSort.asc;
        else currentSort = { col, asc: true };
    }

    // header visual
    document.querySelectorAll("#header-row th").forEach(th => th.classList.remove("active"));
    document.querySelectorAll("#header-row .sort-indicator").forEach(span => span.textContent = "⇅");

    if (headerElem) {
        headerElem.classList.add("active");
        headerElem.querySelector(".sort-indicator").textContent =
            currentSort.asc ? "▲" : "▼";
    }

    const dir = currentSort.asc ? 1 : -1;

    optionsArray.sort((a, b) => {
        let va, vb;

        switch (col) {
            case "symbolDisplay":
                return a.symbolDisplay.localeCompare(b.symbolDisplay) * dir;

            case "type":
                return a.type.localeCompare(b.type) * dir;

            case "strike":
                va = a.strike; vb = b.strike;
                break;

            case "expiry":
                va = a.expiryDate ? a.expiryDate.getTime() : 0;
                vb = b.expiryDate ? b.expiryDate.getTime() : 0;
                break;

            case "bid":
                va = a.bidVal; vb = b.bidVal;
                break;

            case "ask":
                va = a.askVal; vb = b.askVal;
                break;

            case "npv":
                va = a.npvVal; vb = b.npvVal;
                break;

            case "bidSpread":
                va = a.bidSpreadVal; vb = b.bidSpreadVal;
                break;

            case "askSpread":
                va = a.askSpreadVal; vb = b.askSpreadVal;
                break;

            case "iv":
                va = a.ivVal; vb = b.ivVal;
                break;

            case "oi":
                va = a.oiVal; vb = b.oiVal;
                break;

            default:
                va = 0; vb = 0;
        }

        return (Number(va) - Number(vb)) * dir;
    });

    const tbody = document.getElementById("grid-body");
    for (const rec of optionsArray)
        tbody.appendChild(rec.row);
}