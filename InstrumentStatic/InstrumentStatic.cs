using System.Text.Json.Serialization;
namespace InstrumentStatic
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OptionType
    {
        CE,  // Call Option
        PE   // Put Option
    }

    public enum InstrumentType
    {
        // Common
        INDEX,    // Index (e.g., NIFTY 50)

        // NFO-specific
        FUTSTK,   // Equity Futures
        OPTSTK,   // Equity Options
        FUTIDX,   // Index Futures
        OPTIDX,   // Index Options

        // NSE-specific
        EQ,       // Equity
        ETF,      // Exchange-Traded Fund
        DEBENTURE, // Debt Instrument
        OTHERS  // Other types
    }

    public abstract record BaseInstrument(
        string Exchange,   // "NSE", "NFO", etc.
        uint Token,        // Token as uint
        int LotSize,       // Lot size (e.g., 1, 900)
        string Symbol,     // Underlying symbol (e.g., "NIFTY INDEX")
        string TradingSymbol,  // Tradable symbol
        double TickSize    // Price increment (e.g., 0.05)
    )
    {
        public static OptionType? ParseOptionType(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            input = input.ToUpperInvariant();

            return input switch
            {
                "CE" => OptionType.CE,
                "PE" => OptionType.PE,
                _ => null
            };
        }

        public static InstrumentType ParseInstrumentType(string input)
        {
            input = input?.ToUpperInvariant() ?? string.Empty;

            return input switch
            {
                "INDEX" => InstrumentType.INDEX,
                "EQ" => InstrumentType.EQ,
                "ETF" => InstrumentType.ETF,
                "DEBENTURE" => InstrumentType.DEBENTURE,
                "OPTIDX" => InstrumentType.OPTIDX,
                "FUTIDX" => InstrumentType.FUTIDX,
                "OPTSTK" => InstrumentType.OPTSTK,
                "FUTSTK" => InstrumentType.FUTSTK,
                _ => InstrumentType.OTHERS
            };
        }
    }

    public record NSEInstrument(
        string Exchange,
        uint Token,
        int LotSize,
        string Symbol,
        string TradingSymbol,
        InstrumentType Instrument,
        double TickSize
    ) : BaseInstrument(Exchange, Token, LotSize, Symbol, TradingSymbol, TickSize);

    public record NFOInstrument(
        string Exchange,
        uint Token,
        int LotSize,
        string Symbol,
        string TradingSymbol,
        DateTime Expiry,
        InstrumentType Instrument,
        OptionType? OptionType,
        double StrikePrice,
        double TickSize
    ) : BaseInstrument(Exchange, Token, LotSize, Symbol, TradingSymbol, TickSize);
}
