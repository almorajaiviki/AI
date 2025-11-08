using BrokerInterfaces;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Shoonya
{
    public sealed class BrokerWebSocketService : IBrokerWebSocketService<SubscriptionDepthAck>
    {
        private static BrokerWebSocketService? _instance;
        private static readonly object _lock = new();

        private readonly string _userId;
        private readonly string _token;
        private readonly string _wsUrl;
        private readonly ClientWebSocket _webSocket;

        public event Action<PriceFeedUpdate>? OnPriceFeedUpdate;

        public Dictionary<uint, SubscriptionDepthAck> SubscriptionDepthAcks { get; } = new();
        private bool _processFeedMessages = false;

        private BrokerWebSocketService(string userId, string token, string wsUrl)
        {
            _userId = userId ?? throw new ArgumentNullException(nameof(userId));
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _wsUrl = wsUrl ?? throw new ArgumentNullException(nameof(wsUrl));
            _webSocket = new ClientWebSocket();
        }

        public static IBrokerWebSocketService<SubscriptionDepthAck> Instance(
            string userId,
            string token,
            string wsUrl)
        {
            lock (_lock)
            {
                return _instance ??= new BrokerWebSocketService(userId, token, wsUrl);
            }
        }

        public async Task ConnectAsync()
        {
            var uri = new Uri($"{_wsUrl}?uid={_userId}&token={_token}");
            await _webSocket.ConnectAsync(uri, CancellationToken.None);
            _ = Task.Run(ReceiveLoop);
        }

        public async Task SubscribeToBatchAsync(IEnumerable<(string Exchange, uint Token)> instruments)
        {
            foreach (var batch in instruments.Chunk(500)) // 500 = Shoonya batch size assumption
            {
                var subscribeMessage = new
                {
                    t = "t", // Type: subscribe
                    k = string.Join("#", batch.Select(b => $"{b.Exchange}|{b.Token}"))
                };

                string json = JsonSerializer.Serialize(subscribeMessage);
                await _webSocket.SendAsync(
                    Encoding.UTF8.GetBytes(json),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
        }

        public void SetProcessFeedMessages(bool enable) => _processFeedMessages = enable;

        public void MarkOIFetched()
        {
            // Can be used to signal OI fetch complete, if needed
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            while (_webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                if (_processFeedMessages)
                {
                    try
                    {
                        var feed = JsonSerializer.Deserialize<PriceFeedUpdate>(json);
                        //if (feed != null)
                            OnPriceFeedUpdate?.Invoke(feed);
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed message
                    }
                }
            }
        }

        public void Dispose()
        {
            //placeholder method for now
        }
    }
}
