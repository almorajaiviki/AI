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
        
        private readonly object _lock = new();

        private IReadOnlyDictionary<Trade, TradeGreeks> _tradeGreeks
            = new Dictionary<Trade, TradeGreeks>();

        // -------------------------------------------
        // Constructor
        // -------------------------------------------
        public Scenario(IEnumerable<Trade> trades, AtomicMarketSnap snap)
        {
            if (trades == null) throw new ArgumentNullException(nameof(trades));
            if (snap == null) throw new ArgumentNullException(nameof(snap));

            _trades = trades.ToList();
            _currentSnap = snap;

            CalculateGreeks(snap);
        }

        // -------------------------------------------
        // Public getters (read-only)
        // -------------------------------------------
        public IReadOnlyList<Trade> Trades => _trades;       
        public IReadOnlyDictionary<Trade, TradeGreeks> TradeGreeks
        {
            get
            {
                lock (_lock)
                {
                    return _tradeGreeks;
                }
            }
        }
   
        // -------------------------------------------
        // Recalculate greeks with a new snapshot
        // -------------------------------------------
        public void CalculateGreeks(AtomicMarketSnap snap)
        {
            var tradeGreeks = new Dictionary<Trade, TradeGreeks>();
            
            foreach (var trade in _trades)
            {
                var g = trade.GetGreeks(snap);

                tradeGreeks[trade] = g;

            }

            lock (_lock)
            {
                _tradeGreeks = tradeGreeks;

            }
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