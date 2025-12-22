using MarketData;
using CalendarLib;



namespace RiskGen
{
    public sealed class Scenario
    {
        // -------------------------------------------
        // Internal state
        // -------------------------------------------
        private readonly List<Trade> _trades;
        private readonly IReadOnlyList<ScenarioBump> _bumps;

        private AtomicMarketSnap _currentSnap;

        private readonly object _lock = new();

        private IReadOnlyDictionary<Trade, TradeGreeks> _tradeGreeks
            = new Dictionary<Trade, TradeGreeks>();

        private IReadOnlyDictionary<ScenarioBump, double> _bumpResults
            = new Dictionary<ScenarioBump, double>();

        // -------------------------------------------
        // Constructor
        // -------------------------------------------
        public Scenario(IEnumerable<Trade> trades, AtomicMarketSnap snap)
        {
            if (trades == null) throw new ArgumentNullException(nameof(trades));
            if (snap == null) throw new ArgumentNullException(nameof(snap));

            _trades = trades.ToList();
            _currentSnap = snap;

            _bumps = CreateDefaultBumps();

            CalculateGreeks(snap);
        }

        private static IReadOnlyList<ScenarioBump> CreateDefaultBumps()
        {
            double[] time = { 0.0 };
            double[] fwd  = { 0.0, 0.01, 0.02, 0.05, -0.01, -0.02, -0.05 };
            double[] vol  = { 0.0, 0.01, 0.02, -0.01, -0.02 };

            var list = new List<ScenarioBump>();

            foreach (var t in time)
            foreach (var f in fwd)
            foreach (var v in vol)
                list.Add(new ScenarioBump(t, f, v));

            return list;
        }

        // -------------------------------------------
        // Public getters (read-only)
        // -------------------------------------------
        public IReadOnlyList<Trade> Trades => _trades;

        public IReadOnlyDictionary<Trade, TradeGreeks> TradeGreeks
        {
            get { lock (_lock) return _tradeGreeks; }
        }

        public IReadOnlyDictionary<ScenarioBump, double> BumpResults
        {
            get { lock (_lock) return _bumpResults; }
        }

        // -------------------------------------------
        // Recalculate greeks + scenario NPVs
        // -------------------------------------------
        public void CalculateGreeks(AtomicMarketSnap snap)
        {
            var tradeGreeks = new Dictionary<Trade, TradeGreeks>();

            foreach (var trade in _trades)
            {
                tradeGreeks[trade] = trade.GetGreeks(snap);
            }

            var bumpResults = CalculateScenarioNPVs(snap);

            lock (_lock)
            {
                _tradeGreeks = tradeGreeks;
                _bumpResults = bumpResults;
                _currentSnap = snap;
            }
        }

        // -------------------------------------------
        // Bump logic
        // -------------------------------------------
        public IReadOnlyDictionary<ScenarioBump, double> CalculateScenarioNPVs(
            AtomicMarketSnap baseSnap)
        {
            var results = new Dictionary<ScenarioBump, double>();

            foreach (var bump in _bumps)
            {
                double totalNPV = 0.0;

                foreach (var trade in _trades)
                {
                    // ---- base inputs ----
                    double tte = baseSnap.Calendar.GetYearFraction(
                        baseSnap.InitializationTime,
                        trade.Instrument.Expiry);

                    tte = Math.Max(0.0, tte + bump.TimeShiftYears);

                    double forward = baseSnap.ForwardCurve!.GetForwardPrice(tte);
                    forward *= (1.0 + bump.ForwardShiftPct);

                    var volSurface = bump.VolShiftAbs == 0.0
                        ? baseSnap.VolSurface
                        : baseSnap.VolSurface.Bump(bump.VolShiftAbs);

                    var tradeNPV = trade.CalcNPV(
                        tte,
                        forward,
                        baseSnap.RiskFreeRate,
                        volSurface);

                    totalNPV += tradeNPV.NPV;
                }

                results[bump] = totalNPV;
            }

            return results;
        }
        
    }


    /* public sealed class Scenario
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
 */
    public sealed record OptionExpiryStrikesDto(
        DateTime Expiry,
        IReadOnlyList<double> Strikes
    );

    public sealed record ScenarioSnapshotDto(
        IReadOnlyList<OptionExpiryStrikesDto> Options,
        IReadOnlyList<DateTime> Futures,
        IReadOnlyList<string> Scenarios
    );

    /// Immutable valuation bump applied on top of the base market snapshot.
    /// All values are additive shifts.
    /// </summary>
    public sealed record ScenarioBump
    (
        double TimeShiftYears,   // +1 Biz day/365 = one day forward
        double ForwardShiftPct,  // +0.01 = +1% forward move
        double VolShiftAbs       // +0.01 = +1 vol (absolute)
    );
}