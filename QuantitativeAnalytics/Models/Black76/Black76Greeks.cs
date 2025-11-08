namespace QuantitativeAnalytics
{
    internal static class Black76Greeks
    {
        internal static double NPV(ProductType productType, bool isCall, double Spot, double forwardPrice, bool bIsFutureBenchmark, double strike, double riskFreeRate, double dividendYield, double timeToExpiry, IParametricModelSkew? volSurface)
        {
            return Black76.NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, timeToExpiry, riskFreeRate, dividendYield, volSurface);
        }

        internal static double Delta(ProductType productType, bool isCall, double Spot, double forwardPrice, bool bIsFutureBenchmark, double strike, double riskFreeRate, double dividendYield, double timeToExpiry, IParametricModelSkew? volSurface, double bumpSize = 0.001)
        {
            double bumpedForward = forwardPrice * (1 + bumpSize);
            double npvUp = NPV(productType, isCall, Spot, bumpedForward, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, volSurface);

            double bumpedDownForward = forwardPrice * (1 - bumpSize);
            double npvDown = NPV(productType, isCall, Spot, bumpedDownForward, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, volSurface);

            return (npvUp - npvDown) / 2.0;
        }

        internal static double Gamma(ProductType productType, bool isCall, double Spot, double forwardPrice, bool bIsFutureBenchmark, double strike, double riskFreeRate, double dividendYield, double timeToExpiry, IParametricModelSkew? volSurface, double bumpSize = 0.001)
        {
            double bumpedForward = forwardPrice * (1 + bumpSize);
            double npvUp = NPV(productType, isCall, Spot, bumpedForward, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, volSurface);

            double npvAt = NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, volSurface);

            double bumpedDownForward = forwardPrice * (1 - bumpSize);
            double npvDown = NPV(productType, isCall, Spot, bumpedDownForward, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, volSurface);

            return (npvUp - 2 * npvAt + npvDown);
        }

        internal static IEnumerable<(string ParamName, double Amount)> VegaByParam(ProductType productType, bool isCall, double Spot, double forwardPrice, bool bIsFutureBenchmark, double strike, double riskFreeRate, double dividendYield, double timeToExpiry, IParametricModelSkew volSurface, IEnumerable<(string parameterName, double bumpAmount)>? bumps = null)
        {
            if (productType != ProductType.Option)
                yield break; // Vega not defined for futures

            bumps ??= volSurface.GetBumpParamNames().Select(p => (p, 0.01));

            var allowedParams = new HashSet<string>(volSurface.GetBumpParamNames(), StringComparer.OrdinalIgnoreCase);

            var invalidBumps = bumps.Where(b => !allowedParams.Contains(b.parameterName)).ToList();
            if (invalidBumps.Any())
            {
                var invalidParams = string.Join(", ", invalidBumps.Select(b => b.parameterName));
                var allowedParamsString = string.Join(", ", allowedParams);
                throw new ArgumentException($"Invalid parameter name(s): {invalidParams}. Allowed parameters are: {allowedParamsString}");
            }

            foreach (var bump in bumps)
            {
                var singleBump = new[] { bump };

                var bumpedUpSurface = volSurface.Bump(singleBump);
                var bumpedDownSurface = volSurface.Bump(singleBump.Select(b => (b.parameterName, -b.bumpAmount)));

                double npvUp = NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, bumpedUpSurface);
                double npvDown = NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, bumpedDownSurface);

                double vega = (npvUp - npvDown) / 2.0;
                double m = forwardPrice / strike;

                yield return (m.ToString(), vega);
            }
        }

        internal static double Theta(ProductType productType, bool isCall, double Spot, double forwardPrice, bool bIsFutureBenchmark, double strike, double riskFreeRate, double dividendYield, double timeToExpiry, IParametricModelSkew? volSurface)
        {
            double npvAt = NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, timeToExpiry, volSurface);
            double bumpedTimeToExpiry = Math.Max(0, timeToExpiry - (1.0 / 365.0));
            double npvBumped = NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, riskFreeRate, dividendYield, bumpedTimeToExpiry, volSurface);
            return (npvBumped - npvAt);
        }

        internal static double Rho(ProductType productType, bool isCall, double Spot, double forwardPrice,bool bIsFutureBenchmark, double strike, double riskFreeRate, double dividendYield, double timeToExpiry, IParametricModelSkew? volSurface, double bumpSize = 0.0001)
        {
            double bumpedUpRate = riskFreeRate + bumpSize;
            double npvUp = NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, bumpedUpRate, dividendYield, timeToExpiry, volSurface);

            double bumpedDownRate = riskFreeRate - bumpSize;
            double npvDown = NPV(productType, isCall, Spot, forwardPrice, bIsFutureBenchmark, strike, bumpedDownRate, dividendYield, timeToExpiry, volSurface);

            return (npvUp - npvDown) / 2.0;
        }
    }

    /// <summary>
    /// Adapter class to expose Black76Greeks via IGreeksCalculator. Implements singleton pattern.
    /// </summary>
    public sealed class Black76GreeksCalculator : IGreeksCalculator
    {
        public static Black76GreeksCalculator Instance { get; } = new Black76GreeksCalculator();
        private Black76GreeksCalculator() { }

        public double NPV(ProductType productType, bool isCall, double Spot, double forward, bool bIsFutureBenchmark, double strike, double rate, double dividendYield, double tte, IParametricModelSkew? surface)
            => Black76Greeks.NPV(productType, isCall, Spot, forward, bIsFutureBenchmark, strike, rate, dividendYield, tte, surface);

        public double Delta(ProductType productType, bool isCall, double Spot, double forward, bool bIsFutureBenchmark, double strike, double rate, double dividendYield, double tte, IParametricModelSkew? surface)
            => Black76Greeks.Delta(productType, isCall, Spot, forward, bIsFutureBenchmark, strike, rate, dividendYield, tte, surface);

        public double Gamma(ProductType productType, bool isCall, double Spot, double forward, bool bIsFutureBenchmark, double strike, double rate, double dividendYield, double tte, IParametricModelSkew? surface)
            => Black76Greeks.Gamma(productType, isCall, Spot, forward, bIsFutureBenchmark, strike, rate, dividendYield, tte, surface);

        public IEnumerable<(string ParamName, double Amount)> VegaByParam(ProductType productType, bool isCall, double Spot, double forward, bool bIsFutureBenchmark, double strike, double rate, double dividendYield, double tte, IParametricModelSkew surface, IEnumerable<(string parameterName, double bumpAmount)>? bumps = null)
            => Black76Greeks.VegaByParam(productType, isCall, Spot, forward, bIsFutureBenchmark, strike, rate, dividendYield, tte, surface, bumps);

        public double Theta(ProductType productType, bool isCall, double Spot, double forward, bool bIsFutureBenchmark, double strike, double rate, double dividendYield, double tte, IParametricModelSkew? surface)
            => Black76Greeks.Theta(productType, isCall, Spot, forward, bIsFutureBenchmark, strike, rate, dividendYield, tte, surface);

        public double Rho(ProductType productType, bool isCall, double Spot, double forward, bool bIsFutureBenchmark, double strike, double rate, double dividendYield, double tte, IParametricModelSkew? surface)
            => Black76Greeks.Rho(productType, isCall, Spot, forward, bIsFutureBenchmark, strike, rate, dividendYield, tte, surface);
    }
}
