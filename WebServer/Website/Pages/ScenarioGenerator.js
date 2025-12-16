window.addEventListener("load", onPageLoad);

async function onPageLoad() {
    injectStyles();

    try {
        const resp = await fetch("/api/scenario/snapshot");
        const data = await resp.json();

        console.log("Scenario snapshot:", data);
        renderScenarioGenerator(data);
    } catch (err) {
        console.error("Failed to load scenario snapshot", err);
    }
}

/* =========================================================
   Styling (JS-driven, like your other pages)
   ========================================================= */
function injectStyles() {
    const style = document.createElement("style");
    style.innerHTML = `
        body {
            font-family: Arial, sans-serif;
            margin: 12px;
        }

        h1, h2 {
            margin: 8px 0;
        }

        .scenario-name {
            margin-bottom: 12px;
        }

        .expiry-header {
            cursor: pointer;
            background: #eee;
            padding: 6px;
            margin-top: 10px;
            font-weight: bold;
            border: 1px solid #ccc;
        }

        .expiry-body {
            margin-bottom: 10px;
        }

        table {
            border-collapse: collapse;
            margin-top: 6px;
        }

        th, td {
            border: 1px solid #ccc;
            padding: 4px 6px;
            text-align: center;
        }

        input[type="number"] {
            width: 60px;
        }

        .futures-container {
            display: flex;
            gap: 16px;
            flex-wrap: wrap;
            margin-bottom: 15px;
        }

        .future-box {
            border: 2px solid #444;
            padding: 8px 10px;
            min-width: 160px;
            text-align: center;
        }

        .future-expiry {
            font-weight: bold;
            margin-bottom: 6px;
        }

        .options-container {
            display: flex;
            gap: 16px;
            align-items: flex-start;
            overflow-x: auto;
            margin-bottom: 20px;
        }

        .expiry-column {
            border: 2px solid #aaa;
            padding: 6px;
            min-width: 220px;
        }

        .expiry-column.collapsed .expiry-body {
            display: none;
        }

        .expiry-header-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            font-weight: bold;
            background: #eee;
            padding: 4px;
            border: 1px solid #ccc;
            cursor: pointer;
        }

        .expiry-toggle {
            font-size: 12px;
            border: 1px solid #666;
            padding: 0 6px;
            cursor: pointer;
            user-select: none;
        }

    `;
    document.head.appendChild(style);
}

/* =========================================================
   UI Rendering
   ========================================================= */
function renderScenarioGenerator(data) {
    const app = document.getElementById("app");
    app.innerHTML = "";

    // Title
    const title = document.createElement("h1");
    title.innerText = "Scenario Generator";
    app.appendChild(title);

    // -------------------------------
    // Scenario name
    // -------------------------------
    const nameDiv = document.createElement("div");
    nameDiv.className = "scenario-name";

    const nameLabel = document.createElement("label");
    nameLabel.innerText = "Scenario Name: ";

    const nameInput = document.createElement("input");
    nameInput.type = "text";
    nameInput.id = "scenarioName";

    nameDiv.appendChild(nameLabel);
    nameDiv.appendChild(nameInput);
    app.appendChild(nameDiv);

    // -------------------------------
    // Existing Scenarios
    // -------------------------------
    if (data.scenarios && data.scenarios.length > 0) {
        const h = document.createElement("h2");
        h.innerText = "Scenarios";
        app.appendChild(h);

        const ul = document.createElement("ul");
        data.scenarios.forEach(name => {
            const li = document.createElement("li");
            li.innerText = name;
            ul.appendChild(li);
        });

        app.appendChild(ul);
    }

    // -------------------------------
    // Futures (horizontal boxed layout)
    // -------------------------------
    if (data.futures && data.futures.length > 0) {
        const futHeader = document.createElement("h2");
        futHeader.innerText = "Futures";
        app.appendChild(futHeader);

        const container = document.createElement("div");
        container.className = "futures-container";

        data.futures.forEach(expiry => {
            const box = document.createElement("div");
            box.className = "future-box";

            const expDiv = document.createElement("div");
            expDiv.className = "future-expiry";
            expDiv.innerText = formatDate(expiry);

            const input = document.createElement("input");
            input.type = "number";
            input.className = "future-lots";
            input.dataset.expiry = expiry;

            box.appendChild(expDiv);
            box.appendChild(input);
            container.appendChild(box);
        });

        app.appendChild(container);

        // -------------------------------
        // Create Scenario button
        // -------------------------------
        const btn = document.createElement("button");
        btn.innerText = "Create Scenario";
        btn.onclick = collectAndLogScenario;
        btn.style.marginTop = "15px";

        app.appendChild(btn);

        
    }

    


    if (data.options && data.options.length > 0) {
        const optHeader = document.createElement("h2");
        optHeader.innerText = "Options";
        app.appendChild(optHeader);

        const optionsContainer = document.createElement("div");
        optionsContainer.className = "options-container";

        data.options.forEach(group => {
            const column = document.createElement("div");
            column.className = "expiry-column";

            // ---------- Header row ----------
            const headerRow = document.createElement("div");
            headerRow.className = "expiry-header-row";

            const title = document.createElement("span");
            title.innerText = formatDate(group.expiry);

            const toggle = document.createElement("span");
            toggle.className = "expiry-toggle";
            toggle.innerText = "−"; // expanded by default

            headerRow.appendChild(title);
            headerRow.appendChild(toggle);

            // ---------- Body ----------
            const body = document.createElement("div");
            body.className = "expiry-body";

            const table = document.createElement("table");
            const hdr = document.createElement("tr");
            hdr.innerHTML = "<th>Call</th><th>Strike</th><th>Put</th>";
            table.appendChild(hdr);

            group.strikes.forEach(strike => {
                const tr = document.createElement("tr");

                const tdCall = document.createElement("td");
                const callInput = document.createElement("input");
                callInput.type = "number";
                callInput.className = "option-lots";
                callInput.dataset.expiry = group.expiry;
                callInput.dataset.strike = strike;
                callInput.dataset.side = "CE";
                tdCall.appendChild(callInput);

                const tdStrike = document.createElement("td");
                tdStrike.innerText = strike;

                const tdPut = document.createElement("td");
                const putInput = document.createElement("input");
                putInput.type = "number";
                putInput.className = "option-lots";
                putInput.dataset.expiry = group.expiry;
                putInput.dataset.strike = strike;
                putInput.dataset.side = "PE";
                tdPut.appendChild(putInput);

                tr.appendChild(tdCall);
                tr.appendChild(tdStrike);
                tr.appendChild(tdPut);
                table.appendChild(tr);
            });

            body.appendChild(table);

            // ---------- Toggle logic ----------
            headerRow.onclick = () => {
                const collapsed = column.classList.toggle("collapsed");
                toggle.innerText = collapsed ? "+" : "−";
            };

            column.appendChild(headerRow);
            column.appendChild(body);
            optionsContainer.appendChild(column);
        });

        app.appendChild(optionsContainer);
    }
}

function collectAndLogScenario() {
    const scenarioName = document.getElementById("scenarioName").value.trim();

    if (!scenarioName) {
        alert("Scenario name is required");
        return;
    }

    // -------------------------------
    // Collect options
    // -------------------------------
    const optionMap = new Map(); 
    // key = expiry|strike → { expiry, strike, callLots, putLots }

    document.querySelectorAll(".option-lots").forEach(input => {
        const expiry = input.dataset.expiry;
        const strike = Number(input.dataset.strike);
        const side = input.dataset.side;
        const lots = parseInt(input.value || "0", 10);

        if (lots === 0) return;

        const key = `${expiry}|${strike}`;
        if (!optionMap.has(key)) {
            optionMap.set(key, {
                expiry: expiry,
                strike: strike,
                callLots: 0,
                putLots: 0
            });
        }

        const obj = optionMap.get(key);
        if (side === "CE") obj.callLots = lots;
        if (side === "PE") obj.putLots = lots;
    });

    const options = Array.from(optionMap.values());

    // -------------------------------
    // Collect futures
    // -------------------------------
    const futures = [];

    document.querySelectorAll(".future-lots").forEach(input => {
        const lots = parseInt(input.value || "0", 10);
        if (lots === 0) return;

        futures.push({
            expiry: input.dataset.expiry,
            lots: lots
        });
    });

    if (options.length === 0 && futures.length === 0) {
        alert("No instruments selected");
        return;
    }

    const payload = {
        scenarioName: scenarioName,
        options: options,
        futures: futures
    };

    (async () => {
        try {
            const resp = await fetch("/api/scenario/create", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!resp.ok) {
                throw new Error("Scenario creation failed");
            }

            const data = await resp.json();
            console.log("Updated scenario snapshot:", data);

            renderScenarioGenerator(data);
        } catch (err) {
            console.error("Failed to create scenario", err);
            alert("Failed to create scenario. See console for details.");
        }
    })();
}

/* =========================================================
   Helpers
   ========================================================= */
function formatDate(iso) {
    const d = new Date(iso);
    return d.toLocaleDateString() + " " + d.toLocaleTimeString();
}