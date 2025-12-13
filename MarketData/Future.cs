namespace MarketData
{
    // ========================================================================
    //  Future  (LIVE MARKET OBJECT)
    // ========================================================================
    public class Future
    {
        private readonly string _tradingSymbol;
        private readonly uint _token;
        private readonly DateTime _expiry;
        private readonly int _lotSize;   // <-- NEW

        private double _ltp;
        private double _bid;
        private double _ask;
        private double _oi;

        private readonly object _lock = new();

        public string TradingSymbol => _tradingSymbol;
        public uint Token => _token;
        public DateTime Expiry => _expiry;

        // NEW: expose LotSize
        public int LotSize => _lotSize;

        public Future(
            string tradingSymbol,
            uint token,
            DateTime expiry,
            DateTime now,
            RFR rfr,
            double ltp,
            double bid,
            double ask,
            double oi,
            int lotSize)  // <-- NEW PARAM
        {
            if (string.IsNullOrWhiteSpace(tradingSymbol))
                throw new ArgumentException("Trading symbol cannot be null or empty", nameof(tradingSymbol));
            if (expiry <= now)
                throw new ArgumentException("Expiry date must be in the future", nameof(expiry));
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0)
                throw new ArgumentException("Market data values must be non-negative");
            if (rfr == null)
                throw new ArgumentNullException(nameof(rfr));
            if (lotSize <= 0)
                throw new ArgumentException("Lot size must be positive", nameof(lotSize));

            _tradingSymbol = tradingSymbol;
            _token = token;
            _expiry = expiry;

            _ltp = ltp;
            _bid = bid;
            _ask = ask;
            _oi = oi;

            _lotSize = lotSize;   // <-- STORE LOT SIZE
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

        public FutureSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new FutureSnapshot(
                    tradingSymbol: _tradingSymbol,
                    token: _token,
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

    // ========================================================================
    //  FutureSnapshot  (IMMUTABLE SNAPSHOT)
    // ========================================================================
    public readonly struct FutureSnapshot
    {
        public string TradingSymbol { get; }
        public uint Token { get; }
        public DateTime Expiry { get; }
        public double LTP { get; }
        public double Bid { get; }
        public double Ask { get; }
        public double Mid => (Bid + Ask) / 2;
        public double OI { get; }

        // NEW FIELD
        public int LotSize { get; }

        public FutureSnapshot(
            string tradingSymbol,
            uint token,
            DateTime expiry,
            double ltp,
            double bid,
            double ask,
            double oi,
            int lotSize)  // <-- NEW PARAM
        {
            if (string.IsNullOrWhiteSpace(tradingSymbol))
                throw new ArgumentException("Trading symbol cannot be null or empty", nameof(tradingSymbol));
            if (expiry == default)
                throw new ArgumentException("Expiry date must be valid", nameof(expiry));
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0)
                throw new ArgumentException("Market data values must be non-negative");
            if (lotSize <= 0)
                throw new ArgumentException("Lot size must be positive", nameof(lotSize));

            TradingSymbol = tradingSymbol;
            Token = token;
            Expiry = expiry;
            LTP = ltp;
            Bid = bid;
            Ask = ask;
            OI = oi;

            LotSize = lotSize;   // <-- NEW
        }
    }
}