using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Zerodha
{
    public class ZerodhaAuthRequest
    {
        public string ApiKey { get; set; } = string.Empty;
        public string RequestToken { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;

        public string ComputeChecksum()
        {
            using var sha256 = SHA256.Create();
            string payload = ApiKey + RequestToken + ApiSecret;
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }        
    }

    public class ZerodhaAuthResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public ZerodhaUserData? Data { get; set; }

        [JsonIgnore]
        public string? Token => Data?.AccessToken;

        [JsonIgnore]
        public bool IsSuccess => Status == "success";
    }

    public class ZerodhaUserData
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = string.Empty;

        // You can add more fields if needed
    }
}
