using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BrokerInterfaces;
using System.Buffers.Binary;


namespace Zerodha
{
    public class BrokerWebSocketService : IBrokerWebSocketService<ZerodhaSubscriptionDepthAck>
    {
        private readonly string _userId;
        private readonly string _clientId;
        private readonly string _authToken;

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private volatile bool _processFeedMessages = false;

        public event Action<PriceFeedUpdate> OnPriceFeedUpdate;

        private BrokerWebSocketService(string userId, string clientId, string authToken)
        {
            _userId = userId ?? throw new ArgumentNullException(nameof(userId));
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
        }

        public static IBrokerWebSocketService<ZerodhaSubscriptionDepthAck> Instance(
            string userId,
            string clientId,
            string authToken)
        {
            return new BrokerWebSocketService(userId, clientId, authToken);
        }

        public async Task ConnectAsync()
        {
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            var uri = new Uri($"wss://ws.kite.trade?api_key={_clientId}&access_token={_authToken}");
            await _webSocket.ConnectAsync(uri, _cts.Token);

            _ = Task.Run(ReceiveLoop, _cts.Token);
        }

        public async Task SubscribeToBatchAsync(IEnumerable<(string Exchange, uint Token)> instruments)
        {
            if (_webSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected.");

            var tokens = instruments.Select(i => (int)i.Token).ToList();
            var payload = JsonSerializer.Serialize(new { a = "subscribe", v = tokens });
            var bytes = Encoding.UTF8.GetBytes(payload);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);

            var modePayload = JsonSerializer.Serialize(new { a = "mode", v = new object[] { "full", tokens } });
            var modeBytes = Encoding.UTF8.GetBytes(modePayload);
            await _webSocket.SendAsync(new ArraySegment<byte>(modeBytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        public void SetProcessFeedMessages(bool enable)
        {
            _processFeedMessages = enable;
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];

            try
            {
                while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(buffer, _cts.Token);                    

                    if (!_processFeedMessages)
                        continue;

                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        Console.WriteLine($"[ZerodhaWS] Ignoring non-binary message {result}");
                        continue;
                    }

                    try
                    {
                        foreach (var update in DecodeZerodhaBinary(buffer, result.Count))
                        {
                            //Console.WriteLine($"Token: {update.Token}, LTP: {update.LastTradedPrice}, Bid: {update.BidPrice}, Ask: {update.AskPrice}, OI: {update.OpenInterest}");
                            _ = Task.Run(() => OnPriceFeedUpdate?.Invoke(update));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ZerodhaWS] Error decoding/processing websocket update. Buffer : {buffer}, Message: {ex.Message}");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZerodhaWS] Error in ReceiveLoop: {ex.Message}");
            }
        }

        /// <summary>
        /// Decodes one WebSocket binary frame into one or more PriceFeedUpdate structs.
        /// Handles big-endian, multiple packets, and all modes (ltp/quote/full).
        /// </summary>
        private static IEnumerable<PriceFeedUpdate> DecodeZerodhaBinary(byte[] buffer, int count)
        {
            var updates = new List<PriceFeedUpdate>();
            if (count < 2)
                return updates;

            int offset = 0;
            short packetCount = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(offset, 2));
            offset += 2;

            for (int i = 0; i < packetCount; i++)
            {
                if (offset + 2 > count) break;
                short packetLength = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(offset, 2));
                offset += 2;

                if (offset + packetLength > count) break;
                var packet = buffer.AsSpan(offset, packetLength);
                offset += packetLength;

                // --- Parse single quote packet ---
                if (packet.Length < 8) continue; // minimum token + ltp
                uint instrumentToken = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(0, 4));

                // mode detection by packet length
                double ltp = 0, bid = 0, ask = 0;
                double? oi = null;

                if (packet.Length == 8) // LTP mode
                {
                    ltp = bid = ask = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(4, 4)) / 100.0;
                }
                else if (packet.Length >= 8 && packet.Length <= 44) // Quote mode
                {
                    ltp = bid = ask = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(4, 4)) / 100.0;
                    //bid = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(8, 4)) / 100.0;
                    //ask = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(12, 4)) / 100.0;
                    //oi  = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(48, 4));
                }
                else // Full mode
                {
                    ltp = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(4, 4)) / 100.0;
                    bid = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(68, 4)) / 100.0;
                    ask = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(128, 4)) / 100.0;
                    oi  = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(48, 4));
                }

                updates.Add(new PriceFeedUpdate(instrumentToken, ltp, bid, ask, oi));
            }

            return updates;
        }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel(); // stop any loops
                if (_webSocket?.State == WebSocketState.Open)
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None).Wait();
                _webSocket?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket Dispose error: {ex.Message}");
            }
        }
    }

    public class ZerodhaSubscriptionDepthAck
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}