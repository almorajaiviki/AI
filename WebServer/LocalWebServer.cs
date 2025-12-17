using System.Net;
using System.Text;
using RiskGen;

namespace Server
{
    public sealed class LocalWebServer
    {
        private readonly HttpListener _listener;
        private readonly string _baseFolder;
        private static string? HomePageCache = null;
        private readonly MarketData.MarketData _marketData;

        private static LocalWebServer? _instance;
        private static readonly object _lock = new();

        public static LocalWebServer Instance(string uriPrefix, MarketData.MarketData marketData)
        {
            lock (_lock)
            {
                return _instance ??= new LocalWebServer(uriPrefix, marketData);
            }
        }

        private LocalWebServer(string uriPrefix, MarketData.MarketData marketData)
        {
            if (string.IsNullOrEmpty(uriPrefix))
                throw new ArgumentException("URI prefix cannot be null or empty.", nameof(uriPrefix));
            _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));

            _listener = new HttpListener();
            _listener.Prefixes.Add(uriPrefix);
            _baseFolder = Path.Combine(Directory.GetCurrentDirectory(), "Website/Pages/");
            Console.WriteLine("[LocalWebServer] Initialized.");
        }

        public void Start()
        {
            _listener.Start();
            Console.WriteLine("[LocalWebServer] Started.");

            Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        _ = Task.Run(() => ProcessRequestAsync(context));
                    }
                    catch (HttpListenerException) { break; }
                    catch (InvalidOperationException) { break; }
                }
            });
        }

        public void Stop()
        {
            _listener.Stop();
            Console.WriteLine("[LocalWebServer] Stopped.");
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                string rawUrl = request.RawUrl ?? "/";
                byte[] msg;
                string contentType = "text/plain";

                if (rawUrl.StartsWith("/api/snapshot", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    contentType = "application/json";

                    var snapshotDto = _marketData.AtomicSnapshot.ToDTO();
                    //string json = System.Text.Json.JsonSerializer.Serialize(snapshotDto);
                    string json = System.Text.Json.JsonSerializer.Serialize(snapshotDto, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    msg = Encoding.UTF8.GetBytes(json);
                }
                else
                { 
                    if (rawUrl.StartsWith("/api/scenario/snapshot", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        contentType = "application/json";

                        var snap = _marketData.AtomicSnapshot;

                        ScenarioSnapshotDto dto;

                        if (snap == null)
                        {
                            dto = new ScenarioSnapshotDto(
                                Options: Array.Empty<OptionExpiryStrikesDto>(),
                                Futures: Array.Empty<DateTime>(),
                                Scenarios: Array.Empty<string>()
                            );
                        }
                        else
                        {
                            // -------------------------------
                            // Options: expiry → strikes
                            // -------------------------------
                            var optionGroups = snap
                                .OptionsByTradingSymbol
                                .Values
                                .GroupBy(o => o.Expiry)
                                .Select(g => new OptionExpiryStrikesDto(
                                    Expiry: g.Key,
                                    Strikes: g
                                        .Select(o => o.Strike)
                                        .Distinct()
                                        .OrderBy(s => s)
                                        .ToList()
                                ))
                                .OrderBy(o => o.Expiry)
                                .ToList();

                            // -------------------------------
                            // Futures: expiries only
                            // -------------------------------
                            var futures = snap
                                .FuturesByTradingSymbol
                                .Values
                                .Select(f => f.FutureSnapshot.Expiry)
                                .Distinct()
                                .OrderBy(e => e)
                                .ToList();

                            // -------------------------------
                            // Scenario names
                            // -------------------------------
                            var scenarioNames = ScenarioOrchestrator.Instance
                                .GetAllScenarios()
                                .Keys
                                .OrderBy(n => n)
                                .ToList();

                            dto = new ScenarioSnapshotDto(
                                Options: optionGroups,
                                Futures: futures,
                                Scenarios: scenarioNames
                            );
                        }

                        string json = System.Text.Json.JsonSerializer.Serialize(
                            dto,
                            new System.Text.Json.JsonSerializerOptions
                            {
                                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });

                        msg = Encoding.UTF8.GetBytes(json);
                    }                
                    else
                    {
                        if (
                                rawUrl.StartsWith("/api/scenario/create", StringComparison.OrdinalIgnoreCase) && request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                            )
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.OK;
                            contentType = "application/json";

                            // 1️⃣ Collect atomic market snapshot
                            var snap = _marketData.AtomicSnapshot;

                            if (snap == null)
                            {
                                throw new InvalidOperationException("AtomicMarketSnap is not available.");
                            }

                            // 2️⃣ Read request body (raw JSON)
                            string body;
                            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                            {
                                body = await reader.ReadToEndAsync();
                            }

                            if (string.IsNullOrWhiteSpace(body))
                            {
                                throw new InvalidOperationException("Empty scenario create request body.");
                            }

                            // 3️⃣ Deserialize JSON payload
                            var createRequest = System.Text.Json.JsonSerializer.Deserialize<
                                ScenarioCreateRequestInternal
                            >(body, new System.Text.Json.JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (createRequest == null)
                            {
                                throw new InvalidOperationException("Failed to deserialize scenario create request.");
                            }

                            // 4️⃣ Collect option inputs
                            var optionInputs = new List<OptionInput>();

                            if (createRequest.Options != null)
                            {
                                foreach (var o in createRequest.Options)
                                {
                                    if (o.CallLots != 0)
                                    {
                                        var callSnap = snap.OptionsByTradingSymbol
                                            .Values
                                            .FirstOrDefault(x =>
                                                x.Expiry == o.Expiry &&
                                                x.Strike == o.Strike &&
                                                x.OptionType == InstrumentStatic.OptionType.CE
                                            );

                                        if (string.IsNullOrEmpty(callSnap.TradingSymbol))
                                            throw new InvalidOperationException(
                                                $"Call option not found for {o.Expiry}, strike {o.Strike}"
                                            );

                                        optionInputs.Add(new OptionInput(
                                            TradingSymbol: callSnap.TradingSymbol,
                                            OptionType: InstrumentStatic.OptionType.CE,
                                            Strike: o.Strike,
                                            Expiry: o.Expiry,
                                            Lots: o.CallLots
                                        ));
                                    }

                                    if (o.PutLots != 0)
                                    {
                                        var putSnap = snap.OptionsByTradingSymbol
                                            .Values
                                            .FirstOrDefault(x =>
                                                x.Expiry == o.Expiry &&
                                                x.Strike == o.Strike &&
                                                x.OptionType == InstrumentStatic.OptionType.PE
                                            );

                                        if (string.IsNullOrEmpty(putSnap.TradingSymbol))
                                            throw new InvalidOperationException(
                                                $"Put option not found for {o.Expiry}, strike {o.Strike}"
                                            );

                                        optionInputs.Add(new OptionInput(
                                            TradingSymbol: putSnap.TradingSymbol,
                                            OptionType: InstrumentStatic.OptionType.PE,
                                            Strike: o.Strike,
                                            Expiry: o.Expiry,
                                            Lots: o.PutLots
                                        ));
                                    }
                                }
                            }

                            // 5️⃣ Collect future inputs
                            var futureInputs = new List<FutureInput>();

                            if (createRequest.Futures != null)
                            {
                                foreach (var f in createRequest.Futures)
                                {
                                    if (f.Lots == 0)
                                        continue;

                                    var futureSnap = snap.FuturesByTradingSymbol
                                        .Values
                                        .FirstOrDefault(x => x.FutureSnapshot.Expiry == f.Expiry);

                                    if (futureSnap == null)
                                        throw new InvalidOperationException(
                                            $"No future found for expiry {f.Expiry}"
                                        );

                                    futureInputs.Add(new FutureInput(
                                        TradingSymbol: futureSnap.FutureSnapshot.TradingSymbol,
                                        Expiry: f.Expiry,
                                        Lots: f.Lots
                                    ));
                                }
                            }

                            // 6️⃣ Create scenario
                            ScenarioOrchestrator.Instance.CreateScenario(
                                createRequest.ScenarioName,
                                optionInputs,
                                futureInputs,
                                snap
                            );

                            // 7️⃣ Return response (which is entire page refreshed with new scenario)
                            // Return updated scenario snapshot (same as GET)

                            var optionGroups = snap
                                .OptionsByTradingSymbol
                                .Values
                                .GroupBy(o => o.Expiry)
                                .Select(g => new OptionExpiryStrikesDto(
                                    Expiry: g.Key,
                                    Strikes: g
                                        .Select(o => o.Strike)
                                        .Distinct()
                                        .OrderBy(s => s)
                                        .ToList()
                                ))
                                .OrderBy(o => o.Expiry)
                                .ToList();

                            var futures = snap
                                .FuturesByTradingSymbol
                                .Values
                                .Select(f => f.FutureSnapshot.Expiry)
                                .Distinct()
                                .OrderBy(e => e)
                                .ToList();

                            var scenarioNames = ScenarioOrchestrator.Instance
                                .GetAllScenarios()
                                .Keys
                                .OrderBy(n => n)
                                .ToList();

                            var dto = new ScenarioSnapshotDto(
                                Options: optionGroups,
                                Futures: futures,
                                Scenarios: scenarioNames
                            );

                            string json = System.Text.Json.JsonSerializer.Serialize(
                                dto,
                                new System.Text.Json.JsonSerializerOptions
                                {
                                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                                });

                            msg = Encoding.UTF8.GetBytes(json);

                        }
                            else
                            {   if (rawUrl.StartsWith("/api/scenario/view", StringComparison.OrdinalIgnoreCase) && request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                                {
                                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                                    contentType = "application/json";

                                    var scenarios = ScenarioOrchestrator.Instance.GetAllScenarios();

                                    var scenarioDtos = new List<ScenarioDto>();

                                    foreach (var kvp in scenarios)
                                    {
                                        var scenarioName = kvp.Key;
                                        var scenario = kvp.Value;

                                        var tradeDtos = new List<ScenarioTradeDto>();

                                        foreach (var trade in scenario.Trades)
                                        {
                                            var greeks = scenario.TradeGreeks[trade];

                                            tradeDtos.Add(new ScenarioTradeDto(
                                                TradingSymbol: trade.Instrument.TradingSymbol,
                                                Lots: trade.Lots,
                                                Quantity: trade.Lots * trade.Instrument.LotSize,
                                                NPV: greeks.NPV,
                                                Delta: greeks.Delta,
                                                Gamma: greeks.Gamma,
                                                Vega: greeks.Vega,
                                                Theta: greeks.Theta,
                                                Rho: greeks.Rho
                                            ));
                                        }

                                        scenarioDtos.Add(new ScenarioDto(
                                            ScenarioName: scenarioName,
                                            Trades: tradeDtos
                                        ));
                                    }

                                    var dto = new ScenarioViewerSnapshotDto(
                                        Scenarios: scenarioDtos
                                    );

                                    string json = System.Text.Json.JsonSerializer.Serialize(
                                        dto,
                                        new System.Text.Json.JsonSerializerOptions
                                        {
                                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                                        });

                                    msg = Encoding.UTF8.GetBytes(json);                                    
                                }
                                else
                                {
                                    string filename = Path.GetFileName(rawUrl);
                                    filename = string.IsNullOrEmpty(filename) ? "HomePage.html" : filename;
    
                                    string path = Path.Combine(_baseFolder, filename);
                                    if (!File.Exists(path))
                                    {
                                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                                        msg = Encoding.UTF8.GetBytes("404 - Page not found.");
                                    }
                                    else
                                    {
                                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                                        contentType = filename switch
                                        {
                                            string f when f.EndsWith(".js") => "text/javascript",
                                            string f when f.EndsWith(".css") => "text/css",
                                            string f when f.EndsWith(".html") => "text/html",
                                            _ => "application/octet-stream"
                                        };
    
                                        msg = await File.ReadAllBytesAsync(path);
                                    }
                                }
                            }
                    }
                }

                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = msg.Length;
                await context.Response.OutputStream.WriteAsync(msg, 0, msg.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LocalWebServer] Error: " + ex.Message);
            }
        }
    }
}