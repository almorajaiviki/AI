using QuantitativeAnalytics; // For ProductType
using InstrumentStatic; // For OptionType

namespace RiskGen
{
    public sealed record Instrument
    {
        // --------------------------------------------------
        // Common properties for all tradable products
        // --------------------------------------------------
        public string TradingSymbol { get; }
        //public string Exchange { get; }
        //public uint Token { get; }
        public int LotSize { get; }

        // --------------------------------------------------
        // Classification from QuantitativeAnalytics
        // --------------------------------------------------
        public ProductType ProductType { get; }        // Option, Future, Fee (later)        

        // --------------------------------------------------
        // Backing fields (nullable)
        // --------------------------------------------------
        private readonly double? _strike;
        private readonly OptionType? _optionType;
        private readonly DateTime? _expiry;

        // --------------------------------------------------
        // Constructor
        // --------------------------------------------------
        internal Instrument(
            string tradingSymbol,            
            int lotSize,
            ProductType productType,            
            double? strike = null,
            OptionType? optionType = null,
            DateTime? expiry = null)
        {
            TradingSymbol = tradingSymbol ?? throw new ArgumentNullException(nameof(tradingSymbol));
            //Exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));

            //Token = token;
            LotSize = lotSize;

            ProductType = productType;            

            _strike = strike;
            _optionType = optionType;
            _expiry = expiry;
        }

        // --------------------------------------------------
        // Validated Accessors
        // --------------------------------------------------

        internal double Strike =>
            ProductType == ProductType.Option
                ? _strike ?? throw new InvalidOperationException("Strike is missing for this Option.")
                : throw new InvalidOperationException("Strike is only valid for Option instruments.");

        internal OptionType OptionType =>
            ProductType == ProductType.Option
                ? _optionType ?? throw new InvalidOperationException("OptionType is missing for this Option.")
                : throw new InvalidOperationException("OptionType is only valid for Option instruments.");

        internal DateTime Expiry =>
            _expiry ?? throw new InvalidOperationException("Expiry is not defined for this instrument.");
    }
}