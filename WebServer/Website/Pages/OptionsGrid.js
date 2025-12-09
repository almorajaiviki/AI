'use strict';

// === CONFIGURATION ===
const API_URL = "http://localhost:50000/api/snapshot";
const WS_URL  = "ws://localhost:50001";

// === GLOBALS ===
let optionsMap = new Map();      // internal unique symbol -> record
let optionsArray = [];           // list of CE+PE rows
let currentSort = { col: null, asc: true };

// === GLOBAL DROPDOWN FOR FILTERS ===
let filterDropdown = null;
let activeFilterInput = null;

// Each column has a filter object:
// { type: "none" | "text" | "number" | "range" | "dropdown", value: ..., raw: ... }
let columnFilters = {};

function initFilterDropdown() {
    filterDropdown = document.createElement("div");
    filterDropdown.id = "filter-dropdown";
    filterDropdown.style.position = "absolute";
    filterDropdown.style.minWidth = "120px";
    filterDropdown.style.background = "white";
    filterDropdown.style.border = "1px solid #ccc";
    filterDropdown.style.boxShadow = "0 2px 6px rgba(0,0,0,0.15)";
    filterDropdown.style.fontSize = "11px";
    filterDropdown.style.display = "none";
    filterDropdown.style.zIndex = "9999";
    filterDropdown.style.maxHeight = "200px";
    filterDropdown.style.overflowY = "auto";
    document.body.appendChild(filterDropdown);

    // Hide dropdown if clicking elsewhere
    document.addEventListener("click", (e) => {
        if (e.target !== activeFilterInput) hideFilterDropdown();
    });
}

function hideFilterDropdown() {
    filterDropdown.style.display = "none";
    activeFilterInput = null;
}

function showFilterDropdown(inputElem, items) {
    if (!items || items.length === 0) {
        hideFilterDropdown();
        return;
    }

    activeFilterInput = inputElem;

    // Clear old contents
    filterDropdown.innerHTML = "";

    items.forEach(item => {
        const opt = document.createElement("div");
        opt.textContent = item;
        opt.style.padding = "3px 6px";
        opt.style.cursor = "pointer";

        opt.addEventListener("mouseover", () => opt.style.background = "#eee");
        opt.addEventListener("mouseout", () => opt.style.background = "#fff");

        opt.addEventListener("click", () => {
            inputElem.value = item;

            // NEW: apply filter immediately
            parseAndApplyFilter(inputElem.dataset.colName || inputElem.colName, item);

            hideFilterDropdown();
        });

        filterDropdown.appendChild(opt);
    });

    // Position dropdown below input
    const rect = inputElem.getBoundingClientRect();
    filterDropdown.style.left = rect.left + "px";
    filterDropdown.style.top = (rect.bottom + window.scrollY) + "px";
    filterDropdown.style.display = "block";
}


// === INITIALIZATION ===
window.addEventListener("load", onPageLoad);

async function onPageLoad() {
    injectTableStyles();
    initFilterDropdown();

    const container = document.getElementById("grid-container");
    container.innerHTML = "<p>Loading data...</p>";

    try {
        const res = await fetch(API_URL);
        if (!res.ok) throw new Error("Snapshot fetch failed");

        const snapshot = await res.json();

        buildGridSkeleton();
        addGlobalClearButton();
        populateInitial(snapshot.optionPairs);
        startWebSocket();

        container.style.border = "1px solid #ccc";
    }
    catch (err) {
        console.error("[OptionsGrid] Load error:", err);
        container.innerHTML = `<p style="color:red;">Failed to load data: ${err.message}</p>`;
    }
}

function addGlobalClearButton() {
    const container = document.getElementById("grid-container");

    const btn = document.createElement("button");
    btn.textContent = "Clear All Filters";
    btn.style.fontSize = "11px";
    btn.style.padding = "4px 8px";
    btn.style.margin = "4px 0";
    btn.style.float = "right";
    btn.style.cursor = "pointer";

    btn.onclick = () => {
        for (const col in columnFilters) {
            columnFilters[col] = { type: "none", raw: "", value: null };
        }

        // Clear all filter boxes
        document.querySelectorAll(".filter-input").forEach(inp => {
            inp.value = "";
            inp.classList.remove("active");
            const clearIcon = inp.parentElement.querySelector(".filter-clear");
            if (clearIcon) clearIcon.style.display = "none";
        });

        applyAllFilters();
    };

    container.prepend(btn);
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
            position: relative;
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

        /* -----------------------------------------
           STICKY HEADER ROW
        ------------------------------------------*/
        #options-table thead tr#header-row th {
            position: sticky;
            top: 0;
            z-index: 20;           /* above filter row & body */
            background: #f5f5f5;
        }

        /* -----------------------------------------
           STICKY FILTER ROW
        ------------------------------------------*/
        #options-table thead tr#filter-row td {
            position: sticky;
            top: 32px;             /* height of header row */
            z-index: 19;
            background: #fafafa;   /* slightly lighter */
        }

        /* -----------------------------------------
        FILTER INPUT CLEAR ICON & ACTIVE STYLE
        ------------------------------------------*/
        .filter-input {
            position: relative;
            padding-right: 18px !important;
        }

        .filter-input.active {
            background-color: #fff8d2; /* pale yellow highlight */
        }

        .filter-clear {
            position: absolute;
            right: 4px;
            top: 2px;
            font-size: 12px;
            cursor: pointer;
            color: #888;
            display: none;
        }

        .filter-clear:hover {
            color: #333;
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

    const filterRow = document.createElement("tr");
    filterRow.id = "filter-row";

    for (let i = 0; i < headerRow.children.length; i++) {
        const th = headerRow.children[i];
        const colName = th.dataset.col;   // <-- FIXED: define colName

        const td = document.createElement("td");
        td.style.padding = "2px 4px";
        td.style.textAlign = "center";
        td.style.background = "#fafafa";

        const input = createFilterBox(colName);

        td.appendChild(input);
        filterRow.appendChild(td);

        // Initialize filter state for this column
        columnFilters[colName] = { type: "none", raw: "", value: null };
    }

    headerRow.insertAdjacentElement("afterend", filterRow);
}

/* -------------------------------------------------------------
   Helpers (Case-insensitive DTO parsing)
------------------------------------------------------------- */
function createFilterBox(colName) {
    const wrapper = document.createElement("div");
    wrapper.style.position = "relative";
    wrapper.style.width = "100%";

    const input = document.createElement("input");
    input.type = "text";
    input.style.width = "90%";
    input.style.fontSize = "11px";
    input.classList.add("filter-input");
    input.colName = colName;

    // Add clear icon
    const clearIcon = document.createElement("span");
    clearIcon.textContent = "✖";
    clearIcon.classList.add("filter-clear");

    clearIcon.onclick = (e) => {
        e.stopPropagation();
        input.value = "";
        input.classList.remove("active");
        clearIcon.style.display = "none";

        parseAndApplyFilter(colName, "");
    };

    // On typing
    input.addEventListener("input", () => {
        parseAndApplyFilter(colName, input.value);

        // highlight active
        if (input.value.trim() !== "") {
            input.classList.add("active");
            clearIcon.style.display = "block";
        } else {
            input.classList.remove("active");
            clearIcon.style.display = "none";
        }

        const items = getDropdownItemsForColumn(colName);
        showFilterDropdown(input, items);
    });

    // On focus
    input.addEventListener("focus", () => {
        const items = getDropdownItemsForColumn(colName);
        showFilterDropdown(input, items);
    });

    wrapper.appendChild(input);
    wrapper.appendChild(clearIcon);
    return wrapper;
}


function parseAndApplyFilter(colName, rawText) {
    rawText = rawText.trim();
    let filter = { type: "none", raw: rawText, value: null };

    // --- 1. Empty input → clear filter ---
    if (rawText === "") {
        columnFilters[colName] = filter;

        // Remove highlight + clear icon
        const input = document.querySelector(`#filter-row input[colname="${colName}"]`);
        if (input) {
            input.classList.remove("active");
            const clearIcon = input.parentElement.querySelector(".filter-clear");
            if (clearIcon) clearIcon.style.display = "none";
        }

        applyAllFilters();
        return;
    }

    // --- 2. Numeric COMPARE expression: >=, <=, >, <, = ---
    let cmpMatch = rawText.match(/^(>=|<=|>|<|=)\s*(\d+(\.\d+)?)$/);
    if (cmpMatch) {
        filter.type = "number";
        filter.operator = cmpMatch[1];
        filter.value = Number(cmpMatch[2]);
        columnFilters[colName] = filter;
        applyAllFilters();
        return;
    }

    // --- 3. RANGE expression: 25000-26000 ---
    let rangeMatch = rawText.match(/^(\d+(\.\d+)?)\s*-\s*(\d+(\.\d+)?)$/);
    if (rangeMatch) {
        filter.type = "range";
        filter.min = Number(rangeMatch[1]);
        filter.max = Number(rangeMatch[3]);
        columnFilters[colName] = filter;
        applyAllFilters();
        return;
    }

    // --- 3b. DATE COMPARE expression for expiry: >=2025-12-26 ---
    let dateCmp = rawText.match(/^(>=|<=|>|<|=)\s*(\d{4}-\d{2}-\d{2})$/);
    if (dateCmp && colName === "expiry") {
        filter.type = "number";
        filter.operator = dateCmp[1];
        filter.value = new Date(dateCmp[2]).getTime();
        columnFilters[colName] = filter;
        applyAllFilters();
        return;
    }

    // --- 3c. DATE RANGE expression for expiry: 2025-12-26 - 2026-01-31 ---
    let dateRange = rawText.match(/^(\d{4}-\d{2}-\d{2})\s*-\s*(\d{4}-\d{2}-\d{2})$/);
    if (dateRange && colName === "expiry") {
        filter.type = "range";
        filter.min = new Date(dateRange[1]).getTime();
        filter.max = new Date(dateRange[2]).getTime();
        columnFilters[colName] = filter;
        applyAllFilters();
        return;
    }

    // --- 3d. ADVANCED NUMERIC FILTER ---
    if (rawText.match(/[><=!,%]/)) {
        const parsed = parseAdvancedNumericFilter(rawText);
        columnFilters[colName] = {
            type: "advanced",
            conditions: parsed
        };
        applyAllFilters();
        return;
    }

    // --- 4. Else → TEXT filter ---
    filter.type = "text";
    filter.value = rawText.toLowerCase();
    columnFilters[colName] = filter;

    const input = document.querySelector(`#filter-row input[colname="${colName}"]`);
    if (input) {
        input.classList.add("active");
        const clearIcon = input.parentElement.querySelector(".filter-clear");
        if (clearIcon) clearIcon.style.display = "block";
    }
    applyAllFilters();
}

function getDropdownItemsForColumn(colName) {
    let values = new Set();

    for (const rec of optionsArray) {
        let v = null;

        switch (colName) {

            case "symbolDisplay":
                v = rec.symbolDisplay;
                break;

            case "type":
                v = rec.type;
                break;

            case "strike":
                v = rec.strike;
                break;

            case "expiry":
                v = rec.expiry;
                break;

            case "bid":
                v = rec.bidVal;
                break;

            case "bidSpread":
                v = rec.bidSpreadVal;
                break;

            case "npv":
                v = rec.npvVal;
                break;

            case "askSpread":
                v = rec.askSpreadVal;
                break;

            case "ask":
                v = rec.askVal;
                break;

            case "iv":
                v = (rec.ivVal * 100).toFixed(2);
                break;

            case "oi":
                v = rec.oiVal;
                break;

            default:
                break;
        }

        if (v !== null && v !== undefined)
            values.add(String(v));
    }

    return Array.from(values).sort();
}

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
        const dd = String(expiryDate.getDate()).padStart(2, "0");
        const mon = expiryDate.toLocaleString("en-GB", { month: "short" });
        const yyyy = expiryDate.getFullYear();
        expiryStr = `${dd}-${mon}-${yyyy}`;
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
        applyAllFilters();
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
        const dd = String(expiryDate.getDate()).padStart(2, "0");
        const mon = expiryDate.toLocaleString("en-GB", { month: "short" });
        const yyyy = expiryDate.getFullYear();
        expiryStr = `${dd}-${mon}-${yyyy}`;
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

    applyAllFilters();
}

function applyAllFilters() {
    const tbody = document.getElementById("grid-body");
    tbody.innerHTML = "";

    for (const rec of optionsArray) {
        if (recordPassesFilters(rec)) {
            tbody.appendChild(rec.row);
        }
    }
}

function recordPassesFilters(rec) {

    for (const colName in columnFilters) {
        const f = columnFilters[colName];
        if (!f || f.type === "none") continue;

        let val = null;
        let expiryTS = null;

        switch (colName) {

            case "symbolDisplay":
                val = rec.symbolDisplay.toLowerCase();
                break;

            case "type":
                val = rec.type.toLowerCase();
                break;

            case "strike":
                val = rec.strike;
                break;

            case "expiry":
                val = rec.expiry.toLowerCase();
                expiryTS = rec.expiryDate ? rec.expiryDate.getTime() : null;
                break;

            case "bid":
                val = rec.bidVal;
                break;

            case "bidSpread":
                val = rec.bidSpreadVal;
                break;

            case "npv":
                val = rec.npvVal;
                break;

            case "askSpread":
                val = rec.askSpreadVal;
                break;

            case "ask":
                val = rec.askVal;
                break;

            case "iv":
                val = rec.ivVal * 100;
                break;

            case "oi":
                val = rec.oiVal;
                break;
        }

        // TEXT MATCH
        if (f.type === "text") {
            if (!String(val).toLowerCase().includes(f.value))
                return false;
        }

        // NUMBER MATCH
        if (f.type === "number") {
            const n = (colName === "expiry")
                ? expiryTS
                : Number(val);
            if (f.operator === ">"  && !(n >  f.value)) return false;
            if (f.operator === "<"  && !(n <  f.value)) return false;
            if (f.operator === ">=" && !(n >= f.value)) return false;
            if (f.operator === "<=" && !(n <= f.value)) return false;
            if (f.operator === "="  && !(n == f.value)) return false;
        }

        // RANGE MATCH
        if (f.type === "range") {
            const n = (colName === "expiry")
                ? expiryTS
                : Number(val);
            if (n < f.min || n > f.max) return false;
        }

        /* ----------------------------------------------------
        ADVANCED NUMERIC FILTER (FIXED — INSIDE LOOP)
        -----------------------------------------------------*/
        if (f.type === "advanced") {
            let numericVal;

            if (colName === "expiry") {
                numericVal = rec.expiryDate ? rec.expiryDate.getTime() : null;
                if (!numericVal) return false;
            } else {
                numericVal = Number(val);
                if (isNaN(numericVal)) return false;
            }

            // OR conditions
            let orPass = false;

            for (const andConditions of f.conditions) {
                let andPass = true;

                for (const cond of andConditions) {
                    let v = numericVal;

                    if (cond.type === "abs") v = Math.abs(v);

                    switch (cond.operator) {
                        case ">":  if (!(v >  cond.value)) andPass = false; break;
                        case "<":  if (!(v <  cond.value)) andPass = false; break;
                        case ">=": if (!(v >= cond.value)) andPass = false; break;
                        case "<=": if (!(v <= cond.value)) andPass = false; break;
                        case "=":  if (!(v == cond.value)) andPass = false; break;
                        case "!=": if (!(v != cond.value)) andPass = false; break;
                    }
                }

                if (andPass) { 
                    orPass = true; 
                    break; 
                }
            }

            if (!orPass) return false;
        }
    }

    

    return true;
}

function parseAdvancedNumericFilter(rawText) {
    let text = rawText.trim();

    // Replace percentage (convert to number)
    text = text.replace(/%/g, "");

    // Split OR conditions ("||")
    const orParts = text.split("||").map(p => p.trim());

    const orConditions = orParts.map(part => {
        // Split AND conditions (",")
        const andParts = part.split(",").map(x => x.trim()).filter(x => x !== "");

        const andConditions = andParts.map(expr => {
            // abs > N
            if (expr.startsWith("abs")) {
                const match = expr.match(/abs\s*(>=|<=|>|<|=|!=)\s*(\d+(\.\d+)?)/);
                if (match) {
                    return {
                        type: "abs",
                        operator: match[1],
                        value: Number(match[2])
                    };
                }
            }

            // normal numeric compare
            let match = expr.match(/(>=|<=|>|<|=|!=)\s*(\d+(\.\d+)?)/);
            if (match) {
                return {
                    type: "number",
                    operator: match[1],
                    value: Number(match[2])
                };
            }

            return null;
        });

        return andConditions.filter(c => c !== null);
    });

    return orConditions;
}