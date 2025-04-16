namespace QuantitativeAnalytics
{
    public static class Greeks
    {
        public static double Delta(bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, VolSurface volSurface, double bumpSize = 0.001)
        {
            double bumpedForward = forwardPrice * (1 + bumpSize);
            double npvUp = Black76.NPV(isCall, bumpedForward, strike, riskFreeRate, timeToExpiry, volSurface);

            double bumpedDownForward = forwardPrice * (1 - bumpSize);
            double npvDown = Black76.NPV(isCall, bumpedDownForward, strike, riskFreeRate, timeToExpiry, volSurface);

            return (npvUp - npvDown) / 2;  // Corrected: Divide by 2 for proper monetary impact
        }

        public static double Gamma(bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, VolSurface volSurface, double bumpSize = 0.001)
        {
            double bumpedForward = forwardPrice * (1 + bumpSize);
            double npvUp = Black76.NPV(isCall, bumpedForward, strike, riskFreeRate, timeToExpiry, volSurface);

            double npvAt = Black76.NPV(isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volSurface);

            double bumpedDownForward = forwardPrice * (1 - bumpSize);
            double npvDown = Black76.NPV(isCall, bumpedDownForward, strike, riskFreeRate, timeToExpiry, volSurface);

            return (npvUp - 2 * npvAt + npvDown);
        }

        public static double Vega(bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, VolSurface volSurface, double bumpSize = 0.01)
        {
            VolSurface bumpedUpSurface = volSurface.Bump(bumpSize);
            double npvUp = Black76.NPV(isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, bumpedUpSurface);

            VolSurface bumpedDownSurface = volSurface.Bump(-bumpSize);
            double npvDown = Black76.NPV(isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, bumpedDownSurface);

            return (npvUp - npvDown) / 2;  // Corrected: Divide by 2 for proper monetary impact
        }

        public static double Theta(bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, VolSurface volSurface)
        {
            double npvAt = Black76.NPV(isCall, forwardPrice, strike, riskFreeRate, timeToExpiry, volSurface);

            // Reduce time to expiry by 1 day (24 hours = 1 / 365.0 in year fraction)
            double bumpedTimeToExpiry = Math.Max(0, timeToExpiry - (1.0 / 365.0));
            double npvBumped = Black76.NPV(isCall, forwardPrice, strike, riskFreeRate, bumpedTimeToExpiry, volSurface);

            return (npvBumped - npvAt);
        }

        public static double Rho(bool isCall, double forwardPrice, double strike, double riskFreeRate, double timeToExpiry, VolSurface volSurface, double bumpSize = 0.0001)
        {
            double bumpedUpRate = riskFreeRate + bumpSize;
            double npvUp = Black76.NPV(isCall, forwardPrice, strike, bumpedUpRate, timeToExpiry, volSurface);

            double bumpedDownRate = riskFreeRate - bumpSize;
            double npvDown = Black76.NPV(isCall, forwardPrice, strike, bumpedDownRate, timeToExpiry, volSurface);

            return (npvUp - npvDown) / 2;  // Corrected: Divide by 2 for proper monetary impact
        }
    }
}