using MarketData;
using CalendarLib;
using QuantitativeAnalytics;

namespace RiskGen
{
    public sealed class Scenario
    {
        // -------------------------------------------
        // Internal state
        // -------------------------------------------
        // Delta is computed as NPV change for a 0.1% forward move
        private const double DELTA_BUMP_PCT = 0.001; // 0.1%
        private const double DELTA_SCALE_FACTOR = 1.0 / DELTA_BUMP_PCT; // = 1000
        private const double VEGA_BUMP_ABS = 0.01;   // 1 vol point
        private const double VEGA_SCALE_FACTOR = 1.0 / VEGA_BUMP_ABS; // = 100
        private readonly List<Trade> _trades;
        private readonly IReadOnlyList<ScenarioBump> _bumps;
        private IReadOnlyDictionary<Trade, MktDataUsed> _liveMktData;
        private readonly ScenarioBaseState _baseState;
        private IReadOnlyDictionary<Trade, PnLAttributes> _tradePnL
            = new Dictionary<Trade, PnLAttributes>();

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
            _bumps = CreateDefaultBumps(snap);

            lock (_lock)
            {
                CalculateGreeks(snap);
                // BaseGreeks intentionally capture the INITIAL TradeGreeks reference.
                // _tradeGreeks is replaced (not mutated) on market updates,
                // so BaseGreeks remain immutable inception greeks for PnL attribution.
                _baseState = SetBase(snap);
                CalculatePnL(snap);
            }
        }

        private ScenarioBaseState SetBase(AtomicMarketSnap snap)
        {
            return new ScenarioBaseState(snap.InitializationTime, 
                _tradeGreeks.ToDictionary(                                
                        kvp => kvp.Key,                                
                        kvp => ExtractMarketInputs(
                            snap,
                            kvp.Key,
                            baseForward: snap.ForwardCurve!.GetForwardPrice(
                                snap.Calendar.GetYearFraction(
                                    snap.InitializationTime,
                                    kvp.Key.Instrument.Expiry))
                        )
                    ),
                    _tradeGreeks, snap.VolSurface);
        }

        private static MktDataUsed ExtractMarketInputs(
            AtomicMarketSnap snap,
            Trade trade,
            double? baseForward = null
        )
        {
            double tte = snap.Calendar.GetYearFraction(
                snap.InitializationTime,
                trade.Instrument.Expiry);

            double forward = snap.ForwardCurve!.GetForwardPrice(tte);

            // base moneyness uses baseForward if provided
            double fBase = baseForward ?? forward;

            double logMnyBase = Math.Log(trade.Instrument.Strike / fBase);
            double logMnyLive = Math.Log(trade.Instrument.Strike / forward);

            double volAtBaseMny = snap.VolSurface.GetVol(tte, logMnyBase);
            double volAtLiveMny = snap.VolSurface.GetVol(tte, logMnyLive);

            return new MktDataUsed(
                tte,
                forward,
                volAtBaseMny,
                volAtLiveMny,
                snap.RiskFreeRate
            );
        }

        private IReadOnlyList<ScenarioBump> CreateDefaultBumps(AtomicMarketSnap snap)
        {
            if (snap == null)
                throw new ArgumentNullException(nameof(snap));

            // -------------------------------
            // Time bumps
            // -------------------------------
            var calendar = snap.Calendar;

            DateTime t0 = snap.InitializationTime;

            // Next business day according to CalendarLib
            DateTime t1 = calendar.AddBusinessDays(t0, 1);

            // Year fraction difference
            double dt1 = calendar.GetYearFraction(t0, t1);

            double[] time = new[]
            {
                0.0,
                -dt1    //keep this consistent with theta calculations, where we have -ve tte bump to indicate lesser time to expiry (with the flow of time)
            };

            // -------------------------------
            // Forward & Vol bumps (unchanged)
            // -------------------------------
            double[] fwd = { 0.0, 0.01, 0.02, 0.05, -0.01, -0.02, -0.05 };
            double[] vol = { 0.0, 0.01, 0.02, -0.01, -0.02 };

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

        public IReadOnlyDictionary<Trade, PnLAttributes> TradePnL
        {
            get { lock (_lock) return _tradePnL; }
        }
        public IReadOnlyDictionary<ScenarioBump, double> BumpResults
        {
            get { lock (_lock) return _bumpResults; }
        }

        public IReadOnlyDictionary<ScenarioBump, double> BumpPnL
        {
            get
            {
                lock (_lock)
                {
                    if (_bumpResults.Count == 0)
                        return new Dictionary<ScenarioBump, double>();

                    // base scenario = (0,0,0)
                    var baseKey = _bumpResults.Keys.FirstOrDefault(b =>
                        b.TimeShiftYears == 0.0 &&
                        b.ForwardShiftPct == 0.0 &&
                        b.VolShiftAbs == 0.0);

                    if (baseKey == null)
                        return new Dictionary<ScenarioBump, double>();

                    double baseNpv = _bumpResults[baseKey];

                    var pnl = new Dictionary<ScenarioBump, double>(_bumpResults.Count);

                    foreach (var kvp in _bumpResults)
                    {
                        pnl[kvp.Key] = kvp.Value - baseNpv;
                    }

                    return pnl;
                }
            }
        }

        // -------------------------------------------
        // Recalculate greeks + scenario NPVs
        // -------------------------------------------
        private void CalculateGreeks(AtomicMarketSnap snap)
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
                _liveMktData = tradeGreeks.ToDictionary(kvp => kvp.Key, 
                    kvp => ExtractMarketInputs(
                        snap,
                        kvp.Key,
                        baseForward: snap.ForwardCurve!.GetForwardPrice( snap.Calendar.GetYearFraction( snap.InitializationTime, kvp.Key.Instrument.Expiry))
                    )
                );
            }
        }

        // -------------------------------------------
        // Bump logic
        // -------------------------------------------
        private IReadOnlyDictionary<ScenarioBump, double> CalculateScenarioNPVs(
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

                    double forward = baseSnap.ForwardCurve!.GetForwardPrice(tte);
                    forward *= (1.0 + bump.ForwardShiftPct);

                    var volSurface = bump.VolShiftAbs == 0.0
                        ? baseSnap.VolSurface
                        : baseSnap.VolSurface.Bump(bump.VolShiftAbs);

                    // ---- apply bumps (as some valuations will be calculated for one day ijn the future) ----
                    double tteBumped = Math.Max(0.0, tte + bump.TimeShiftYears);

                    var tradeNPV = trade.CalcNPV(
                        tteBumped,
                        forward,
                        baseSnap.RiskFreeRate,
                        volSurface);

                    totalNPV += tradeNPV.NPV;
                }

                results[bump] = totalNPV;
            }

            return results;
        }
        
        public void CalculateGreeksAndPnL(AtomicMarketSnap snap)
        {
            // For now, just greeks.
            // PnL attribution will be added here later.
            CalculateGreeks(snap);
            CalculatePnL(snap);
        }

        // Requires _liveMktData to be populated by CalculateGreeks()
        private void CalculatePnL(AtomicMarketSnap liveSnap)
        {
            var pnl = new Dictionary<Trade, PnLAttributes>();

            foreach (var trade in _trades)
            {
                // --- base inputs ---
                var baseInputs = _baseState.TradeInputs[trade];
                var baseGreeks = _baseState.BaseGreeks[trade];

                // --- live inputs ---
                var liveInputs = _liveMktData[trade];

                // --- forward % change ---
                double dF_pct =
                    (liveInputs.Forward - baseInputs.Forward)
                    / baseInputs.Forward;

                //calculate bottom line (Actual PnL)
                double actualPnL = _tradeGreeks[trade].NPV - _baseState.BaseGreeks[trade].NPV;

                //calculate theta
                double rolledThetaNPV = trade.CalcNPV(
                    liveInputs.TTE,
                    baseInputs.Forward,
                    baseInputs.Rfr,
                    _baseState.BaseVolSurface!).NPV;
                double thetaPnL = rolledThetaNPV - _baseState.BaseGreeks[trade].NPV;

                //calculate dR
                double rolledThetaRFRNPV = trade.CalcNPV(
                    liveInputs.TTE,
                    baseInputs.Forward,
                    liveInputs.Rfr,
                    _baseState.BaseVolSurface!).NPV;
                double dRfrPnL = rolledThetaRFRNPV - rolledThetaNPV;

                //calculate risk based pnls
                // --- vol change ---
                double dVol_surface =
                    liveInputs.VolAtBaseMny
                - baseInputs.VolAtBaseMny;

                double dVol_mny =
                    liveInputs.VolAtLiveMny
                - liveInputs.VolAtBaseMny;

                // --- delta ---
                double deltaPnL =
                    baseGreeks.Delta * dF_pct * DELTA_SCALE_FACTOR;

                // --- gamma ---
                double gammaPnL =
                    0.5 * baseGreeks.Gamma
                    * dF_pct * dF_pct
                    * DELTA_SCALE_FACTOR * DELTA_SCALE_FACTOR;

                // --- vega ---
                double vegaPnL =
                    baseGreeks.Vega
                    * dVol_surface
                    * VEGA_SCALE_FACTOR;

                // --- volga ---
                double volgaPnL =
                    0.5 * baseGreeks.Volga
                    * dVol_surface * dVol_surface
                    * VEGA_SCALE_FACTOR * VEGA_SCALE_FACTOR;

                // --- vanna ---
                double vannaPnL =
                    baseGreeks.Vanna
                    * (dF_pct * DELTA_SCALE_FACTOR)
                    * (dVol_mny * VEGA_SCALE_FACTOR);
                
                //now calculate reval residual pnl
                //fwd reval residual pnl
                double rolledThetaRFRFwdNPV = trade.CalcNPV(
                    liveInputs.TTE,
                    liveInputs.Forward,
                    liveInputs.Rfr,
                    _baseState.BaseVolSurface!).NPV;
                double fwdRevalResidualPnL = rolledThetaRFRFwdNPV - rolledThetaRFRNPV - deltaPnL - gammaPnL;

                //vol reval residual pnl
                double rolledThetaRFRVolNPV = trade.CalcNPV(
                    liveInputs.TTE,
                    baseInputs.Forward,
                    liveInputs.Rfr,
                    liveSnap.VolSurface!).NPV;
                double volRevalResidualPnL = rolledThetaRFRVolNPV - rolledThetaRFRNPV - vegaPnL - volgaPnL - vannaPnL;

                //cross residual
                double rolledThetaRFRFwdVolCrossNPV = trade.CalcNPV(
                    liveInputs.TTE,
                    liveInputs.Forward,
                    liveInputs.Rfr,
                    liveSnap.VolSurface!).NPV;

                double crossResidualPnL = (rolledThetaRFRFwdVolCrossNPV - rolledThetaRFRNPV) - (volRevalResidualPnL + fwdRevalResidualPnL) - (deltaPnL + gammaPnL) - (vegaPnL + volgaPnL + vannaPnL);

                double totalExplainedPnL = thetaPnL + dRfrPnL + deltaPnL + gammaPnL + vegaPnL + volgaPnL + vannaPnL + fwdRevalResidualPnL + volRevalResidualPnL + crossResidualPnL;
                double totalUnexplainedPnL = actualPnL - totalExplainedPnL;

                pnl[trade] = new PnLAttributes(dF_pct, actualPnL, thetaPnL, dRfrPnL, deltaPnL, gammaPnL, vegaPnL, volgaPnL, vannaPnL, fwdRevalResidualPnL, volRevalResidualPnL, crossResidualPnL, totalExplainedPnL, totalUnexplainedPnL);
            }

            lock (_lock)
            {
                _tradePnL = pnl;
            }
        }
    }

    //helper classes for DTOs and other records
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

    internal sealed record MktDataUsed
    (
        double TTE,
        double Forward,
        double VolAtBaseMny,
        double VolAtLiveMny,
        double Rfr
    );

    internal sealed record ScenarioBaseState
    (
        DateTime BaseTime,
        IReadOnlyDictionary<Trade, MktDataUsed> TradeInputs,
        IReadOnlyDictionary<Trade, TradeGreeks> BaseGreeks,
        IParametricModelSurface? BaseVolSurface = null
    );

    public sealed record PnLAttributes
    (
        double dF_pct,
        double ActualPnL,
        double ThetaPnL,
        double dRfrPnL,
        double DeltaPnL,
        double GammaPnL,
        double VegaPnL,
        double VolgaPnL,
        double VannaPnL,
        double FwdRevalResidualPnL,
        double VolRevalResidualPnL,
        double crossResidualPnL,
        double TotalExplainedPnL,
        double TotalUnexplainedPnL
    );
}