'use strict';

// === CONFIGURATION ===
const API_URL = "http://localhost:50000/api/snapshot";
const WS_URL  = "ws://localhost:50001";

// === GLOBALS ===
let optionsMap = new Map();
let optionsArray = [];
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
    } catch (err) {
        console.error("[OptionsGrid] Load error:", err);
        container.innerHTML = `<p style='color:red;'>Failed to load data: ${err.message}</p>`;
    }
}

// === STYLE INJECTION ===
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
        #options-table th .sort-indicator {
            margin-left: 4px;
            color: #bbb;
        }
        #options-table th.active .sort-indicator {
            color: #333;
            font-weight: bold;
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
    `;
    document.head.appendChild(style);
}

// === GRID BUILD ===
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

// === POPULATE INITIAL SNAPSHOT ===
function populateInitial(optionPairs) {
    const tbody = document.getElementById("grid-body");
    optionsArray = [];
    optionsMap.clear();
    tbody.innerHTML = "";

    if (!optionPairs) return;

    for (const pair of optionPairs) {
        for (const side of ["call", "put"]) {
            const o = pair[side];
            const g = pair[`${side}Greeks`];
            const s = pair[`${side}Spreads`];
            if (!o || !g) continue;

            const tr = document.createElement("tr");
            tr.id = o.tradingSymbol;
            tr.innerHTML = `
                <td>${o.tradingSymbol}</td>
                <td>${o.optionType}</td>
                <td>${o.strike.toLocaleString("en-IN")}</td>
                <td>${o.bid.toFixed(2)}</td>
                <td>${s ? s.bidSpread.toFixed(2) : "-"}</td>
                <td>${g.npv.toFixed(2)}</td>
                <td>${s ? s.askSpread.toFixed(2) : "-"}</td>
                <td>${o.ask.toFixed(2)}</td>
                <td>${(g.iv_Used * 100).toFixed(2)}</td>
                <td>${o.oi.toLocaleString("en-IN")}</td>
            `;

            const rec = {
                symbol: o.tradingSymbol,
                type: o.optionType,
                strike: o.strike,
                bid: o.bid,
                bidSpread: s ? s.bidSpread : null,
                npv: g.npv,
                askSpread: s ? s.askSpread : null,
                ask: o.ask,
                iv: g.iv_Used,
                oi: o.oi,
                row: tr
            };

            optionsArray.push(rec);
            optionsMap.set(o.tradingSymbol, rec);
            tbody.appendChild(tr);
        }
    }
}

// === WEBSOCKET UPDATES ===
function startWebSocket() {
    const socket = new WebSocket(WS_URL);
    socket.onopen = () => console.log("[OptionsGrid] WebSocket connected.");
    socket.onclose = () => console.log("[OptionsGrid] WebSocket closed.");
    socket.onerror = e => console.error("[OptionsGrid] WebSocket error:", e);

    socket.onmessage = event => {
        const json = JSON.parse(event.data);
        if (!json || !json.optionPairs) return;

        for (const pair of json.optionPairs) {
            for (const side of ["call", "put"]) {
                const o = pair[side];
                const g = pair[`${side}Greeks`];
                const s = pair[`${side}Spreads`];
                if (!o || !g) continue;

                const rec = optionsMap.get(o.tradingSymbol);
                if (rec) {
                    rec.bid = o.bid;
                    rec.ask = o.ask;
                    rec.npv = g.npv;
                    rec.bidSpread = s ? s.bidSpread : null;
                    rec.askSpread = s ? s.askSpread : null;
                    rec.iv = g.iv_Used;
                    rec.oi = o.oi;

                    const cells = rec.row.children;
                    cells[3].textContent = o.bid.toFixed(2);
                    cells[4].textContent = s ? s.bidSpread.toFixed(2) : "-";
                    cells[5].textContent = g.npv.toFixed(2);
                    cells[6].textContent = s ? s.askSpread.toFixed(2) : "-";
                    cells[7].textContent = o.ask.toFixed(2);
                    cells[8].textContent = (g.iv_Used * 100).toFixed(2);
                    cells[9].textContent = o.oi.toLocaleString("en-IN");
                }
            }
        }

        if (currentSort.col) sortBy(currentSort.col, null, false);
    };
}

// === SORTING ===
function sortBy(col, headerElem = null, toggle = true) {
    if (toggle) {
        if (currentSort.col === col) currentSort.asc = !currentSort.asc;
        else currentSort = { col, asc: true };
    }

    // Reset header indicators
    document.querySelectorAll("#header-row th").forEach(th => th.classList.remove("active"));
    document.querySelectorAll("#header-row .sort-indicator").forEach(span => span.textContent = "⇅");

    // Highlight active header
    if (headerElem) {
        headerElem.classList.add("active");
        const indicator = headerElem.querySelector(".sort-indicator");
        indicator.textContent = currentSort.asc ? "▲" : "▼";
    } else if (currentSort.col) {
        const activeTh = document.querySelector(`#header-row th[data-col='${currentSort.col}']`);
        if (activeTh) {
            activeTh.classList.add("active");
            const indicator = activeTh.querySelector(".sort-indicator");
            indicator.textContent = currentSort.asc ? "▲" : "▼";
        }
    }

    const key = col;
    const dir = currentSort.asc ? 1 : -1;

    optionsArray.sort((a, b) => {
        const va = a[key], vb = b[key];

        if (["symbol", "type"].includes(key)) {
            return va.localeCompare(vb, "en", { sensitivity: "base" }) * dir;
        }

        const na = parseFloat(va);
        const nb = parseFloat(vb);
        if (isNaN(na) && isNaN(nb)) return 0;
        if (isNaN(na)) return 1;
        if (isNaN(nb)) return -1;
        return (na - nb) * dir;
    });

    const tbody = document.getElementById("grid-body");
    for (const rec of optionsArray) tbody.appendChild(rec.row);
}