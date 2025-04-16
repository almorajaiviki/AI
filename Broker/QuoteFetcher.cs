using System.Text.Json;
using System.Text;
using System.Net.Http.Json;

namespace Broker
{
    public class QuoteFetcher
    {
        private readonly BrokerAuthService _authService;
        private readonly HttpClient _httpClient;

        public QuoteFetcher(BrokerAuthService authService, HttpClient httpClient)
        {
            _authService = authService;
            _httpClient = httpClient;
        }

        public async Task<List<InstrumentQuote>> FetchQuotesAsync(List<QuoteRequest> requests)
        {
            var quotes = new List<InstrumentQuote>();
            
            foreach (var req in requests)
            {
                var quote = await FetchSingleQuoteAsync(req.Exchange, req.Token);
                quotes.Add(new InstrumentQuote(
                    req.Token,
                    double.Parse(quote.LastPrice),  // Last price
                    double.Parse(quote.LastPrice),   // Bid (same as LTP)
                    double.Parse(quote.LastPrice)    // Ask (same as LTP)
                ));
            }
            
            return quotes;
        }

        private async Task<QuoteResponse> FetchSingleQuoteAsync(string exchange, uint token, CancellationToken ct = default)
        {
            if (_authService.CurrentAuthResponse?.Token == null)
                throw new InvalidOperationException("Not authenticated");

            var jData = new JDataQuote(
                uid: _authService.CurrentAuthResponse.UserName ?? "default",
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

            var result = await response.Content.ReadFromJsonAsync<QuoteResponse>();
            if (result?.Status != "Ok")
                throw new QuoteFetchException($"API error: {result?.Status ?? "null"}");

            return result;
        }
    }

    // Supporting classes
    public record QuoteRequest(string Exchange, uint Token, string Symbol);
    public record InstrumentQuote(uint Token, double Ltp, double Bid, double Ask);
    public class QuoteFetchException : Exception
    {
        public QuoteFetchException(string message) : base(message) { }
    }
}