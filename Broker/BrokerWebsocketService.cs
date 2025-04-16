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
        private readonly ConcurrentDictionary<uint, SubscriptionAck> _subscriptionAcks = new();

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
                switch (check?.Type?.ToLower())
                {
                    case "dk": // Subscription Acknowledgement
                    case "tk":
                        var ack = JsonSerializer.Deserialize<SubscriptionAck>(json);
                        if (ack?.Token != null && uint.TryParse(ack.Token, out uint token))
                        {
                            _subscriptionAcks[token] = ack;
                        }
                        break;

                    case "am":
                        var alert = JsonSerializer.Deserialize<AlertMessage>(json);
                        Console.WriteLine($"⚠️ Broker Alert: {alert?.Message}");
                        break;

                    // We'll ignore everything else for now.
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RouteMessage error: {ex.Message}");
            }
        }

        public bool TryGetSubscriptionAck(uint token, out SubscriptionAck ack)
        {
            return _subscriptionAcks.TryGetValue(token, out ack);
        }
    }
}
