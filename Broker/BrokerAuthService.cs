using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;

namespace Broker
{
    public interface IBrokerAuthService : IDisposable
    {
        Task<AuthResponse> LoginAsync(AuthRequest request, CancellationToken ct = default);
        Task<bool> LogoutAsync(CancellationToken ct = default);
    }


    public class BrokerAuthService : IBrokerAuthService
    {
        public AuthResponse? CurrentAuthResponse => _currentAuthResponse; // Add this property
        private readonly HttpClient _httpClient;
        private AuthResponse? _currentAuthResponse; // Private storage for full login response

        public BrokerAuthService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<AuthResponse> LoginAsync(AuthRequest request, CancellationToken ct = default)
        {
            const string LoginEndpoint = ApiEndpoints.Login;
            
            try
            {
                LogLoginAttempt(request.UserId);

                var httpRequest = CreateLoginRequest(request, LoginEndpoint);
                var response = await SendLoginRequestAsync(httpRequest, ct);
                var authResponse = await ParseAuthResponseAsync(response, ct);

                // Store the complete response internally
                _currentAuthResponse = authResponse;

                HandleLoginResult(authResponse);
                return authResponse;
            }
            catch (HttpRequestException ex)
            {
                LogAndThrow("HTTP error during login", ex);
            }
            catch (TaskCanceledException ex) when (ct.IsCancellationRequested)
            {
                LogAndThrow("Login cancelled by user", ex, ct);
            }
            catch (Exception ex)
            {
                LogAndThrow("Unexpected login error", ex);
            }

            throw new InvalidOperationException("Unreachable code reached");
        }

        public async Task<bool> LogoutAsync(CancellationToken ct = default)
        {
            const string LogoutEndpoint = ApiEndpoints.Logout;
            
            if (_currentAuthResponse?.Token == null)
            {
                Console.WriteLine("No active session - already logged out");
                return true;
            }

            try
            {
                Console.WriteLine("Attempting logout...");
                
                // Use stored response data
                var logoutPayload = new { 
                    uid = _currentAuthResponse.UserName ?? "default_user" // Fallback if needed
                };
                string formData = $"jData={JsonSerializer.Serialize(logoutPayload)}&jKey={_currentAuthResponse.Token}";

                using var request = new HttpRequestMessage(HttpMethod.Post, LogoutEndpoint)
                {
                    Content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded")
                };

                var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                _currentAuthResponse = null; // Clear session
                Console.WriteLine("Logout successful");
                return true;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP logout error: {ex.StatusCode} - {ex.Message}");
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                Console.WriteLine("Logout cancelled by user");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected logout error: {ex.Message}");
            }
            
            return false;
        }

        // ===== Helper Methods (Unchanged from original) =====
        private void LogLoginAttempt(string userId)
        {
            Console.WriteLine($"Attempting login for user: {userId}");
        }

        private HttpRequestMessage CreateLoginRequest(AuthRequest request, string endpoint)
        {
            string jsonPayload = JsonSerializer.Serialize(request);
            string formData = $"jData={jsonPayload}";

            return new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded")
            };
        }

        private async Task<HttpResponseMessage> SendLoginRequestAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return response;
        }

        private async Task<AuthResponse> ParseAuthResponseAsync(HttpResponseMessage response, CancellationToken ct)
        {
            return await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct) 
                ?? throw new InvalidOperationException("Null API response");
        }

        private void HandleLoginResult(AuthResponse authResponse)
        {
            if (authResponse.IsSuccess)
            {
                Console.WriteLine($"Login successful! Welcome {authResponse.UserName ?? "User"}");
            }
            else
            {
                Console.WriteLine($"Login failed: {authResponse.ErrorMessage ?? "Unknown error"}");
            }
        }

        private void LogAndThrow(string message, Exception ex, CancellationToken ct = default)
        {
            Console.WriteLine(message);
            if (ex is TaskCanceledException && ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(message, ex, ct);
            }
            throw new BrokerApiException(message, ex);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class BrokerApiException : Exception
    {
        public BrokerApiException(string message, Exception innerException) 
            : base(message, innerException) { }
    }
}