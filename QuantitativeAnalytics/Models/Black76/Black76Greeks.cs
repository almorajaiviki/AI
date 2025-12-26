namespace QuantitativeAnalytics
{
    internal static class Black76Greeks
    {
        internal static double NPV(ProductType productType, bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, IParametricModelSurface? volSurface, double FeeAmount = 0)
        {
            return Black76.NPV(productType, isCall, forwardPrice, strike, timeToExpiry, riskFreeRate, volSurface, FeeAmount);
        }

        internal static double Delta(ProductType productType, bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, IParametricModelSurface? volSurface, double bumpSize = 0.001)
        {
            double bumpedForward = forwardPrice * (1 + bumpSize);
            double npvUp = NPV(productType, isCall, bumpedForward, strike, riskFreeRate, timeToExpiry, volSurface);

            double bumpedDownForward = forwardPrice * (1 - bumpSize);
            double npvDown = NPV(productType, isCall, bumpedDownForward, strike, riskFreeRate, timeToExpiry, volSurface);

            return (npvUp - npvDown) / 2.0;
        }

        internal static double Gamma(ProductType productType, bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, IParametricModelSurface? volSurface, double bumpSize = 0.001)
        {
            double bumpedForward = forwardPrice * (1 + bumpSize);
            double npvUp = NPV(productType, isCall, bumpedForward, strike, riskFreeRate, timeToExpiry, volSurface);

            double npvAt = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volSurface);

            double bumpedDownForward = forwardPrice * (1 - bumpSize);
            double npvDown = NPV(productType, isCall, bumpedDownForward, strike, riskFreeRate, timeToExpiry, volSurface);
            //Console.WriteLine($"Inside gamma calcs, npvUp: {npvUp}, npvAt: {npvAt}, npvDown: {npvDown}");

            double gamma = npvUp - 2*npvAt + npvDown;
            // // Clamp tiny numerical negatives
            // if (gamma < 0 && gamma > -1e-4)
            //     gamma = 0.0;

            return gamma;
        }

        internal static IEnumerable<(string ParamName, double Amount)> VolRiskByParam(
            ProductType productType,
            bool isCall,
            double forwardPrice,
            double strike,
            double riskFreeRate,
            double timeToExpiry,
            IParametricModelSurface volSurface,
            double forwardFracBump = 0.001, // 0.1%
            double volBump = 0.01            // 1 vol point
        )
        {
            if (productType != ProductType.Option)
                yield break;

            // ============================================================
            // 0️⃣ Level (Parallel Vega)
            // ============================================================
            {
                var volUp   = volSurface.Bump(+volBump);
                var volDown = volSurface.Bump(-volBump);

                double npvUp   = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volUp);
                double npvDown = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volDown);

                double vega = (npvUp - npvDown) / 2.0;

                yield return (VolatilityParam.Vega.ToString(), vega);
            }

            // ============================================================
            // 1️⃣ Vanna (Spot–Vol cross)
            // ============================================================
            {
                double fUp   = forwardPrice * (1.0 + forwardFracBump);
                double fDown = forwardPrice * (1.0 - forwardFracBump);

                var volUp   = volSurface.Bump(+volBump);
                var volDown = volSurface.Bump(-volBump);

                double npvPP = NPV(productType, isCall, fUp,   strike, riskFreeRate, timeToExpiry, volUp);
                double npvPM = NPV(productType, isCall, fUp,   strike, riskFreeRate, timeToExpiry, volDown);
                double npvMP = NPV(productType, isCall, fDown, strike, riskFreeRate, timeToExpiry, volUp);
                double npvMM = NPV(productType, isCall, fDown, strike, riskFreeRate, timeToExpiry, volDown);

                double vanna = (npvPP - npvPM - npvMP + npvMM) / 4.0;

                yield return (VolatilityParam.Vanna.ToString(), vanna);
            }

            // ============================================================
            // 2️⃣ Volga (Vol curvature)
            // ============================================================
            {
                var volUp   = volSurface.Bump(+volBump);
                var volDown = volSurface.Bump(-volBump);

                double npvUp   = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volUp);
                double npvBase = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volSurface);
                double npvDown = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volDown);

                double volga = npvUp - 2.0 * npvBase + npvDown;

                yield return (VolatilityParam.Volga.ToString(), volga);
            }

            // ============================================================
            // 3️⃣ Correlation (not supported in Black-76)
            // ============================================================
            {
                yield return (VolatilityParam.Correl.ToString(), 0.0);
            }
        }
        internal static double Theta(ProductType productType, bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, IParametricModelSurface? volSurface, double tteBump = -1.0 / 365.0)
        {
            double npvAt = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volSurface);
            double bumpedTimeToExpiry = Math.Max(0, timeToExpiry + tteBump);
            double npvBumped = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, bumpedTimeToExpiry, volSurface);
            return (npvBumped - npvAt);
        }

        internal static double Rho(ProductType productType, bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, IParametricModelSurface? volSurface, double bumpSize = 0.0001)
        {
            double bumpedUpRate = riskFreeRate + bumpSize;
            double npvUp = NPV(productType, isCall, forwardPrice, strike, bumpedUpRate, timeToExpiry, volSurface);

            double bumpedDownRate = riskFreeRate - bumpSize;
            double npvDown = NPV(productType, isCall, forwardPrice, strike, bumpedDownRate, timeToExpiry, volSurface);

            return (npvUp - npvDown) / 2.0;
        }
    
        // inside your Black76Greeks / Black76GreeksCalculator class
        internal static double Vanna(
            ProductType productType,
            bool isCall,         
            double forwardPrice,            
            double strike,
            double riskFreeRate,
            double timeToExpiry,
            IParametricModelSurface volSurface,
            double forwardFracBump = 0.001, // 0.1%
            double volBump = 0.01            // 1 vol point = 0.01
        )
        {
            // forward up/down (fractional)
            double fUp  = forwardPrice * (1.0 + forwardFracBump);
            double fDown= forwardPrice * (1.0 - forwardFracBump);

            // vol-surface bumped up/down (parallel)
            var volUpSurface   = volSurface.Bump(volBump);
            var volDownSurface = volSurface.Bump(-volBump);

            // 4 evaluations (NPV signature follows your other methods)
            double npvPP = NPV(productType, isCall, fUp, strike, riskFreeRate, timeToExpiry, volUpSurface);
            double npvPM = NPV(productType, isCall, fUp, strike, riskFreeRate, timeToExpiry, volDownSurface);
            double npvMP = NPV(productType, isCall, fDown, strike, riskFreeRate, timeToExpiry, volUpSurface);
            double npvMM = NPV(productType, isCall, fDown, strike, riskFreeRate, timeToExpiry, volDownSurface);

            // dollar vanna consistent with your other dollar-greeks
            double vannaDollar = (npvPP - npvPM - npvMP + npvMM) / 4.0;

            return vannaDollar;
        }
    
        internal static double Volga(
            ProductType productType,
            bool isCall,            
            double forwardPrice,            
            double strike,
            double riskFreeRate,            
            double timeToExpiry,
            IParametricModelSurface volSurface,
            double volBump = 0.01   // 1 vol point = 0.01
        )
        {
            // Build bumped surfaces
            var volUp   = volSurface.Bump(+volBump);
            var volDown = volSurface.Bump(-volBump);

            // Price with up/down/base vols
            double npvUp   = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volUp);

            double npvDown = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volDown);

            double npvBase = NPV(productType, isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volSurface);

            // Dollar-Volga (NPV curvature for ±1 vol point bump)
            double volgaDollar = npvUp - 2.0 * npvBase + npvDown;

            return volgaDollar;
        }

    }

    /// <summary>
    /// Adapter class to expose Black76Greeks via IGreeksCalculator. Implements singleton pattern.
    /// </summary>
    public sealed class Black76GreeksCalculator : IGreeksCalculator
    {
        public static Black76GreeksCalculator Instance { get; } = new Black76GreeksCalculator();
        private Black76GreeksCalculator() { }

        public double NPV(ProductType productType, bool isCall, double forward, double strike, double rate, double tte, IParametricModelSurface? surface)
            => Black76Greeks.NPV(productType, isCall, forward, strike, rate, tte, surface);

        public double Delta(ProductType productType, bool isCall, double forward, double strike, double rate, double tte, IParametricModelSurface? surface)
            => Black76Greeks.Delta(productType, isCall, forward, strike, rate, tte, surface);

        public double Gamma(ProductType productType, bool isCall, double forward, double strike, double rate, double tte, IParametricModelSurface? surface)
            => Black76Greeks.Gamma(productType, isCall, forward, strike, rate, tte, surface);

        public IEnumerable<(string ParamName, double Amount)> VolRiskByParam(ProductType productType, bool isCall, double forward, double strike, double rate, double tte, IParametricModelSurface surface, IEnumerable<(string parameterName, double bumpAmount)>? bumps = null)
            => Black76Greeks.VolRiskByParam(productType, isCall, forward, strike, rate, tte, surface);

        public double Theta(ProductType productType, bool isCall, double forward, double strike, double rate, double tte, IParametricModelSurface? surface, double tteBump = -1.0 / 365.0)
            => Black76Greeks.Theta(productType, isCall, forward, strike, rate, tte, surface, tteBump);

        public double Rho(ProductType productType, bool isCall, double forward, double strike, double rate, double tte, IParametricModelSurface? surface)
            => Black76Greeks.Rho(productType, isCall, forward, strike, rate, tte, surface);
    }
}

