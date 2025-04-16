using System.Text.Json;
using Broker;

class Server
{
    static void Main()
    {
        // Load configuration from appsettings.json
        var config = LoadConfiguration();
        string userId = config.BrokerCredentials.UserId;
        string plainTextPassword = config.BrokerCredentials.PlainTextPassword;
        string appKey = config.BrokerCredentials.AppKey;

        // Create HttpClient (should be singleton in real app)
        HttpClient httpClient = new HttpClient();
        BrokerAuthService authService = new BrokerAuthService(httpClient);

        try
        {
            LoginToBroker(authService, userId, plainTextPassword, appKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"API Error: {ex.Message}");
        }
        finally
        {
            // Clean up
            bool bLogout = authService.LogoutAsync().Result;
            if (bLogout)
            {
                Console.WriteLine("Logged out successfully.");
            }
            else
            {
                Console.WriteLine("Logout failed.");
            }

            // Dispose of the HttpClient
            httpClient.Dispose();
            // Dispose of the authService
            authService.Dispose();
        }
    }

    private static void LoginToBroker(BrokerAuthService authService, string userId, string plainTextPassword, string appKey)
    {
        Console.Write("Please enter your OTP: ");
        string OTP = Console.ReadLine() ?? string.Empty;

        // Prepare login request
        AuthRequest request = new AuthRequest(
            userId: userId,
            plainTextPassword: plainTextPassword,
            twoFA: OTP, // or your 2FA code if required
            appVersion: "1.0.0",
            appKey: appKey,
            source: "API"
        );

        // Execute login
        var response = authService.LoginAsync(request).Result;

        if (response.IsSuccess)
        {
            // Use the token for subsequent API calls
            Console.WriteLine($"Session token: {response.Token}");
        }
        else
        {
            throw new Exception($"Login failed: {response.ErrorMessage}");
        }
    }

    private static AppConfig LoadConfiguration()
    {
        string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

        if (!File.Exists(configFilePath))
        {
            throw new FileNotFoundException("Configuration file not found.", configFilePath);
        }

        string json = File.ReadAllText(configFilePath);
        return JsonSerializer.Deserialize<AppConfig>(json)
               ?? throw new InvalidOperationException("Failed to deserialize configuration.");
    }
}

public class AppConfig
{
    public BrokerCredentials BrokerCredentials { get; set; } = new BrokerCredentials();
}

public class BrokerCredentials
{
    public string UserId { get; set; } = string.Empty;
    public string PlainTextPassword { get; set; } = string.Empty;
    public string AppKey { get; set; } = string.Empty;
}