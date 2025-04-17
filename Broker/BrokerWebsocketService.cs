using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Broker
{
    public class BrokerWebSocketService
    {
        private readonly ClientWebSocket _webSocket;
        private readonly string _userId;
        private readonly string _accountId;
        private readonly string _token;
        private readonly Uri _webSocketUri = new(ApiEndpoints.WebSocket);
        public readonly ConcurrentDictionary<uint, SubscriptionAck> _subscriptionAcks = new();
        private bool _bIsOIFetched = false;
        private readonly Lock _flagLock = new();

        private Action<string>? _onAckJsonReceived; // Delegate for tick handling

        public void MarkOIFetched()
        {
            lock (_flagLock)
            {
                _bIsOIFetched = true;
                _onAckJsonReceived = SubsribtionAck;
            }
        }

        public BrokerWebSocketService(string userId, string accountId, string token)
        {
            _userId = userId ?? throw new ArgumentNullException(nameof(userId));
            _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _webSocket = new ClientWebSocket();
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            await _webSocket.ConnectAsync(_webSocketUri, ct);
            var connectMsg = new ConnectMessage("c", _userId, _accountId, _token);
            string json = JsonSerializer.Serialize(connectMsg);
            await SendAsync(json, ct);

            var buffer = new byte[1024];
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            string response = Encoding.UTF8.GetString(buffer, 0, result.Count);

            var connectResponse = JsonSerializer.Deserialize<ConnectResponse>(response);
            if (connectResponse?.Status?.ToUpper() != "OK")
            {
                throw new Exception($"WebSocket connection failed: {connectResponse?.Status}");
            }

            _ = Task.Run(() => ReceiveMessagesAsync(ct));  // Start processing messages
        }

        private async Task SendAsync(string message, CancellationToken ct = default)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private async Task ReceiveMessagesAsync(CancellationToken ct = default)
        {
            var buffer = new byte[4096];

            while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    RouteMessage(json);
                }
            }
        }

        private void RouteMessage(string json)
        {
            try
            {
                var check = JsonSerializer.Deserialize<AllResponsesCheck>(json);
                bool isOIFetched;

                //lock (_flagLock)
                {
                    isOIFetched = _bIsOIFetched;
                }

                switch (check?.Type?.ToLower())
                {
                    case "dk":
                    case "tk":
                        if (!isOIFetched)
                        {
                            var ack = JsonSerializer.Deserialize<SubscriptionAck>(json);
                            if (ack?.Token != null && uint.TryParse(ack.Token, out uint token))
                            {   
                                _subscriptionAcks[token] = ack;                                                                       
                            }
                        }
                        else
                        {
                            _onAckJsonReceived?.Invoke(json);
                        }
                        break;

                    case "am":
                        var alert = JsonSerializer.Deserialize<AlertMessage>(json);
                        Console.WriteLine($"⚠️ Broker Alert: {alert?.Message}");
                        break;

                    default:
                        if (isOIFetched)
                        {
                            //do nothing
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RouteMessage error: {ex.Message}");
            }
        }
        private void SubsribtionAck (string json)
        {
            //do nothing
        }
        public async Task SubscribeAsync(string exchange, uint token, CancellationToken ct = default)
        {
            if (_webSocket.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected.");

            var key = $"{exchange}|{token}";
            var subscribe = new Subscribe("d", key); // 'd' for depth/touchline feed
            string json = JsonSerializer.Serialize(subscribe);
            await SendAsync(json, ct);
        }
        public async Task SubscribeToBatchAsync(IEnumerable<(string Exchange, uint Token)> instruments, CancellationToken ct = default)
        {
            if (_webSocket.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected.");

            foreach (var (exchange, token) in instruments)
            {
                var key = $"{exchange}|{token}";
                var subscribe = new Subscribe("d", key);
                string json = JsonSerializer.Serialize(subscribe);
                await SendAsync(json, ct);
                await Task.Delay(10, ct); // small delay to avoid flooding (Shoonya might throttle rapid sends)
            }
        }
        public bool TryGetSubscriptionAck(uint token, out SubscriptionAck ack)
        {
            return _subscriptionAcks.TryGetValue(token, out ack);
        }
    }
}
