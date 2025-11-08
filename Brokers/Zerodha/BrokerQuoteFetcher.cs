using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using BrokerInterfaces;

namespace Zerodha
{
    public sealed class ZerodhaQuoteFetcher : IBrokerQuoteFetcher<ZerodhaAuthRequest, ZerodhaAuthResponse>
    {
        private readonly IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> _authService;
        private readonly HttpClient _httpClient;

        private static ZerodhaQuoteFetcher? _instance;
        private static readonly object _lock = new();

        private const string BaseUrl = "https://api.kite.trade";
        private const int MaxBatchSize = 500;

        private ZerodhaQuoteFetcher(
            IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> authService,
            HttpClient httpClient)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        // Singleton accessor (matches your IBrokerQuoteFetcher signature)
        public static IBrokerQuoteFetcher<ZerodhaAuthRequest, ZerodhaAuthResponse> Instance(
            IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> authService,
            HttpClient httpClient)
        {
            lock (_lock)
            {
                return _instance ??= new ZerodhaQuoteFetcher(authService, httpClient);
            }
        }

        public async Task<List<InstrumentQuote>> FetchQuotesAsync(List<QuoteRequest> requests)
        {
            if (_authService.CurrentAuthResponse == null)
                throw new InvalidOperationException("Zerodha authentication required before fetching quotes.");

            var results = new List<InstrumentQuote>();

            foreach (var batch in BatchRequests(requests, MaxBatchSize))
            {
                var quotes = await FetchBatchAsync(batch);
                results.AddRange(quotes);
            }

            return results;
        }

        private async Task<List<InstrumentQuote>> FetchBatchAsync(List<QuoteRequest> batch)
        {
            var queryParams = string.Join("&", batch.Select(req =>
                $"i={req.Exchange}:{req.Symbol}"
            ));

            var url = $"{BaseUrl}/quote?{queryParams}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", 
                $"token {_authService.CurrentAuthRequest!.ApiKey}:{_authService.CurrentAuthResponse!.Token}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            var results = new List<InstrumentQuote>();

            if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
            {
                foreach (var req in batch)
                {
                    var key = $"{req.Exchange}:{req.Symbol}";
                    if (!dataElement.TryGetProperty(key, out var instrumentElement))
                        continue;

                    double ltp = instrumentElement.GetProperty("last_price").GetDouble();
                    double bid = 0;
                    double ask = 0;
                    double oi = 0;

                    if (instrumentElement.TryGetProperty("depth", out var depthElement))
                    {
                        if (depthElement.TryGetProperty("buy", out var buyArray) && buyArray.GetArrayLength() > 0)
                            bid = buyArray[0].GetProperty("price").GetDouble();

                        if (depthElement.TryGetProperty("sell", out var sellArray) && sellArray.GetArrayLength() > 0)
                            ask = sellArray[0].GetProperty("price").GetDouble();
                    }

                    if (instrumentElement.TryGetProperty("oi", out var oiElement))
                        oi = oiElement.GetDouble();

                    results.Add(new InstrumentQuote(req.Token, ltp, bid, ask, oi));
                }
            }
            return results;
        }

        private static IEnumerable<List<QuoteRequest>> BatchRequests(List<QuoteRequest> requests, int batchSize)
        {
            for (int i = 0; i < requests.Count; i += batchSize)
                yield return requests.Skip(i).Take(batchSize).ToList();
        }
    }
}
