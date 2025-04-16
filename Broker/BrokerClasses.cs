using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace Broker
{
    public static class ApiEndpoints
    {
        public const string Login = "https://api.shoonya.com/NorenWClientTP/QuickAuth";
        public const string Logout = "https://api.shoonya.com/NorenWClientTP/Logout";
        public const string GetQuotes = "https://api.shoonya.com/get-quotes";

        // WebSocket endpoint for real-time streaming
        public const string WebSocket = "wss://api.shoonya.com/NorenWSTP/";
    }

    public readonly struct AuthRequest
    {
        [JsonPropertyName("uid")]
        public string UserId { get; }

        [JsonPropertyName("pwd")]
        public string Password { get; }

        [JsonPropertyName("factor2")]
        public string TwoFA { get; }

        [JsonPropertyName("vc")]
        public string VendorCode { get; }

        [JsonPropertyName("apkversion")]
        public string AppVersion { get; }
        [JsonPropertyName("appkey")]
        public string AppKey { get; }

        [JsonPropertyName("imei")]
        public string DeviceId { get; }

        [JsonPropertyName("source")]
        public string Source { get; }

        public AuthRequest(
            string userId,
            string plainTextPassword,
            string twoFA,            
            string appVersion,
            string appKey,
            
            string source)
        {
            UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            Password = ComputeSha256Hash(plainTextPassword ?? throw new ArgumentNullException(nameof(plainTextPassword)));
            TwoFA = twoFA; // Can be null if 2FA not required
            VendorCode = userId + "_U"; // Vendor code is userId with "_U" suffix
            AppVersion = appVersion;
            AppKey = ComputeSha256Hash (userId + "|" + appKey) ?? throw new ArgumentNullException(nameof(appKey));
            DeviceId = "abc1234";
            Source = source;
        }

        private static string ComputeSha256Hash(string rawData)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            
            var builder = new StringBuilder();
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));  // Convert to hex string
            }
            return builder.ToString();
        }

    }    
    public class AuthResponse
    {
        // Only non-nullable property (required for IsSuccess)
        [JsonPropertyName("stat")]
        public string Status { get; set; } = string.Empty;

        // All other properties are nullable
        [JsonPropertyName("susertoken")]
        public string? Token { get; set; }

        [JsonPropertyName("emsg")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("exarr")]
        public string[]? Exchanges { get; set; }

        [JsonPropertyName("uname")]
        public string? UserName { get; set; }

        [JsonIgnore]
        public bool IsSuccess => Status == "Ok";
    }
    public sealed class QuoteResponse
    {
        [JsonPropertyName("request_time")]
        public string RequestTime { get; }
        
        [JsonPropertyName("stat")]
        public string Status { get; }
        
        [JsonPropertyName("exch")]
        public string Exchange { get; }
        
        [JsonPropertyName("pp")]
        public string PreviousClose { get; }
        
        [JsonPropertyName("ls")]
        public string LotSize { get; }
        
        [JsonPropertyName("ti")]
        public string TickSize { get; }
        
        [JsonPropertyName("mult")]
        public string Multiplier { get; }
        
        [JsonPropertyName("cutof_all")]
        public string CutoffAll { get; }
        
        [JsonPropertyName("prcftr_d")]
        public string PriceFactor { get; }
        
        [JsonPropertyName("token")]
        public string Token { get; }
        
        [JsonPropertyName("lp")]
        public string LastPrice { get; }
        
        [JsonPropertyName("c")]
        public string Change { get; }
        
        [JsonPropertyName("bp1")]
        public string BidPrice { get; }
        
        [JsonPropertyName("sp1")]
        public string AskPrice { get; }

        [JsonConstructor]
        public QuoteResponse(
            string request_time,
            string stat,
            string exch,
            string pp,
            string ls,
            string ti,
            string mult,
            string cutof_all,
            string prcftr_d,
            string token,
            string lp,
            string c,
            string bp1,
            string sp1)
        {
            RequestTime = request_time ?? string.Empty;
            Status = stat ?? string.Empty;
            Exchange = exch ?? string.Empty;
            PreviousClose = pp ?? "0";
            LotSize = ls ?? "0";
            TickSize = ti ?? "0";
            Multiplier = mult ?? "0";
            CutoffAll = cutof_all ?? "0";
            PriceFactor = prcftr_d ?? "0";
            Token = token ?? "0";
            LastPrice = lp ?? "0";
            Change = c ?? "0";
            BidPrice = bp1 ?? "0";
            AskPrice = sp1 ?? "0";
        }

        public double GetLastPrice() => SafeParse(LastPrice);
        public double GetBidPrice() => SafeParse(BidPrice);
        public double GetAskPrice() => SafeParse(AskPrice);

        private static double SafeParse(string value) => 
            double.TryParse(value, out var num) ? num : 0;
    }
    public sealed class JDataQuote
    {
        public string uid { get; }
        public string exch { get; }
        public string token { get; }

        // Primary constructor (uint token version)
        public JDataQuote(string uid, string exch, uint token)
            : this(uid, exch, token.ToString())
        {
        }

        // Secondary constructor (string token version)
        public JDataQuote(string uid, string exch, string token)
        {
            this.uid = uid ?? throw new ArgumentNullException(nameof(uid));
            this.exch = exch ?? throw new ArgumentNullException(nameof(exch));
            this.token = token ?? throw new ArgumentNullException(nameof(token));
        }

        // Optional: Add JSON serialization support
        public override string ToString() => 
            $"{{ \"uid\":\"{uid}\", \"exch\":\"{exch}\", \"token\":\"{token}\" }}";
    }
    public sealed record ConnectMessage(
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("uid")] string UserId,
        [property: JsonPropertyName("actid")] string AccountId,
        [property: JsonPropertyName("susertoken")] string Token
    );
    public sealed record ConnectResponse(
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("uid")] string UserId,
        [property: JsonPropertyName("s")] string Status
    );
    public sealed record AllResponsesCheck(
        [property: JsonPropertyName("t")] string Type
    );
    public sealed record AlertMessage(
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("dmsg")] string Message
    );
    public sealed record SubscriptionAck(
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("e")] string Exchange,
        [property: JsonPropertyName("tk")] string Token,
        [property: JsonPropertyName("pp")] string PreviousClose,
        [property: JsonPropertyName("ts")] string Timestamp,
        [property: JsonPropertyName("ti")] string TickSize,
        [property: JsonPropertyName("ls")] string LotSize,
        [property: JsonPropertyName("lp")] string LastPrice,
        [property: JsonPropertyName("pc")] string PercentChange,
        [property: JsonPropertyName("v")] string Volume,
        [property: JsonPropertyName("o")] string Open,
        [property: JsonPropertyName("h")] string High,
        [property: JsonPropertyName("l")] string Low,
        [property: JsonPropertyName("c")] string Close,
        [property: JsonPropertyName("ap")] string AveragePrice,
        [property: JsonPropertyName("oi")] string OpenInterest,
        [property: JsonPropertyName("poi")] string PrevOpenInterest,
        [property: JsonPropertyName("toi")] string TotalOpenInterest,
        [property: JsonPropertyName("bq1")] string BidQty,
        [property: JsonPropertyName("bp1")] string BidPrice,
        [property: JsonPropertyName("sq1")] string AskQty,
        [property: JsonPropertyName("sp1")] string AskPrice
    );
    public sealed record SubscriptionUpdate(
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("e")] string Exchange,
        [property: JsonPropertyName("tk")] string Token,
        [property: JsonPropertyName("lp")] string LastPrice,
        [property: JsonPropertyName("pc")] string PercentChange,
        [property: JsonPropertyName("v")] string Volume,
        [property: JsonPropertyName("o")] string Open,
        [property: JsonPropertyName("h")] string High,
        [property: JsonPropertyName("l")] string Low,
        [property: JsonPropertyName("c")] string Close,
        [property: JsonPropertyName("ap")] string AveragePrice,
        [property: JsonPropertyName("oi")] string OpenInterest,
        [property: JsonPropertyName("poi")] string PrevOpenInterest,
        [property: JsonPropertyName("toi")] string TotalOpenInterest,
        [property: JsonPropertyName("bq1")] string BidQty,
        [property: JsonPropertyName("bp1")] string BidPrice,
        [property: JsonPropertyName("sq1")] string AskQty,
        [property: JsonPropertyName("sp1")] string AskPrice
    );
}


    
