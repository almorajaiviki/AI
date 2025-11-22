// File: QuantitativeAnalytics/Interpolation/MonotoneCubicSpline.cs
namespace QuantitativeAnalytics.Interpolation
{
    // Monotone cubic Hermite spline (Fritsch-Carlson) over strictly increasing x[]
    public class MonotoneCubicSpline
    {
        private readonly double[] xs, ys, m; // slopes m at nodes
        private readonly int n;

        public MonotoneCubicSpline(double[] x, double[] y)
        {
            if (x == null || y == null) throw new ArgumentNullException();
            if (x.Length != y.Length) throw new ArgumentException("x and y length mismatch");
            n = x.Length;
            if (n < 2) throw new ArgumentException("Need at least two points");
            xs = (double[])x.Clone();
            ys = (double[])y.Clone();

            // compute secant slopes
            var delta = new double[n - 1];
            var h = new double[n - 1];
            for (int i = 0; i < n - 1; ++i)
            {
                h[i] = xs[i + 1] - xs[i];
                if (h[i] <= 0) throw new ArgumentException("x must be strictly increasing");
                delta[i] = (ys[i + 1] - ys[i]) / h[i];
            }

            m = new double[n];
            // endpoints: use delta[0] and delta[n-2]
            m[0] = delta[0];
            m[n - 1] = delta[n - 2];
            // interior slopes: initial average
            for (int i = 1; i < n - 1; ++i)
                m[i] = (delta[i - 1] + delta[i]) * 0.5;

            // adjust to preserve monotonicity (Fritsch-Carlson)
            for (int i = 0; i < n - 1; ++i)
            {
                if (Math.Abs(delta[i]) < 1e-16)
                {
                    m[i] = 0.0;
                    m[i + 1] = 0.0;
                    continue;
                }
                double a = m[i] / delta[i];
                double b = m[i + 1] / delta[i];
                double norm = a * a + b * b;
                if (norm > 9.0) // 3 is common factor: ensure monotonicity
                {
                    double tau = 3.0 / Math.Sqrt(norm);
                    m[i] = tau * a * delta[i];
                    m[i + 1] = tau * b * delta[i];
                }
            }
        }

        public double Evaluate(double x)
        {
            // extrapolate flat by slope at ends
            if (x <= xs[0]) return ys[0] + m[0] * (x - xs[0]);
            if (x >= xs[n - 1]) return ys[n - 1] + m[n - 1] * (x - xs[n - 1]);

            int i = Array.BinarySearch(xs, x);
            if (i >= 0) return ys[i];
            i = ~i - 1;
            double h = xs[i + 1] - xs[i];
            double t = (x - xs[i]) / h;

            // Hermite basis
            double h00 = (1 + 2 * t) * (1 - t) * (1 - t);
            double h10 = t * (1 - t) * (1 - t);
            double h01 = t * t * (3 - 2 * t);
            double h11 = t * t * (t - 1);

            return h00 * ys[i] + h10 * h * m[i] + h01 * ys[i + 1] + h11 * h * m[i + 1];
        }

        public MonotoneCubicSpline Bump(double bumpAmount)
        {
            double[] bumpedY = ys.Select(val => val + bumpAmount).ToArray();
            return new MonotoneCubicSpline(xs, bumpedY);
        }
        // Expose raw nodes if needed
        public (double[] X, double[] Y) RawPoints => ((double[])xs.Clone(), (double[])ys.Clone());
    }
}