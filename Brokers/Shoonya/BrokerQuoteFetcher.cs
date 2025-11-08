using System.Text.Json;
using System.Text;
using BrokerInterfaces;

namespace Shoonya
{
    public sealed class BrokerQuoteFetcher : IBrokerQuoteFetcher<AuthRequest, AuthResponse>
    {
        private readonly IBrokerAuthService<AuthRequest, AuthResponse> _authService;
        private readonly HttpClient _httpClient;

        private static BrokerQuoteFetcher? _instance;
        private static readonly object _lock = new();

        private BrokerQuoteFetcher(
            IBrokerAuthService<AuthRequest, AuthResponse> authService,
            HttpClient httpClient)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public static IBrokerQuoteFetcher<AuthRequest, AuthResponse> Instance(
            IBrokerAuthService<AuthRequest, AuthResponse> authService,
            HttpClient httpClient)
        {
            lock (_lock)
            {
                return _instance ??= new BrokerQuoteFetcher(authService, httpClient);
            }
        }

        public async Task<List<InstrumentQuote>> FetchQuotesAsync(List<QuoteRequest> requests)
        {
            var quotes = new List<InstrumentQuote>();

            foreach (var req in requests)
            {
                var quote = await FetchSingleQuoteAsync(req.Exchange, uint.Parse(req.Token));
                quotes.Add(new InstrumentQuote(
                    req.Token,
                    double.TryParse(quote.LastPrice, out var ltp) ? ltp : 0,
                    double.TryParse(quote.BidPrice, out var bid) ? bid : 0,
                    double.TryParse(quote.AskPrice, out var ask) ? ask : 0,
                    0 //double.TryParse(quote.OpenInterest , out var oi) ? oi : 0
                ));
            }

            return quotes;
        }


        private async Task<QuoteResponse> FetchSingleQuoteAsync(string exchange, uint token, CancellationToken ct = default)
        {
            if (_authService.CurrentAuthRequest == null || _authService.CurrentAuthResponse == null)
                throw new InvalidOperationException("Not authenticated");

            var jData = new JDataQuote(
                uid: _authService.CurrentAuthRequest.UserId ?? "default",
                exch: exchange,
                token: token
            );

            string formData = $"jData={JsonSerializer.Serialize(jData)}&jKey={_authService.CurrentAuthResponse.Token}";

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.GetQuotes)
            {
                Content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = JsonSerializer.Deserialize<QuoteResponse>(await response.Content.ReadAsStringAsync());

            if (result?.Status != "Ok")
                throw new QuoteFetchException($"API error: {result?.Status ?? "null"}");

            return result;
        }
    }

    public class QuoteFetchException : Exception
    {
        public QuoteFetchException(string message) : base(message) { }
        public QuoteFetchException(string message, Exception innerException) : base(message, innerException) { }
    }
}
