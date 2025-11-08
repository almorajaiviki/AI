using Zerodha;
using BrokerInterfaces;

namespace IntegrationTests
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== Zerodha Login Integration Test ===");

            // Load config (ApiKey, ApiSecret) using plain System.Text.Json
            var config = ConfigHelper.LoadConfiguration();
            string apiKey = config.BrokerCredentials.ApiKey;
            string apiSecret = config.BrokerCredentials.ApiSecret;

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            {
                Console.WriteLine("❌ ApiKey or ApiSecret missing in appsettings.json");
                return 1;
            }

            Console.Write("Enter Request Token from Zerodha login: ");
            string? requestToken = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(requestToken))
            {
                Console.WriteLine("❌ Request Token is required.");
                return 1;
            }

            var authRequest = new ZerodhaAuthRequest
            {
                ApiKey = apiKey,
                ApiSecret = apiSecret,
                RequestToken = requestToken.Trim()
            };

            using var httpClient = new HttpClient();
            IBrokerAuthService<ZerodhaAuthRequest, ZerodhaAuthResponse> authService =
                ZerodhaAuthService.Instance(httpClient);

            try
            {
                var authResponse = await authService.LoginAsync(authRequest);

                if (authResponse.IsSuccess && !string.IsNullOrWhiteSpace(authResponse.Token))
                {
                    Console.WriteLine("✅ Login successful.");
                    Console.WriteLine("Access Token: " + authResponse.Token);
                    return 0;
                }
                else
                {
                    Console.WriteLine("⚠️ Login failed. Status: " + authResponse.Status);
                    return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Exception during login: " + ex.Message);
                return 3;
            }
            finally
            {
                await authService.LogoutAsync();
                authService.Dispose();
            }
        }
    }
}
