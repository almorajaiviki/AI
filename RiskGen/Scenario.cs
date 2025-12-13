using MarketData;

namespace RiskGen
{
    public sealed class Scenario
    {
        // -------------------------------------------
        // Internal state
        // -------------------------------------------
        private readonly List<Trade> _trades;
        private AtomicMarketSnap _currentSnap;
        private TradeGreeks _scenarioGreeks;

        private readonly object _lock = new();

        // -------------------------------------------
        // Constructor
        // -------------------------------------------
        public Scenario(IEnumerable<Trade> trades, AtomicMarketSnap snap)
        {
            if (trades == null) throw new ArgumentNullException(nameof(trades));
            if (snap == null) throw new ArgumentNullException(nameof(snap));

            _trades = trades.ToList();
            _currentSnap = snap;

            lock (_lock)
            {
                _scenarioGreeks = CalculateScenarioGreeks(_currentSnap);
            }
        }

        // -------------------------------------------
        // Public getters (read-only)
        // -------------------------------------------
        public IReadOnlyList<Trade> Trades => _trades;
        public TradeGreeks ScenarioGreeks
        {
            get
            {
                lock (_lock)
                {
                    return _scenarioGreeks;
                }
            }
        }

        // -------------------------------------------
        // Recalculate greeks with a new snapshot
        // -------------------------------------------
        public void Recalculate(AtomicMarketSnap newSnap)
        {
            if (newSnap == null) 
                throw new ArgumentNullException(nameof(newSnap));

            lock (_lock)
            {
                _currentSnap = newSnap;
                _scenarioGreeks = CalculateScenarioGreeks(newSnap);
            }
        }

        // -------------------------------------------
        // Internal aggregator
        // -------------------------------------------
        private TradeGreeks CalculateScenarioGreeks(AtomicMarketSnap snap)
        {
            double npv = 0, delta = 0, gamma = 0, vega = 0, theta = 0, rho = 0;

            foreach (var trade in _trades)
            {
                var g = trade.GetGreeks(snap);

                npv   += g.NPV;
                delta += g.Delta;
                gamma += g.Gamma;
                vega  += g.Vega;
                theta += g.Theta;
                rho   += g.Rho;
            }

            return new TradeGreeks(
                NPV: npv,
                Delta: delta,
                Gamma: gamma,
                Vega: vega,
                Theta: theta,
                Rho: rho
            );
        }
    }

    public sealed record OptionExpiryStrikesDto(
        DateTime Expiry,
        IReadOnlyList<double> Strikes
    );

    public sealed record ScenarioSnapshotDto(
        IReadOnlyList<OptionExpiryStrikesDto> Options,
        IReadOnlyList<DateTime> Futures,
        IReadOnlyList<string> Scenarios
    );
}