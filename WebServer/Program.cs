using Broker;
class Server
{
    static void Main()
    {
        // Create HttpClient (should be singleton in real app)
        var httpClient = new HttpClient();
        var authService = new BrokerAuthService(httpClient);
        try
        {
            LoginToBroker(authService);
        }
        catch (Exception ex)
        {            
            Console.WriteLine($"API Error: {ex.Message}");;
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

    private static void LoginToBroker(BrokerAuthService authService)
    {
        Console.Write("Please enter your OTP ");
        string OTP = Console.ReadLine() ?? string.Empty;
        // Prepare login request
        AuthRequest request = new AuthRequest(
            userId: "FA122064",
            plainTextPassword: "Almoraboy7*",
            twoFA: OTP, // or your 2FA code if required            
            appVersion: "1.0.0",
            "8c5cabe1de485bad141570c3c7927e90",            
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
}