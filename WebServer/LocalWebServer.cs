using System.Net;
using System.Text;

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