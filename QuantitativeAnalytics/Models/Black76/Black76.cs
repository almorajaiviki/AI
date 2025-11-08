namespace QuantitativeAnalytics
{
    public static class Black76
    {
        /// <summary>
        /// Computes the price (NPV) of a European option using the Black-76 model.
        /// </summary>
        private static double NPVIV(bool isCall, double forwardPrice, double strike, double riskFreeRate, double iv, double timeToExpiry)
        {
            if (timeToExpiry <= 0)
                return isCall ? Math.Max(forwardPrice - strike, 0) : Math.Max(strike - forwardPrice, 0);

            double d1 = (Math.Log(forwardPrice / strike) + 0.5 * iv * iv * timeToExpiry) / (iv * Math.Sqrt(timeToExpiry));
            double d2 = d1 - iv * Math.Sqrt(timeToExpiry);
            double discountFactor = Math.Exp(-riskFreeRate * timeToExpiry);

            if (isCall)
                return discountFactor * (forwardPrice * Statistics.Phi(d1) - strike * Statistics.Phi(d2));
            else
                return discountFactor * (strike * Statistics.Phi(-d2) - forwardPrice * Statistics.Phi(-d1));
        }

        /// <summary>
        /// Computes the NPV of a product (Option or Future) using the Black-76 model.
        /// </summary>
        internal static double NPV(
            ProductType productType,
            bool isCall,
            double spot,
            double forwardPrice, bool bIsFutureBenchmark,
            double strike,
            double timeToExpiry,
            double riskFreeRate,
            double dividendYield,
            IParametricModelSurface? volSurface = null)
        {
            switch (productType)
            {
                case ProductType.Future:
                    if (bIsFutureBenchmark)
                        return forwardPrice; // For the benchmark future, NPV is its price
                    else    
                        // Futures fair value = Spot * exp[(r - q) * T]
                        return spot * Math.Exp((riskFreeRate - dividendYield) * timeToExpiry);

                case ProductType.Option:
                    if (volSurface == null)
                        throw new ArgumentNullException(nameof(volSurface), "Volatility surface required for option pricing.");

                    if (timeToExpiry <= 0)
                        return isCall ? Math.Max(forwardPrice - strike, 0) : Math.Max(strike - forwardPrice, 0);

                    double moneyness = strike / forwardPrice;
                    double iv = volSurface.GetVol(timeToExpiry, moneyness);

                    return NPVIV(isCall, forwardPrice, strike, riskFreeRate, iv, timeToExpiry);

                default:
                    throw new NotSupportedException($"Unsupported product type: {productType}");
            }
        }

        /// <summary>
        /// Computes the implied volatility using Newton-Raphson method.
        /// </summary>
        internal static double ComputeIV(
            bool isCall,
            double forwardPrice,
            double strike,
            double timeToExpiry,
            double riskFreeRate,
            double marketPrice,
            double initialGuess = 1.0,
            double ivBump = 0.01,
            int maxIterations = 100,
            double tolerance = 1e-6)
        {
            double iv = initialGuess;

            for (int i = 0; i < maxIterations; i++)
            {
                double npv = NPVIV(isCall, forwardPrice, strike, riskFreeRate, iv, timeToExpiry);
                double npvUp = NPVIV(isCall, forwardPrice, strike, riskFreeRate, iv + ivBump, timeToExpiry);
                double npvDown = NPVIV(isCall, forwardPrice, strike, riskFreeRate, iv - ivBump, timeToExpiry);

                double vega = (npvUp - npvDown) / (2 * ivBump);
                if (Math.Abs(npv - marketPrice) < tolerance)
                    return iv;

                iv -= (npv - marketPrice) / vega;
                if (iv <= 0) iv = ivBump; // Prevent negative volatility
            }

            return iv; // Return last computed IV if convergence not achieved
        }
    }
}
