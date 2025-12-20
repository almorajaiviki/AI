using MarketData;
using QuantitativeAnalytics;

namespace RiskGen
{
    public sealed class Trade
    {
        // --------------------------------------------------
        // Immutable core properties
        // --------------------------------------------------
        public Instrument Instrument { get; }
        public int Lots { get; }  // Signed: +long, -short

        // --------------------------------------------------
        // Constructor
        // --------------------------------------------------
        public Trade(Instrument instrument, int lots)
        {
            Instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));

            if (lots == 0)
                throw new ArgumentException("Trade quantity cannot be zero.", nameof(lots));

            Lots = lots;
        }

        // --------------------------------------------------
        // Derived helper properties
        // --------------------------------------------------
        public bool IsLong => Lots > 0;
        public bool IsShort => Lots < 0;

        public override string ToString()
            => $"{Instrument.TradingSymbol} x {Lots}";

        public TradeGreeks GetGreeks(
            AtomicMarketSnap snap,
            IGreeksCalculator? greeksCalculator = null)
        {
            if (snap == null) throw new ArgumentNullException(nameof(snap));

            // default to your Black76 adapter singleton if none provided
            greeksCalculator ??= Black76GreeksCalculator.Instance;

            // 1) Classification
            var productType = Instrument.ProductType; // QuantitativeAnalytics.ProductType

            // for options determine call/put; for futures the flag can be a dummy (calculator ignores for futures)
            bool isCall = productType == ProductType.Option
                ? (Instrument.OptionType == InstrumentStatic.OptionType.CE)
                : true;

            // strike only valid for options
            double strike = productType == ProductType.Option
                ? Instrument.Strike
                : double.NaN;

            // 2) time-to-expiry (tte) — use your MarketHelper calendar (same approach used elsewhere).
            //    This uses the MarketHelperDict to find the NSE calendar and compute year fraction.
            
            double tte = snap.Calendar.GetYearFraction(snap.InitializationTime, Instrument.Expiry);

            // 3) forward price — prefer forward curve if available, else use implied future in snapshot
            double forward;
            
            forward = snap.ForwardCurve!.GetForwardPrice(tte);
            
            // 4) other market params from snapshot
            double spot = snap.IndexSpot;
            double rfr = snap.RiskFreeRate;
            double divYield = snap.DivYield;
            var volSurface = snap.VolSurface;

            // 5) Call the existing IGreeksCalculator methods (per-unit greeks).
            //    These calls follow the same calling convention used in your MarketData Option/Future greeks code.
            double npvPerUnit = greeksCalculator.NPV(
                productType, isCall, forward, strike,
                rfr, tte, volSurface);

            double deltaPerUnit = greeksCalculator.Delta(
                productType, isCall, forward, strike,
                rfr, tte, volSurface);

            double gammaPerUnit = greeksCalculator.Gamma(
                productType, isCall, forward, strike,
                rfr, tte, volSurface);

            // Vega in your codebase is "VegaByParam" returning per-parameter vegas.
            // Here we aggregate total vega as the sum of parameter vegas
            var volRisk = greeksCalculator.VolRiskByParam(
                productType, isCall, forward, strike,
                rfr, tte, volSurface
            );

            double vegaPerUnit   = GetVolRisk(volRisk, VolatilityParam.Vega);
            double vannaPerUnit  = GetVolRisk(volRisk, VolatilityParam.Vanna);
            double volgaPerUnit  = GetVolRisk(volRisk, VolatilityParam.Volga);
            double correlPerUnit = GetVolRisk(volRisk, VolatilityParam.Correl);

            double thetaPerUnit = greeksCalculator.Theta(
                productType, isCall, forward, strike,
                rfr, tte, volSurface);

            double rhoPerUnit = greeksCalculator.Rho(
                productType, isCall, forward, strike,
                rfr, tte, volSurface);

            // 6) scale by lots * lotSize
            double scale = Lots * Instrument.LotSize;

            return new TradeGreeks(
                NPV:    npvPerUnit    * scale,
                Delta:  deltaPerUnit  * scale,
                Gamma:  gammaPerUnit  * scale,
                Vega:   vegaPerUnit   * scale,
                Vanna:  vannaPerUnit  * scale,
                Volga:  volgaPerUnit  * scale,
                Correl: correlPerUnit * scale,
                Theta:  thetaPerUnit  * scale,
                Rho:    rhoPerUnit    * scale
            );
        }

        static double GetVolRisk(
            IEnumerable<(string ParamName, double Amount)> volRisk,
            VolatilityParam param)
        {
            foreach (var (name, value) in volRisk)
            {
                if (string.Equals(name, param.ToString(), StringComparison.OrdinalIgnoreCase))
                    return value;
            }
            return 0.0;
        }
    }

    public sealed record TradeGreeks(
        double NPV,
        double Delta,
        double Gamma,
        double Vega,
        double Vanna,
        double Volga,
        double Correl,
        double Theta,
        double Rho
    );

    public sealed record TradeNPV(
        double NPV        
    );


}