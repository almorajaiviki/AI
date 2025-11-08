using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace Shoonya
{
    public static class ApiEndpoints
    {
        public const string Login = "https://api.shoonya.com/NorenWClientTP/QuickAuth";
        public const string Logout = "https://api.shoonya.com/NorenWClientTP/Logout";
        public const string GetQuotes = "https://api.shoonya.com/NorenWClientTP/GetQuotes";

        // WebSocket endpoint for real-time streaming
        public const string WebSocket = "wss://api.shoonya.com/NorenWSTP/";
    }

    public class AuthRequest
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
            AppKey = ComputeSha256Hash(userId + "|" + appKey) ?? throw new ArgumentNullException(nameof(appKey));
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
    public class QuoteResponse
    {
        [JsonPropertyName("request_time")]
        public string RequestTime { get; set; }

        [JsonPropertyName("stat")]
        public string Status { get; set; }

        [JsonPropertyName("exch")]
        public string Exchange { get; set; }

        [JsonPropertyName("pp")]
        public string PreviousClose { get; set; }

        [JsonPropertyName("ls")]
        public string LotSize { get; set; }

        [JsonPropertyName("ti")]
        public string TickSize { get; set; }

        [JsonPropertyName("mult")]
        public string Multiplier { get; set; }

        [JsonPropertyName("cutof_all")]
        public string CutoffAll { get; set; }

        [JsonPropertyName("prcftr_d")]
        public string PriceFactor { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("lp")]
        public string LastPrice { get; set; }

        [JsonPropertyName("c")]
        public string Change { get; set; }

        [JsonPropertyName("bp1")]
        public string BidPrice { get; set; }

        [JsonPropertyName("sp1")]
        public string AskPrice { get; set; }
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
    public sealed record Subscribe
    (
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("k")] string Key
    );

    public sealed record UnSubscribe
    (
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("k")] string Key
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
    public sealed record SubscriptionDepthAck(
        [property: JsonPropertyName("t")] string Type,
        [property: JsonPropertyName("e")] string Exchange,
        [property: JsonPropertyName("tk")] string Token,
        [property: JsonPropertyName("ts")] string Timestamp,
        [property: JsonPropertyName("lp")] string LastPrice,
        [property: JsonPropertyName("pc")] string PercentChange,
        [property: JsonPropertyName("o")] string Open,
        [property: JsonPropertyName("h")] string High,
        [property: JsonPropertyName("l")] string Low,
        [property: JsonPropertyName("c")] string Close,
        [property: JsonPropertyName("v")] string Volume,
        [property: JsonPropertyName("oi")] string OpenInterest,
        [property: JsonPropertyName("poi")] string PrevOpenInterest,
        [property: JsonPropertyName("toi")] string TotalOpenInterest,
        [property: JsonPropertyName("ap")] string AveragePrice,

        // Bid depth levels
        [property: JsonPropertyName("bq1")] string BidQty1,
        [property: JsonPropertyName("bp1")] string BidPrice1,
        [property: JsonPropertyName("bq2")] string BidQty2,
        [property: JsonPropertyName("bp2")] string BidPrice2,
        [property: JsonPropertyName("bq3")] string BidQty3,
        [property: JsonPropertyName("bp3")] string BidPrice3,
        [property: JsonPropertyName("bq4")] string BidQty4,
        [property: JsonPropertyName("bp4")] string BidPrice4,
        [property: JsonPropertyName("bq5")] string BidQty5,
        [property: JsonPropertyName("bp5")] string BidPrice5,

        // Ask depth levels
        [property: JsonPropertyName("sq1")] string AskQty1,
        [property: JsonPropertyName("sp1")] string AskPrice1,
        [property: JsonPropertyName("sq2")] string AskQty2,
        [property: JsonPropertyName("sp2")] string AskPrice2,
        [property: JsonPropertyName("sq3")] string AskQty3,
        [property: JsonPropertyName("sp3")] string AskPrice3,
        [property: JsonPropertyName("sq4")] string AskQty4,
        [property: JsonPropertyName("sp4")] string AskPrice4,
        [property: JsonPropertyName("sq5")] string AskQty5,
        [property: JsonPropertyName("sp5")] string AskPrice5
    );

    public sealed record TouchlineFeed(
        [property: JsonPropertyName("t")] string Type,               // "tf"
        [property: JsonPropertyName("e")] string Exchange,
        [property: JsonPropertyName("tk")] string Token,
        [property: JsonPropertyName("lp")] string LastPrice,
        [property: JsonPropertyName("pc")] string PercentChange,
        [property: JsonPropertyName("v")] string Volume,
        [property: JsonPropertyName("o")] string Open,
        [property: JsonPropertyName("h")] string High,
        [property: JsonPropertyName("l")] string Low,
        [property: JsonPropertyName("c")] string Close,
        [property: JsonPropertyName("ap")] string AverageTradePrice,
        [property: JsonPropertyName("oi")] string OpenInterest,
        [property: JsonPropertyName("poi")] string PrevOpenInterest,
        [property: JsonPropertyName("toi")] string TotalOpenInterest,
        [property: JsonPropertyName("bq1")] string BidQty,
        [property: JsonPropertyName("bp1")] string BidPrice,
        [property: JsonPropertyName("sq1")] string AskQty,
        [property: JsonPropertyName("sp1")] string AskPrice,
        [property: JsonPropertyName("tvalue")] string TradeValue,
        [property: JsonPropertyName("ltq")] string LastTradedQty,
        [property: JsonPropertyName("tbq")] string TotalBuyQty,
        [property: JsonPropertyName("tsq")] string TotalSellQty,
        [property: JsonPropertyName("bp")] string BasePrice
    );

    public sealed record DepthFeed(
        [property: JsonPropertyName("t")] string Type,               // "df"
        [property: JsonPropertyName("e")] string Exchange,
        [property: JsonPropertyName("tk")] string Token,
        [property: JsonPropertyName("lp")] string LastPrice,
        [property: JsonPropertyName("pc")] string PercentChange,
        [property: JsonPropertyName("v")] string Volume,
        [property: JsonPropertyName("o")] string Open,
        [property: JsonPropertyName("h")] string High,
        [property: JsonPropertyName("l")] string Low,
        [property: JsonPropertyName("c")] string Close,
        [property: JsonPropertyName("ap")] string AverageTradePrice,
        [property: JsonPropertyName("oi")] string OpenInterest,        
        [property: JsonPropertyName("bp1")] string BidPrice1,
        [property: JsonPropertyName("bq1")] string BidQty1,
        [property: JsonPropertyName("bo1")] string BidOrderCount1,
        [property: JsonPropertyName("bp2")] string BidPrice2,
        [property: JsonPropertyName("bq2")] string BidQty2,
        [property: JsonPropertyName("bo2")] string BidOrderCount2,
        [property: JsonPropertyName("bp3")] string BidPrice3,
        [property: JsonPropertyName("bq3")] string BidQty3,
        [property: JsonPropertyName("bo3")] string BidOrderCount3,
        [property: JsonPropertyName("bp4")] string BidPrice4,
        [property: JsonPropertyName("bq4")] string BidQty4,
        [property: JsonPropertyName("bo4")] string BidOrderCount4,
        [property: JsonPropertyName("bp5")] string BidPrice5,
        [property: JsonPropertyName("bq5")] string BidQty5,
        [property: JsonPropertyName("bo5")] string BidOrderCount5,
        [property: JsonPropertyName("sp1")] string AskPrice1,
        [property: JsonPropertyName("sq1")] string AskQty1,
        [property: JsonPropertyName("so1")] string AskOrderCount1,
        [property: JsonPropertyName("sp2")] string AskPrice2,
        [property: JsonPropertyName("sq2")] string AskQty2,
        [property: JsonPropertyName("so2")] string AskOrderCount2,
        [property: JsonPropertyName("sp3")] string AskPrice3,
        [property: JsonPropertyName("sq3")] string AskQty3,
        [property: JsonPropertyName("so3")] string AskOrderCount3,
        [property: JsonPropertyName("sp4")] string AskPrice4,
        [property: JsonPropertyName("sq4")] string AskQty4,
        [property: JsonPropertyName("so4")] string AskOrderCount4,
        [property: JsonPropertyName("sp5")] string AskPrice5,
        [property: JsonPropertyName("sq5")] string AskQty5,
        [property: JsonPropertyName("so5")] string AskOrderCount5
    );

    }