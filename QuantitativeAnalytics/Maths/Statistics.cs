namespace QuantitativeAnalytics
{
    public static class Statistics
    {
        // Standard normal PDF
        public static double NormalPdf(double x)
        {
            return Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
        }

        /// <summary>
        /// Computes the cumulative distribution function (CDF) of the standard normal distribution.
        /// </summary>
        /// <param name="x">The input value.</param>
        /// <returns>The probability that a standard normal variable is ≤ x.</returns>
        public static double Phi(double x)
        {
            // Constants for the approximation
            double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741;
            double a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;

            // Save the sign of x
            int sign = x < 0 ? -1 : 1;
            x = Math.Abs(x) / Math.Sqrt(2.0);

            // Approximation of the error function (erf)
            double t = 1.0 / (1.0 + p * x);
            double erfApprox = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            // Compute the cumulative probability
            return 0.5 * (1.0 + sign * erfApprox);
        }
    }
}
