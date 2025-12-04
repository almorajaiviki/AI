// =====================================================================
// SVI (Stochastic Volatility Inspired) Fitter – Jim Gatheral JWV version
// Drop-in replacement for your quadratic total-variance fit
// =====================================================================

// SVI.cs  –  Updated for MathNet.Numerics 5.x
// =====================================================================
// SVI Fitter – Fully compatible with MathNet.Numerics 5.0+ (2024+)
// Drop-in replacement for your quadratic fit
// =====================================================================

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace QuantitativeAnalytics
{
    public readonly struct SviParameters
    {
        public double a { get; }
        public double b { get; }
        public double ρ { get; }
        public double m { get; }
        public double σ { get; }

        public SviParameters(double a, double b, double ρ, double m, double σ)
            => (this.a, this.b, this.ρ, this.m, this.σ) = (a, b, ρ, m, σ);

        public double TotalVariance(double k)
        {
            double dm = k - m;
            return a + b * (ρ * dm + Math.Sqrt(dm * dm + σ * σ));
        }

        public double ImpliedVol(double k, double tte)
            => tte <= 0 ? 0.0 : Math.Sqrt(Math.Max(0.0, TotalVariance(k) / tte));
    }

    public static class SviFitter
    {
        public static SviParameters FitSvi(
            IReadOnlyList<(double k, double totalVariance)> points,
            double timeToExpiry)
        {
            if (points == null || points.Count < 5)
                throw new ArgumentException("At least 5 points required for SVI fit.");

            var (a0, b0, ρ0, m0, σ0) = RobustInitialGuess(points);
            var initial = Vector<double>.Build.Dense(new[] { a0, b0, ρ0, m0, σ0 });

            // FIXED: Use static ObjectiveFunction.Value to create IObjectiveFunction
            IObjectiveFunction objective = ObjectiveFunction.Value(parameters =>
            {
                double a = parameters[0], b = parameters[1], ρ = parameters[2],
                    m = parameters[3], σ = parameters[4];

                double sumSq = 0.0;
                foreach (var (k, wObs) in points)
                {
                    double dm = k - m;
                    double wModel = a + b * (ρ * dm + Math.Sqrt(dm * dm + σ * σ));
                    double diff = wModel - wObs;
                    sumSq += diff * diff;
                }
                return sumSq;
            });

            // FIXED: Constructor requires (convergenceTolerance, maximumIterations)
            var algorithm = new NelderMeadSimplex(1e-12, 5000);
            var result = algorithm.FindMinimum(objective, initial);

            // FIXED: Check ReasonForExit == ExitCondition.Converged
            if (result.ReasonForExit != ExitCondition.Converged)
                Console.WriteLine($"SVI fit did not fully converge. Reason: {result.ReasonForExit}");

            var p = result.MinimizingPoint;
            double aF = p[0], bF = p[1], ρF = p[2], mF = p[3], σF = p[4];

            // Enforce no-arbitrage (final safety net)
            if (bF * (1.0 + Math.Abs(ρF)) > 4.0)
                bF = 3.99 / (1.0 + Math.Abs(ρF));

            double vertexVar = aF + bF * σF * Math.Sqrt(1 - ρF * ρF);
            if (vertexVar < 0)
                aF = -bF * σF * Math.Sqrt(1 - ρF * ρF) + 1e-8;

            return new SviParameters(aF, bF, ρF, mF, σF);
        }

        private static (double a, double b, double ρ, double m, double σ) RobustInitialGuess(
            IReadOnlyList<(double k, double w)> points)
        {
            var ks = points.Select(p => p.k).ToArray();
            var ws = points.Select(p => p.w).ToArray();

            double wMin = ws.Min();
            int idxMin = Array.IndexOf(ws, wMin);
            double kMin = ks[idxMin];

            double a = wMin * 0.94;

            double leftSlope  = (ws[2] - ws[0]) / (ks[2] - ks[0] + 1e-10);
            double rightSlope = (ws[^1] - ws[^3]) / (ks[^1] - ks[^3] + 1e-10);

            double ρ = (rightSlope - leftSlope) / (rightSlope + leftSlope + 1e-10);
            ρ = Math.Clamp(ρ, -0.99, 0.99);

            double avgWing = (Math.Abs(leftSlope) + Math.Abs(rightSlope)) * 0.5;
            double b = Math.Max(0.01, avgWing * 0.8);

            double σ = Math.Max(0.1, Math.Abs(kMin - ks[ks.Length / 2]) * 1.8);
            double m = kMin;

            return (a, b, ρ, m, σ);
        }
    }
}