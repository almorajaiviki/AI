using InstrumentStatic;

namespace MarketData
{
    // ========================================================================
    //  OptionSnapshot  (IMMUTABLE RUNTIME SNAPSHOT)
    // ========================================================================
    public readonly struct OptionSnapshot
    {
        public OptionType OptionType { get; }
        public string TradingSymbol { get; }
        public uint Token { get; }
        public double Strike { get; }
        public DateTime Expiry { get; }
        public double LTP { get; }
        public double Bid { get; }
        public double Ask { get; }
        public double Mid => (Bid + Ask) / 2;
        public double OI { get; }

        // *** NEW FIELD ***
        public int LotSize { get; }

        public OptionSnapshot(
            OptionType optionType,
            string tradingSymbol,
            uint token,
            double strike,
            DateTime expiry,
            double ltp,
            double bid,
            double ask,
            double oi,
            int lotSize)    // <-- NEW PARAM
        {
            if (string.IsNullOrWhiteSpace(tradingSymbol))
                throw new ArgumentException("Trading symbol cannot be null or empty");
            if (strike <= 0)
                throw new ArgumentException("Strike price must be positive");
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0)
                throw new ArgumentException("Market data values must be non-negative");
            if (lotSize <= 0)
                throw new ArgumentException("LotSize must be positive");

            OptionType = optionType;
            TradingSymbol = tradingSymbol;
            Token = token;
            Strike = strike;
            Expiry = expiry;
            LTP = ltp;
            Bid = bid;
            Ask = ask;
            OI = oi;
            LotSize = lotSize;   // <-- NEW
        }
    }

    // ========================================================================
    //  Option  (LIVE MARKET OBJECT)
    // ========================================================================
    public class Option
    {
        private readonly OptionType _optionType;
        private readonly string _tradingSymbol;
        private readonly uint _token;
        private readonly double _strike;
        private readonly DateTime _expiry;
        private readonly int _lotSize;   // <-- NEW

        private double _ltp;
        private double _bid;
        private double _ask;
        private double _oi;

        private readonly object _lock = new();

        public OptionType OptionType => _optionType;
        public string TradingSymbol => _tradingSymbol;
        public uint Token => _token;
        public double Strike => _strike;
        public DateTime Expiry => _expiry;

        // *** NEW PROPERTY ***
        public int LotSize => _lotSize;

        public Option(
            OptionType optionType,
            string tradingSymbol,
            uint token,
            double strike,
            DateTime expiry,
            DateTime now,
            RFR rfr,
            double ltp,
            double bid,
            double ask,
            double oi,
            int lotSize)  // <-- NEW PARAM
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(tradingSymbol))
                throw new ArgumentException("Trading symbol cannot be null or empty", nameof(tradingSymbol));
            if (strike <= 0)
                throw new ArgumentException("Strike price must be positive", nameof(strike));
            if (expiry <= now)
                throw new ArgumentException("Expiry date must be in the future", nameof(expiry));
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0)
                throw new ArgumentException("Market data values must be non-negative");
            if (rfr == null)
                throw new ArgumentNullException(nameof(rfr));
            if (lotSize <= 0)
                throw new ArgumentException("Lot size must be positive", nameof(lotSize));

            _optionType = optionType;
            _tradingSymbol = tradingSymbol;
            _token = token;
            _strike = strike;
            _expiry = expiry;

            _ltp = ltp;
            _bid = bid;
            _ask = ask;
            _oi = oi;

            _lotSize = lotSize;  // <-- NEW
        }

        public void UpdateMarketData(
            double ltp,
            double bid,
            double ask,
            double oi,
            RFR rfr)
        {
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0)
                throw new ArgumentException("Market data values must be non-negative");
            if (rfr == null)
                throw new ArgumentNullException(nameof(rfr));

            lock (_lock)
            {
                _ltp = ltp;
                _bid = bid;
                _ask = ask;
                _oi = oi;
            }
        }

        public OptionSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new OptionSnapshot(
                    optionType: _optionType,
                    tradingSymbol: _tradingSymbol,
                    token: _token,
                    strike: _strike,
                    expiry: _expiry,
                    ltp: _ltp,
                    bid: _bid,
                    ask: _ask,
                    oi: _oi,
                    lotSize: _lotSize   // <-- NEW
                );
            }
        }
    }
}