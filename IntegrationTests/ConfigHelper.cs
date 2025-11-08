using System.Text.Json;

namespace IntegrationTests
{
    public static class ConfigHelper
    {
        public static BrokerConfig LoadConfiguration()
        {
            // Prefer appsettings.json copied to output folder
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "appsettings.json");

            if (!File.Exists(path))
            {
                // Fallback to project file (for IDE runs)
                var fallback = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "IntegrationTests", "appsettings.json"));
                if (File.Exists(fallback))
                    path = fallback;
                else
                    throw new FileNotFoundException("Could not find appsettings.json in output or project folder.", path);
            }

            var json = File.ReadAllText(path);
            var creds = JsonSerializer.Deserialize<BrokerCredentials>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new BrokerCredentials();

            return new BrokerConfig { BrokerCredentials = creds };
        }
    }

    public class BrokerConfig
    {
        public BrokerCredentials BrokerCredentials { get; set; } = new BrokerCredentials();
    }

    public class BrokerCredentials
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }
}
