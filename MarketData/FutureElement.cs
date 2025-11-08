using QuantitativeAnalytics;

namespace MarketData
{
    public class FutureElement
    {
        private readonly Future _future;
        private FutureGreeks _futureGreeks;
        private FutureSpreads _futureSpreads;
        private readonly object _lock = new();

        public Future Future => _future;
        public FutureGreeks FutureGreeks => _futureGreeks;
        public FutureSpreads FutureSpreads => _futureSpreads;
        public string TradingSymbol => _future.TradingSymbol;

        public FutureElement(Future future, Index index, bool bUseMktFuture, IParametricModelSurface volSurface, RFR rfr, DateTime now, IGreeksCalculator greeksCalculator)
        {
            // Input validation
            if (future == null) throw new ArgumentNullException(nameof(future));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (volSurface == null) throw new ArgumentNullException(nameof(volSurface));
            if (rfr == null) throw new ArgumentNullException(nameof(rfr));
            if (greeksCalculator == null) throw new ArgumentNullException(nameof(greeksCalculator));

            _future = future;

            var futureSnapshot = future.GetSnapshot();
            var indexSnapshot = index.GetSnapshot();
            double timeToExpiry = index.Calendar.GetYearFraction(now, future.Expiry);

            _futureGreeks = new FutureGreeks(
                futureSnapshot,
                indexSnapshot.IndexSpot, bUseMktFuture, // spot
                rfr.Value,               // riskFreeRate
                indexSnapshot.DivYield,  // dividendYield
                timeToExpiry,
                volSurface,
                greeksCalculator);

            _futureSpreads = new FutureSpreads(futureSnapshot, _futureGreeks); // Corrected instantiation
        }

        // The UpdateGreeks method needs to be updated to create new instances
        public void UpdateGreeks(Index index, bool bUseMktFuture, IParametricModelSurface volSurface, RFR rfr, DateTime now, IGreeksCalculator greeksCalculator)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (volSurface == null) throw new ArgumentNullException(nameof(volSurface));
            if (rfr == null) throw new ArgumentNullException(nameof(rfr));
            if (greeksCalculator == null) throw new ArgumentNullException(nameof(greeksCalculator));

            lock (_lock)
            {
                var futureSnapshot = _future.GetSnapshot();
                var indexSnapshot = index.GetSnapshot();
                double timeToExpiry = index.Calendar.GetYearFraction(now, _future.Expiry);

                _futureGreeks = new FutureGreeks(
                    futureSnapshot,
                    indexSnapshot.IndexSpot, bUseMktFuture, // spot
                    rfr.Value,               // riskFreeRate
                    indexSnapshot.DivYield,  // dividendYield
                    timeToExpiry,
                    volSurface,
                    greeksCalculator);

                _futureSpreads = new FutureSpreads(futureSnapshot, _futureGreeks); // Corrected instantiation
            }
        }
    }
}
