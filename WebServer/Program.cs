using System.Text.Json;
using Zerodha;
using MarketData;
using BrokerInterfaces;
using System.Net.WebSockets;
using QuantitativeAnalytics;
using WebSocketServer;

namespace Server
{
    class Server
    {
        // NOTE: parameter is BrokerInterfaces.PriceFeedUpdate (the websocket event type).
        // Return type is MarketData.PriceUpdate (the MarketData struct).
        private static PriceUpdate ConvertToMarketDataUpdate(PriceFeedUpdate feed)
        {
            return new PriceUpdate(
                feed.Token,
                feed.LastTradedPrice,
                feed.BidPrice,
                feed.AskPrice,
                feed.OpenInterest
            );
        }

        private static (double rfr, double q, double OICutoff, bool bUseMktFuture, CancellationTokenSource cts, LocalWebServer? webServer, WebSocketServer.WebsocketServer? webSocketServer) InitializeApplication()
        {
            double rfr = 0.054251;
            Console.WriteLine($"Received rfr: {rfr}");

            double q = 0.0137;
            Console.WriteLine($"Received q: {q:P2}");

            double OICutoff = 1000000;

            bool bUseMktFuture = true;

            // Graceful shutdown setup
            var cts = new CancellationTokenSource();
            /* Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("Ctrl+C pressed, shutting down...");
                e.Cancel = true; // prevent immediate kill
                cts.Cancel();
            }; */

            IBrokerWebSocketService<ZerodhaSubscriptionDepthAck>? brokerWebSocketService = null;
            LocalWebServer? webServer = null;
            WebSocketServer.WebsocketServer? webSocketServer = null;

            return (rfr, q, OICutoff, bUseMktFuture, cts, webServer, webSocketServer);
        }
        
        private static async void SubscribeToBrokerWebSocket(
            IBrokerWebSocketService<ZerodhaSubscriptionDepthAck> brokerWebSocketService,
            MarketData.MarketData marketData)
        {
            try
            {
                await brokerWebSocketService.ConnectAsync();
                Console.WriteLine("✅ WebSocket connected successfully");

                // Subscribe to options tokens
                var optionTokens = marketData.AtomicSnapshot.OptionsByToken.Keys;
                await brokerWebSocketService.SubscribeToBatchAsync(
                    optionTokens.Select(t => ("NFO", t))
                );
                Console.WriteLine($"✅ Subscribed to {optionTokens.Count()} option tokens");

                // Subscribe to index token
                var indexToken = marketData.AtomicSnapshot.Token;
                await brokerWebSocketService.SubscribeToBatchAsync(
                    new List<(string, uint)> { ("NSE", indexToken) }
                );
                Console.WriteLine("✅ Subscribed to index token");

                //subscribe to future tokens
                var FutureToken = marketData.AtomicSnapshot.FuturesByToken.Keys;
                await brokerWebSocketService.SubscribeToBatchAsync(
                    FutureToken.Select(t => ("NFO", t))
                );
                Console.WriteLine($"✅ Subscribed to {FutureToken.Count()} future tokens");

            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"⚠️ WebSocket connection failed: {ex.Message}");
                Console.WriteLine("Continuing without real-time data updates...");
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"⚠️ WebSocket connection timeout: {ex.Message}");
                Console.WriteLine("Continuing without real-time data updates...");
            }
            
        }

        private static (LocalWebServer webServer, WebsocketServer webSocketServer) StartWebServers(string webPrefix, string wsPrefix, MarketData.MarketData marketData)
        {
            var webServer = LocalWebServer.Instance(webPrefix, marketData);
            webServer.Start();
            Console.WriteLine("Web server started at " + webPrefix);
            
            var webSocketServer = WebsocketServer.Instance(wsPrefix);
            Console.WriteLine("WebSocket server started at " + wsPrefix);

            return (webServer, webSocketServer);
        }


        private static void SetupMarketDataFlow(
            IBrokerWebSocketService<ZerodhaSubscriptionDepthAck> brokerWebSocketService,
            MarketData.MarketData marketData,
            WebsocketServer webSocketServer)
        {
            brokerWebSocketService.OnPriceFeedUpdate += (PriceFeedUpdate brokerUpdate) =>
            {
                try 
                {
                    var marketDataUpdate = ConvertToMarketDataUpdate(brokerUpdate);
                    marketData.HandlePriceUpdate(marketDataUpdate);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OnPriceFeedUpdate] Error: {ex.Message}");
                }
            };

            brokerWebSocketService.SetProcessFeedMessages(true);

            // MarketData → WebSocket broadcast
            SubscribeMarketDataToWebSocket(marketData, webSocketServer);
        }

        private static async Task RunApplicationLoop(CancellationTokenSource cts)
        {
            await Task.Run(() =>
            {
                string? input;
                do
                {
                    input = Console.ReadLine()?.Trim();
                } while (!string.Equals(input, "x", StringComparison.OrdinalIgnoreCase) &&
                         !cts.Token.IsCancellationRequested);
                //cancel the token. this will trigger stopping all market data updates etc.
                cts.Cancel();
            });
        }

        private static void HandleMainExceptions(Exception ex)
        {
            switch (ex)
            {
                case InvalidOperationException ioe when ioe.Message.Contains("not available"):
                    Console.WriteLine($"❌ Authentication Error: {ioe.Message}");
                    Console.WriteLine("Please check your API credentials and ensure login was successful.");
                    break;
                case HttpRequestException hre:
                    Console.WriteLine($"❌ Network Error: {hre.Message}");
                    Console.WriteLine("Please check your internet connection and Zerodha API status.");
                    break;
                case TaskCanceledException tce when tce.InnerException is TimeoutException:
                    Console.WriteLine($"❌ Timeout Error: Operation timed out");
                    Console.WriteLine("The broker API is taking too long to respond. Try again later.");
                    break;
                case JsonException je:
                    Console.WriteLine($"❌ Data Format Error: {je.Message}");
                    Console.WriteLine("Received invalid data from broker API.");
                    break;
                case FileNotFoundException fnfe when fnfe.Message.Contains("appsettings.json"):
                    Console.WriteLine($"❌ Configuration Error: {fnfe.Message}");
                    Console.WriteLine("Please ensure appsettings.json exists with valid API credentials.");
                    break;
                default:
                    Console.WriteLine($"❌ Unexpected Error: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    Console.WriteLine("Please report this error if it persists.");
                    break;
            }
        }

        public static async Task Main(string[] args)
        {
            var (rfr, q, OICutoff, bUseMktFuture, cts, localWebServer, localWebSocketServer) = InitializeApplication();

            var creds = LoadConfiguration();
            var httpClient = new HttpClient();
            var authService = ZerodhaAuthService.Instance(httpClient); // Use 'var' to infer type
            
            string apiKey = creds.ApiKey;
            string apiSecret = creds.ApiSecret;

            IBrokerWebSocketService<ZerodhaSubscriptionDepthAck>? brokerWebSocketService = null;
            MarketData.MarketData? marketData = null;   // ✅ declare here so it's visible in finally

            try
            {
                // Login
                await LoginToBroker(authService, apiKey, apiSecret);

                // Market data creation
                marketData = await GetInstrumentsAndMarketData(authService, httpClient, rfr, OICutoff, bUseMktFuture, q, DateTime.Now, cts.Token);

                // Connect to the broker websocket service
                brokerWebSocketService = BrokerWebSocketService.Instance(
                    authService.CurrentAuthResponse?.Data?.UserId
                        ?? throw new InvalidOperationException("User ID not available"),
                    authService.CurrentAuthRequest?.ApiKey
                        ?? throw new InvalidOperationException("API key not available"),
                    authService.CurrentAuthResponse?.Token
                        ?? throw new InvalidOperationException("Auth token not available")
                    );
                SubscribeToBrokerWebSocket(brokerWebSocketService, marketData);

                //string prefix = "http://localhost:50000/";
                string webPrefix = "http://localhost:50000/";
                string wsPrefix  = "http://localhost:50001/";  // different port

                (localWebServer, localWebSocketServer) = StartWebServers(webPrefix, wsPrefix, marketData);

                // Broker updates → MarketData
                SetupMarketDataFlow(brokerWebSocketService, marketData, localWebSocketServer);

                // Wait for exit signal (x or Ctrl+C)
                await RunApplicationLoop(cts);

                Console.WriteLine("Shutting down...");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not available"))
            {
                HandleMainExceptions(ex);
            }
            catch (HttpRequestException ex)
            {
                HandleMainExceptions(ex);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                HandleMainExceptions(ex);
            }
            catch (JsonException ex)
            {
                HandleMainExceptions(ex);
            }
            catch (FileNotFoundException ex) when (ex.Message.Contains("appsettings.json"))
            {
                HandleMainExceptions(ex);
            }
            catch (Exception ex)
            {
                HandleMainExceptions(ex);
            }
            finally
            {
                // ✅ Cancel the global token (stops MarketData background updater)
                cts.Cancel();
                cts.Dispose();

                // ✅ Ensure graceful teardown of MarketData, broker, and servers
                await PerformApplicationCleanup(
                    authService,
                    brokerWebSocketService,
                    localWebServer,
                    localWebSocketServer,
                    httpClient,
                    marketData // 👈 pass it here
                );
            }
        }

        private static async Task PerformApplicationCleanup(
        IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> authService,
        IBrokerWebSocketService<ZerodhaSubscriptionDepthAck>? brokerWebSocketService,
        LocalWebServer? webServer,
        WebSocketServer.WebsocketServer? webSocketServer,
        HttpClient httpClient,
        MarketData.MarketData? marketData = null)
        {
            Console.WriteLine("\n🟠 Initiating graceful shutdown sequence...\n");

            try
            {
                // ✅ Stop MarketData gracefully first
                if (marketData != null)
                {
                    Console.WriteLine("⏳ Stopping MarketData background updater...");
                    await marketData.StopAsync();
                    Console.WriteLine("✅ MarketData updater stopped cleanly.");
                }
                else
                {
                    Console.WriteLine("ℹ️ MarketData object was null — skipping stop step.");
                }

                // ✅ Attempt broker logout next
                Console.WriteLine("⏳ Logging out from broker...");
                bool bLogout = await authService.LogoutAsync();
                Console.WriteLine(bLogout ? "✅ Broker logout successful." : "⚠️ Broker logout failed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Logout or cleanup error: {ex.Message}");
            }

            // ✅ Dispose remaining components
            try
            {
                Console.WriteLine("⏳ Disposing connections and servers...");

                brokerWebSocketService?.Dispose();

                if (webSocketServer != null)
                {
                    await webSocketServer.ShutdownAsync();
                    Console.WriteLine("✅ WebSocket server shut down.");
                }

                if (webServer != null)
                {
                    webServer.Stop();
                    Console.WriteLine("✅ Local web server stopped.");
                }

                httpClient?.Dispose();
                authService?.Dispose();

                Console.WriteLine("\n🟢 ✅ Application cleanup completed successfully. All resources released.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during final resource disposal: {ex.Message}");
            }
        }
        
        private static async Task LoginToBroker(
            IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> authService,
            string apiKey,
            string apiSecret)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("API Key is required but not provided in configuration");

            if (string.IsNullOrWhiteSpace(apiSecret))
                throw new InvalidOperationException("API Secret is required but not provided in configuration");

            Console.Write("Please enter your Request Token: ");
            string requestToken = Console.ReadLine()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(requestToken))
                throw new InvalidOperationException("Request Token is required for authentication");

            var request = new ZerodhaAuthRequest
            {
                ApiKey = apiKey,
                RequestToken = requestToken,
                ApiSecret = apiSecret
            };

            try
            {
                var response = await authService.LoginAsync(request);

                if (response.IsSuccess)
                {
                    Console.WriteLine($"✅ Login successful. User: {response.Data?.UserName ?? "Unknown"}");
                    Console.WriteLine($"Session token: {response.Token?[..8]}...");
                }
                else
                {
                    throw new InvalidOperationException($"Login failed: {response.Status}");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Network error during login: {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid response format from broker API: {ex.Message}", ex);
            }
        }

        private static async Task<MarketData.MarketData> GetInstrumentsAndMarketData(
            IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> authService,
            HttpClient httpClient,
            double rfr, double OICutoff, bool bUseMktFuture,
            double q,
            DateTime now,
            CancellationToken token)
        {
            var marketInfo = MarketHelperDict.MarketHelperDict.MarketInfoDict.First().Value;
            var instrumentProvider = BrokerInstrumentService.Instance();
            var marketDataGenerator = new MarketDataGenerator(instrumentProvider, marketInfo);

            var (indexInstrument, options, futures, latestExpiry) = await instrumentProvider.GetOptionsForIndexAsync(
                marketInfo.IndexSymbol,
                marketInfo.IndexSymbol_Options, 
                now
            );

            IBrokerQuoteFetcher<ZerodhaAuthRequest, ZerodhaAuthResponse> quoteFetcher =
                ZerodhaQuoteFetcher.Instance(authService, httpClient);

            var indexQuotes = await quoteFetcher.FetchQuotesAsync(
                new List<QuoteRequest>
                {
                    new QuoteRequest(indexInstrument.Exchange, indexInstrument.Token.ToString(), indexInstrument.TradingSymbol)
                }
            );
            var indexQuote = indexQuotes.First();

            var indexObj = new MarketData.Index(
                indexInstrument.TradingSymbol,
                indexInstrument.Token,
                indexQuote.LastTradedPrice,
                marketInfo.NSECalendar,
                new RFR(rfr),
                q,
                latestExpiry,
                now
            );

            var filteredOptions = options.Where(o =>
                o.StrikePrice >= indexObj.GetSnapshot().IndexSpot * (1 + marketInfo.LowerStrikePct) &&
                o.StrikePrice <= indexObj.GetSnapshot().IndexSpot * (1 + marketInfo.UpperStrikePct)
            ).ToList();

            var optionQuoteRequests = filteredOptions
                .Select(o => new QuoteRequest(o.Exchange, o.Token.ToString(), o.TradingSymbol))
                .ToList();
            
            var futureQuoteRequests = futures
                .Select(o => new QuoteRequest(o.Exchange, o.Token.ToString(), o.TradingSymbol))
                .ToList();
            
            var optionQuotes = await quoteFetcher.FetchQuotesAsync(optionQuoteRequests);

            var optionOI = optionQuotes
                .Where(q => uint.TryParse(q.Token, out _))
                .ToDictionary(q => uint.Parse(q.Token), q => q.OpenInterest);

            var futureQuotes = await quoteFetcher.FetchQuotesAsync(futureQuoteRequests);
            
            var futureOI = futureQuotes
                .Where(q => uint.TryParse(q.Token, out _))
                .ToDictionary(q => uint.Parse(q.Token), q => q.OpenInterest);

            var volatilityModel = VolatilityModel.Black76;
            return marketDataGenerator.GenerateMarketData(
                rfr, OICutoff, bUseMktFuture,
                now,
                options,
                optionQuotes,
                futures,
                futureQuotes,
                optionOI,
                futureOI,
                volatilityModel,
                indexObj,
                token
            );
        }

        private static void SubscribeMarketDataToWebSocket(
            MarketData.MarketData marketData,
            WebSocketServer.WebsocketServer webSocketServer)
        {
            marketData.OnMarketDataUpdated += snap =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        webSocketServer.EnqueueBroadcast<AtomicMarketSnapDTO>(snap);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Subscription] WebSocket broadcast failed: {ex.Message}");
                    }
                });
            };
        }

        private static BrokerCredentials LoadConfiguration()
        {
            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

            if (!File.Exists(configFilePath))
                throw new FileNotFoundException($"Configuration file not found at: {configFilePath}", configFilePath);

            try
            {
                string json = File.ReadAllText(configFilePath);

                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidOperationException("Configuration file is empty");

                var credentials = JsonSerializer.Deserialize<BrokerCredentials>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (credentials == null)
                    throw new InvalidOperationException("Failed to parse configuration file");

                if (string.IsNullOrWhiteSpace(credentials.ApiKey))
                    throw new InvalidOperationException("ApiKey is missing or empty in configuration");

                if (string.IsNullOrWhiteSpace(credentials.ApiSecret))
                    throw new InvalidOperationException("ApiSecret is missing or empty in configuration");

                Console.WriteLine("✅ Configuration loaded successfully");
                return credentials;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid JSON format in configuration file: {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Access denied reading configuration file: {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Error reading configuration file: {ex.Message}", ex);
            }
        }
    }

    public class AppConfig
    {
        public BrokerCredentials BrokerCredentials { get; set; } = new BrokerCredentials();
    }

    public class BrokerCredentials
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }
}