using QuantitativeAnalytics;
using InstrumentStatic;

namespace MarketData
{   
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
        public double OI { get; }
        public double IV { get; }
        public double Spread { get; }

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
            double iv,
            double spread)
        {
            if (string.IsNullOrWhiteSpace(tradingSymbol))
                throw new ArgumentException("Trading symbol cannot be null or empty");
            if (strike <= 0)
                throw new ArgumentException("Strike price must be positive");
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0 || iv < 0)
                throw new ArgumentException("Market data values must be non-negative");

            OptionType = optionType;
            TradingSymbol = tradingSymbol;
            Token = token;
            Strike = strike;
            Expiry = expiry;
            LTP = ltp;
            Bid = bid;
            Ask = ask;
            OI = oi;
            IV = iv;
            Spread = spread;
        }
    }

    public class Option
    {
        private readonly OptionType _optionType;
        private readonly string _tradingSymbol;
        private readonly uint _token;
        private readonly double _strike;
        private readonly DateTime _expiry;
        private double _ltp;
        private double _bid;
        private double _ask;
        private double _oi;
        private double _iv;
        private readonly object _lock = new();

        public OptionType OptionType => _optionType;
        public string TradingSymbol => _tradingSymbol;
        public uint Token => _token;
        public double Strike => _strike;
        public DateTime Expiry => _expiry;

        public Option(
            OptionType optionType,
            string tradingSymbol,
            uint token,
            double strike,
            DateTime expiry,
            DateTime now,
            Index index,
            RFR rfr,
            double ltp,
            double bid,
            double ask,
            double oi,
            double iv)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(tradingSymbol))
                throw new ArgumentException("Trading symbol cannot be null or empty", nameof(tradingSymbol));
            if (strike <= 0)
                throw new ArgumentException("Strike price must be positive", nameof(strike));
            if (expiry <= now)
                throw new ArgumentException("Expiry date must be in the future", nameof(expiry));
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0 || iv < 0)
                throw new ArgumentException("Market data values must be non-negative");
            if (rfr == null)
                throw new ArgumentNullException(nameof(rfr));

            // Initialize fields
            _optionType = optionType;
            _tradingSymbol = tradingSymbol;
            _token = token;
            _strike = strike;
            _expiry = expiry;
            _ltp = ltp;
            _bid = bid;
            _ask = ask;
            _oi = oi;

            // Calculate IV using passed RFR
            try
            {
                IndexSnapshot indexSnapshot = index.GetSnapshot();
                double timeToExpiry = index.Calendar.GetYearFraction(now, _expiry);
                
                _iv = Black76.ComputeIV(
                    isCall: _optionType == OptionType.CE,
                    forwardPrice: indexSnapshot.ImpliedFuture,
                    strike: _strike,
                    timeToExpiry: timeToExpiry,
                    riskFreeRate: rfr.Value,
                    marketPrice: ltp);
            }
            catch (Exception)
            {
                _iv = double.NaN;
            }
        }

        public void UpdateMarketData(
            double ltp,
            double bid,
            double ask,
            double oi,
            DateTime now,
            Index index,
            RFR rfr)
        {
            // Validate inputs
            if (ltp < 0 || bid < 0 || ask < 0 || oi < 0)
                throw new ArgumentException("Market data values must be non-negative");
            if (rfr == null)
                throw new ArgumentNullException(nameof(rfr));

            lock (_lock)
            {
                // Update market data
                _ltp = ltp;
                _bid = bid;
                _ask = ask;
                _oi = oi;

                // Recalculate IV using passed RFR
                try
                {
                    IndexSnapshot indexSnapshot = index.GetSnapshot();
                    double timeToExpiry = index.Calendar.GetYearFraction(now, _expiry);

                    _iv = double.IsFinite(_iv)
                        ? Black76.ComputeIV(
                            isCall: _optionType == OptionType.CE,
                            forwardPrice: indexSnapshot.ImpliedFuture,
                            strike: _strike,
                            timeToExpiry: timeToExpiry,
                            riskFreeRate: rfr.Value,
                            marketPrice: ltp,
                            initialGuess: _iv)
                        : Black76.ComputeIV(
                            isCall: _optionType == OptionType.CE,
                            forwardPrice: indexSnapshot.ImpliedFuture,
                            strike: _strike,
                            timeToExpiry: timeToExpiry,
                            riskFreeRate: rfr.Value,
                            marketPrice: ltp);
                }
                catch (Exception)
                {
                    _iv = double.NaN;
                }
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
                    iv: _iv,
                    spread: _bid - _ask);
            }
        }
    }
}