namespace QuantitativeAnalytics
{
    public static class Black76
    {
        /// <summary>
        /// Computes the price (NPV) of a European option using the Black-76 model.
        /// </summary>
        internal static double NPVIV(bool isCall, double forwardPrice, double strike, double riskFreeRate, double iv, double timeToExpiry)
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
            double forwardPrice,
            double strike,
            double timeToExpiry,
            double riskFreeRate,
            double dividendYield,
            IParametricModelSurface? volSurface = null)
        {
            switch (productType)
            {
                case ProductType.Future:
                    // Futures fair value = Spot * exp[(r - q) * T]
                    return forwardPrice;

                case ProductType.Option:
                    if (volSurface == null)
                        throw new ArgumentNullException(nameof(volSurface), "Volatility surface required for option pricing.");

                    if (timeToExpiry <= 0)
                        return isCall ? Math.Max(forwardPrice - strike, 0) : Math.Max(strike - forwardPrice, 0);

                    // convert to log-moneyness (ln(K/F))
                    double logMoneyness = Math.Log(strike / forwardPrice);
                    double iv = volSurface.GetVol(timeToExpiry, logMoneyness);

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
            // Initial guess must be positive and finite
            double iv = initialGuess;
            if (!double.IsFinite(iv) || iv <= 0.0)
                iv = 0.2; // mild default

            // Local reusable variables
            for (int iter = 0; iter < maxIterations; iter++)
            {
                // Reject bad IV immediately
                if (!double.IsFinite(iv) || iv <= 0.0 || iv > 5.0)
                    return double.NaN;

                double npv = NPVIV(isCall, forwardPrice, strike, riskFreeRate, iv, timeToExpiry);
                if (!double.IsFinite(npv))
                    return double.NaN;

                // Check convergence
                double diff = npv - marketPrice;
                if (Math.Abs(diff) < tolerance)
                    return iv;

                // Compute vega via symmetric finite differences
                double ivUp = iv + ivBump;
                double ivDown = iv - ivBump;

                if (ivDown <= 0.0) ivDown = ivBump; // prevent invalid negative vol

                double npvUp = NPVIV(isCall, forwardPrice, strike, riskFreeRate, ivUp, timeToExpiry);
                double npvDown = NPVIV(isCall, forwardPrice, strike, riskFreeRate, ivDown, timeToExpiry);

                if (!double.IsFinite(npvUp) || !double.IsFinite(npvDown))
                    return double.NaN;

                double vega = (npvUp - npvDown) / (2 * ivBump);

                // Vega must be non-degenerate
                if (!double.IsFinite(vega) || Math.Abs(vega) < 1e-12)
                    return double.NaN;

                // Newton update
                double newIv = iv - diff / vega;

                // Domain + sanity checks
                if (!double.IsFinite(newIv) || newIv <= 0.0 || newIv > 5.0)
                    return double.NaN;

                iv = newIv;
            }

            // If we are here, Newton did not converge
            return double.NaN;
}
    }
}
