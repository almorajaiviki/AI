using System.Net.Http.Headers;
using System.Text.Json;
using BrokerInterfaces;

namespace Zerodha
{
    public class ZerodhaAuthService : IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse>
    {
        private static ZerodhaAuthService? _instance;
        private static readonly object _lock = new();

        public static IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> Instance(HttpClient httpClient)
        {
            lock (_lock)
            {
                _instance ??= new ZerodhaAuthService(httpClient);
                return _instance;
            }
        }

        private readonly HttpClient _httpClient;
        private ZerodhaAuthRequest? _currentAuthRequest;
        private ZerodhaAuthResponse? _currentAuthResponse;

        private ZerodhaAuthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public ZerodhaAuthRequest? CurrentAuthRequest => _currentAuthRequest;
        public ZerodhaAuthResponse? CurrentAuthResponse => _currentAuthResponse;

        public async Task<ZerodhaAuthResponse> LoginAsync(ZerodhaAuthRequest request, CancellationToken ct = default)
        {
            _currentAuthRequest = request;

            try
            {
                string checksum = request.ComputeChecksum();

                var payload = new Dictionary<string, string>
                {
                    { "api_key", request.ApiKey },
                    { "request_token", request.RequestToken },
                    { "checksum", checksum }
                };

                var content = new FormUrlEncodedContent(payload);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.kite.trade/session/token")
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(httpRequest, ct);
                response.EnsureSuccessStatusCode();

                var authResponse = await JsonSerializer.DeserializeAsync<ZerodhaAuthResponse>(
                    await response.Content.ReadAsStreamAsync(ct),
                    cancellationToken: ct
                ) ?? throw new InvalidOperationException("Null API response");

                _currentAuthResponse = authResponse;
                return authResponse;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZerodhaAuthService] Login failed: {ex.Message}");
                throw;
            }
        }

        public Task<bool> LogoutAsync(CancellationToken ct = default)
        {
            // No API endpoint to logout in Zerodha
            _currentAuthResponse = null;
            Console.WriteLine("[ZerodhaAuthService] Session cleared.");
            return Task.FromResult(true);
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
