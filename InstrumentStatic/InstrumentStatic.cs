using System.Globalization;

namespace InstrumentStatic
{
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
        DEBENTURE // Debt Instrument
    }
    public abstract record BaseInstrument(
    string Exchange,   // "NSE", "NFO", etc.
    uint Token,        // Token as uint
    int LotSize,       // Lot size (e.g., 1, 900)
    string Symbol,     // Underlying symbol (e.g., "NIFTY INDEX")
    string TradingSymbol,  // Tradable symbol
    double TickSize    // Price increment (e.g., 0.05)
    );
    
    public record NSEInstrument(
        string Exchange,
        uint Token,
        int LotSize,
        string Symbol,
        string TradingSymbol,
        InstrumentType Instrument,  // Uses enum
        double TickSize
    ) : BaseInstrument(Exchange, Token, LotSize, Symbol, TradingSymbol, TickSize)
    {
        public static NSEInstrument FromCsvLine(string line)
        {
            string[] parts = line.Split(',');
            
            return new NSEInstrument(
                Exchange: parts[0],
                Token: uint.Parse(parts[1]),
                LotSize: int.Parse(parts[2]),
                Symbol: parts[3],
                TradingSymbol: parts[4],
                Instrument: ParseInstrumentType(parts[5]),
                TickSize: double.Parse(parts[6])
            );
        }

        private static InstrumentType ParseInstrumentType(string input)
        {
            return input switch
            {
                "INDEX" => InstrumentType.INDEX,
                "EQ" => InstrumentType.EQ,
                "ETF" => InstrumentType.ETF,
                "DEBENTURE" => InstrumentType.DEBENTURE,
                _ => throw new ArgumentException($"Invalid NSE Instrument: {input}")
            };
        }
    }
    
    public record NFOInstrument(
        string Exchange,
        uint Token,
        int LotSize,
        string Symbol,
        string TradingSymbol,
        DateTime Expiry,
        InstrumentType Instrument,  // Uses enum
        OptionType? OptionType,     // Nullable (null for futures)
        double StrikePrice,
        double TickSize
    ) : BaseInstrument(Exchange, Token, LotSize, Symbol, TradingSymbol, TickSize)
    {
        private static readonly CultureInfo EnUsCulture = 
            CultureInfo.GetCultureInfo("en-US");

        public static NFOInstrument FromCsvLine(string line)
        {
            string[] parts = line.Split(',');

            return new NFOInstrument(
                Exchange: parts[0],
                Token: uint.Parse(parts[1]),
                LotSize: int.Parse(parts[2]),
                Symbol: parts[3],
                TradingSymbol: parts[4],
                Expiry: DateTime.ParseExact(parts[5], "dd-MMM-yyyy", EnUsCulture),
                Instrument: ParseInstrumentType(parts[6]),
                OptionType: ParseOptionType(parts[7]),
                StrikePrice: string.IsNullOrEmpty(parts[8]) ? 0.0 : double.Parse(parts[8]),
                TickSize: double.Parse(parts[9])
            );
        }

        private static InstrumentType ParseInstrumentType(string input)
        {
            return input switch
            {
                "FUTSTK" => InstrumentType.FUTSTK,
                "OPTSTK" => InstrumentType.OPTSTK,
                "FUTIDX" => InstrumentType.FUTIDX,
                "OPTIDX" => InstrumentType.OPTIDX,
                _ => throw new ArgumentException($"Invalid NFO Instrument: {input}")
            };
        }

        private static OptionType? ParseOptionType(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            return input.ToUpper() switch
            {
                "CE" =>  InstrumentStatic.OptionType.CE ,  // Fully qualified name
                "PE" => InstrumentStatic.OptionType.PE,  // Fully qualified name
                _ => null
            };
        }

        // Validation
        public bool IsValidOption => 
            (Instrument is InstrumentType.OPTSTK or InstrumentType.OPTIDX) && 
            OptionType.HasValue;
    }
}