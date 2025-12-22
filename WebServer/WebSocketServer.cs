using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
//using MessagePack;


namespace WebSocketServer
{
    public sealed class WebsocketServer : IDisposable
    {
        private static readonly object _lock = new();
        private static WebsocketServer? _instance;
        // === Add this near the top of the class ===
        internal static readonly JsonSerializerOptions _fastJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        internal static readonly JsonSerializerOptions _semanticJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never, // 🔑
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static WebsocketServer Instance(string uriPrefix)
        {
            lock (_lock)
            {
                return _instance ??= new WebsocketServer(uriPrefix);
            }
        }

        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<WebSocket, byte> _clients = new();
        private readonly CancellationTokenSource _cts = new();

        // Broadcast queue
        private readonly Channel<string> _broadcastChannel = 
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        private WebsocketServer(string uriPrefix)
        {
            _listener.Prefixes.Add(uriPrefix);
            Console.WriteLine($"[WebSocketServer] Created at {uriPrefix}");

            Task.Run(StartAsync);
            Task.Run(ProcessBroadcastQueueAsync);
        }

        private async Task StartAsync()
        {
            _listener.Start();
            Console.WriteLine($"[WebSocketServer] Listening...");

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
                }
            }
            catch (HttpListenerException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebSocketServer] Listener error: {ex}");
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                WebSocket socket = wsContext.WebSocket;

                _clients.TryAdd(socket, 0);
                Console.WriteLine($"[WebSocketServer] Client connected. Total: {_clients.Count}");

                await MonitorConnectionAsync(socket);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebSocketServer] WS handshake error: {ex}");
            }
        }

        private async Task MonitorConnectionAsync(WebSocket socket)
        {
            var buffer = new byte[4 * 1024];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", _cts.Token);
                    }
                }
            }
            catch { }
            finally
            {
                CleanupSocket(socket);
                Console.WriteLine("[WebSocketServer] Client disconnected");
            }
        }

        // 🔹 Public method to enqueue data
        /* public void EnqueueBroadcast<T>(T data)
        {
            try
            {
                string json = JsonSerializer.Serialize(data);
                if (!_broadcastChannel.Writer.TryWrite(json))
                {
                    Console.WriteLine("[WebSocketServer] Broadcast queue full, dropping message");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebSocketServer] Enqueue error: {ex.Message}");
            }
        } */
        public void EnqueueBroadcast<T>(T data, string type)
        {
            try
            {
                var wsMsg = new WsMessage<T>(
                    Type: type,
                    Data: data
                );
                string json = JsonSerializer.Serialize(wsMsg, _semanticJsonOptions);
                if (!_broadcastChannel.Writer.TryWrite(json))
                {
                    Console.WriteLine("[WebSocketServer] Broadcast queue full, dropping message");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebSocketServer] Enqueue error: {ex.Message}");
            }
        }


        // 🔹 Single background agent to broadcast queued messages
        private async Task ProcessBroadcastQueueAsync()
        {
            Console.WriteLine("[WebSocketServer] Broadcast agent started");
            await foreach (var json in _broadcastChannel.Reader.ReadAllAsync(_cts.Token))
            {
                await BroadcastJsonAsync(json);
            }
        }

        private async Task BroadcastJsonAsync(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);
            List<WebSocket> toRemove = new();

            foreach (var client in _clients.Keys)
            {
                if (client.State != WebSocketState.Open)
                {
                    toRemove.Add(client);
                    continue;
                }

                try
                {
                    await client.SendAsync(segment, WebSocketMessageType.Text, true, _cts.Token);
                }
                catch (WebSocketException)
                {
                    toRemove.Add(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebSocketServer] Broadcast error: {ex.Message}");
                }
            }

            foreach (var c in toRemove)
                CleanupSocket(c);
        }

        private void CleanupSocket(WebSocket socket)
        {
            if (_clients.TryRemove(socket, out _))
            {
                try { socket.Abort(); } catch { }
            }
        }

        public async Task ShutdownAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            Console.WriteLine("[WebSocketServer] Shutting down...");
            _broadcastChannel.Writer.TryComplete();

            var tasks = new List<Task>();
            foreach (var client in _clients.Keys)
            {
                if (client.State == WebSocketState.Open)
                {
                    tasks.Add(client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None));
                }
            }
            await Task.WhenAll(tasks);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
            _cts.Dispose();
        }
    }

    public sealed record WsMessage<T>(
        string Type,
        T Data
    );
}