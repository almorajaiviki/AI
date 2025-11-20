// File: QuantitativeAnalytics/Volatility/VolArbitrageProjector.cs
// Add a using for your OSQP wrapper namespace if available, e.g. using OsqpNet;

namespace QuantitativeAnalytics.Volatility
{
    /// <summary>
    /// Project market prices to the nearest vector satisfying static no-arbitrage constraints:
    /// monotonicity, convexity (discrete slopes non-decreasing), and intrinsic/upper bounds.
    /// Uses weighted least squares QP: 0.5*(x - p)^T W (x - p) minimized s.t. A x >= b.
    /// </summary>
    public static class VolArbitrageProjector
    {
        /// <summary>
        /// Solve for adjusted prices.
        /// If osqpAvailable==true, tries to use OSQP via the provided wrapper.
        /// If not available (or it fails), falls back to a robust local projector (iterative removal).
        /// </summary>
        public static double[] ProjectPricesToNoArb(
            double[] strikes,
            double[] marketPrices,
            double forward,
            double r,
            double timeToExpiry,
            double[] weights = null,
            bool osqpAvailable = true,
            double eps = 1e-12)
        {
            if (strikes == null) throw new ArgumentNullException(nameof(strikes));
            if (marketPrices == null) throw new ArgumentNullException(nameof(marketPrices));
            if (strikes.Length != marketPrices.Length) throw new ArgumentException("strikes/prices length mismatch");
            int n = strikes.Length;
            if (n < 2) return (double[])marketPrices.Clone();

            // Default weights (uniform)
            if (weights == null || weights.Length != n)
            {
                weights = Enumerable.Repeat(1.0, n).ToArray();
            }

            // Bounds
            double discount = Math.Exp(-r * timeToExpiry);
            var lower = new double[n];
            var upper = new double[n];
            for (int i = 0; i < n; ++i)
            {
                lower[i] = Math.Max(0.0, strikes[i] - forward * discount);
                upper[i] = strikes[i] * discount;
            }

            // Build Q and q for the QP: 0.5 x^T Q x + q^T x
            // Q is diagonal with weights w_i
            // q_i = - w_i * p_i^{mkt}
            double[] q = new double[n];
            for (int i = 0; i < n; ++i) q[i] = -weights[i] * marketPrices[i];

            // Build inequality constraint matrices A, l, u
            // We'll form them as a list of sparse rows (iIndices, jIndices, values)
            // Constraints:
            // (1) monotonicity: x_{i+1} - x_i >= 0  (i=0..n-2)
            // (2) convexity: (1/h_{i+1}) x_{i+1} - (1/h_{i+1} + 1/h_i) x_i + (1/h_i) x_{i-1} >= 0  (i=1..n-2)
            // (3) bounds: lower_i <= x_i <= upper_i  (we'll express as two inequalities per var)

            List<int> Ai = new List<int>();
            List<int> Aj = new List<int>();
            List<double> Av = new List<double>();
            List<double> L = new List<double>();
            List<double> U = new List<double>();

            // Monotonicity constraints
            for (int i = 0; i < n - 1; ++i)
            {
                // row: -x_i + x_{i+1} >= 0
                Ai.Add(i); Aj.Add(i); Av.Add(-1.0);
                Ai.Add(i); Aj.Add(i + 1); Av.Add(+1.0);
                L.Add(0.0); U.Add(double.PositiveInfinity);
            }

            // Convexity constraints
            for (int i = 1; i < n - 1; ++i)
            {
                double h0 = strikes[i] - strikes[i - 1];
                double h1 = strikes[i + 1] - strikes[i];
                if (h0 <= 0 || h1 <= 0)
                {
                    // degenerate strike spacing; skip
                    L.Add(-1e308); U.Add(1e308);
                    Ai.Add(i + n); Aj.Add(i); Av.Add(0.0);
                    continue;
                }
                double a = 1.0 / h1; // coeff for x_{i+1}
                double b = - (1.0 / h1 + 1.0 / h0); // coeff for x_i
                double c = 1.0 / h0; // coeff for x_{i-1}
                int rowIndex = (Ai.Count==0 ? 0 : Ai.Max()+1); // not needed; we will just append
                Ai.Add(Ai.Count); Aj.Add(i - 1); Av.Add(c);
                Ai.Add(Ai.Count - 0); Aj.Add(i); Av.Add(b); // corrected below: simpler build method below
                // --- simpler build approach below will be used to avoid index confusion
            }

            // The above partial building is messy mid-construction. We'll use a simpler deterministic builder below:
            Ai.Clear(); Aj.Clear(); Av.Clear(); L.Clear(); U.Clear();

            // Monotonicity (rows 0 .. n-2)
            for (int row = 0; row < n - 1; ++row)
            {
                Ai.Add(row); Aj.Add(row); Av.Add(-1.0);
                Ai.Add(row); Aj.Add(row + 1); Av.Add(+1.0);
                L.Add(0.0); U.Add(double.PositiveInfinity);
            }

            int rowIdx = n - 1;
            // Convexity constraints rows
            for (int i = 1; i < n - 1; ++i)
            {
                double h0 = strikes[i] - strikes[i - 1];
                double h1 = strikes[i + 1] - strikes[i];
                // c * x_{i-1} + b * x_i + a * x_{i+1} >= 0
                double c = 1.0 / h0;
                double a = 1.0 / h1;
                double b = - (a + c);
                Ai.Add(rowIdx); Aj.Add(i - 1); Av.Add(c);
                Ai.Add(rowIdx); Aj.Add(i);     Av.Add(b);
                Ai.Add(rowIdx); Aj.Add(i + 1); Av.Add(a);
                L.Add(0.0); U.Add(double.PositiveInfinity);
                rowIdx++;
            }

            // Bounds: lower_i <= x_i <= upper_i
            for (int i = 0; i < n; ++i)
            {
                Ai.Add(rowIdx); Aj.Add(i); Av.Add(1.0);
                L.Add(lower[i]); U.Add(upper[i]);
                rowIdx++;
            }

            int numRows = rowIdx;

            // Convert lists to arrays (sparse CSC is usually required by solvers; here we keep COO-like representation)
            int nz = Ai.Count;
            int[] ia = Ai.ToArray();
            int[] ja = Aj.ToArray();
            double[] av = Av.ToArray();
            double[] lArr = L.ToArray();
            double[] uArr = U.ToArray();

            // QP data: Q is diagonal (weights), q is negative weighted market prices
            // Many solvers accept Q in a compressed sparse column format. We'll assume OSQP wrapper can accept:
            // - Q as triplets or diagonal array
            // - A as (ia, ja, av) COO
            // If you use a different solver you may need to convert to its format.

            // Attempt to call OSQP via wrapper (pseudo-code, adapt to your OSQP .NET wrapper)
            if (osqpAvailable)
            {
                try
                {
                    // Prepare OSQP data structures:
                    // P (Q) diag entries:
                    double[] Pdiag = weights.ToArray(); // diagonal Q
                    double[] qVec = q;

                    // A matrix in COOrdinate form (row indices ia, col indices ja, values av)
                    // Build solver data structure and call solve.
                    // Example pseudo-code using a hypothetical OSQP .NET wrapper:
                    //
                    // var data = new OsqpData(Pdiag, qVec, ia, ja, av, lArr, uArr, n, numRows, nz);
                    // var settings = new OsqpSettings { MaxIter = 10000, Verbose = false };
                    // using (var solver = new OsqpSolver(data, settings))
                    // {
                    //     var result = solver.Solve();
                    //     if (result.Status != OsqpStatus.Solved) throw new Exception("OSQP failed");
                    //     return result.Solution; // size n
                    // }
                    //
                    // Replace above with the actual OSQP.NET call pattern you have in your environment.

                    // Since the exact wrapper call may differ, if you provide your OSQP wrapper I can adapt precisely.
                    throw new NotImplementedException("OSQP call placeholder — replace with your OSQP.NET wrapper call.");
                }
                catch (Exception ex)
                {
                    // fall through to fallback
                    Console.WriteLine("[VolArbitrageProjector] OSQP call failed or not implemented: " + ex.Message);
                }
            }

            // Fallback: simple iterative convexity/monotonicity projection (no external solver).
            // This is a robust fallback that enforces monotonicity and convexity by small local adjustments.
            return LocalConvexityProjection(strikes, marketPrices, lower, upper, weights);
        }

        /// <summary>
        /// A robust fallback when a QP solver is not available:
        /// Iteratively enforce monotonicity and convexity by local adjustments (small changes).
        /// This keeps runtime small and avoids external dependencies.
        /// </summary>
        private static double[] LocalConvexityProjection(double[] strikes, double[] pMkt, double[] lower, double[] upper, double[] weights)
        {
            int n = strikes.Length;
            var x = (double[])pMkt.Clone();

            // Iteratively remove/adjust violations
            bool changed;
            int safety = 0;
            do
            {
                changed = false;
                // Monotonicity fix: ensure x[i] <= x[i+1]
                for (int i = 0; i < n - 1; ++i)
                {
                    if (x[i] > x[i + 1] + 1e-12)
                    {
                        // average weighted by weights
                        double w1 = weights[i], w2 = weights[i + 1];
                        double newVal = (w1 * x[i] + w2 * x[i + 1]) / (w1 + w2);
                        newVal = Math.Max(newVal, Math.Max(lower[i], lower[i + 1]));
                        newVal = Math.Min(newVal, Math.Min(upper[i], upper[i + 1]));
                        x[i] = newVal;
                        x[i + 1] = newVal;
                        changed = true;
                    }
                }
                if (changed) continue;

                // Convexity fix: ensure slopes are non-decreasing
                // slope_i = (x[i] - x[i-1]) / h_i
                double[] slopes = new double[n - 1];
                double[] h = new double[n - 1];
                for (int i = 1; i < n; ++i)
                {
                    h[i - 1] = strikes[i] - strikes[i - 1];
                    slopes[i - 1] = (x[i] - x[i - 1]) / Math.Max(1e-12, h[i - 1]);
                }
                // enforce monotonicity on slopes via PAV (pooled adjacent violators)
                double[] slopesAdj = PooledAdjacentViolators(slopes, Enumerable.Repeat(1.0, slopes.Length).ToArray());
                // reconstruct x from x0 and slopesAdj
                for (int i = 1; i < n; ++i)
                    x[i] = x[0] + slopesAdj.Take(i).Zip(h, (s,hh) => s*hh).Sum();

                // apply bounds
                for (int i = 0; i < n; ++i)
                {
                    if (x[i] < lower[i]) x[i] = lower[i];
                    if (x[i] > upper[i]) x[i] = upper[i];
                }

            } while (changed && (++safety) < 1000);

            return x;
        }

        // Pooled Adjacent Violators for isotonic regression (non-decreasing)
        // y: input array length m, wts: positive weights
        // returns fitted non-decreasing array z minimizing sum w*(z - y)^2
        private static double[] PooledAdjacentViolators(double[] y, double[] wts)
        {
            int m = y.Length;
            var z = new double[m];
            var weights = new double[m];
            var sum = new double[m];
            var level = new double[m];
            int[] index = new int[m];

            int blocks = 0;
            for (int i = 0; i < m; ++i)
            {
                level[blocks] = y[i];
                weights[blocks] = wts[i];
                index[blocks] = i;
                blocks++;
                // merge blocks while monotonicity violated
                while (blocks >= 2 && level[blocks - 2] > level[blocks - 1])
                {
                    // merge last two blocks
                    double w1 = weights[blocks - 2], w2 = weights[blocks - 1];
                    double l1 = level[blocks - 2], l2 = level[blocks - 1];
                    double mergedW = w1 + w2;
                    double mergedLevel = (w1 * l1 + w2 * l2) / mergedW;
                    weights[blocks - 2] = mergedW;
                    level[blocks - 2] = mergedLevel;
                    blocks--;
                }
            }

            // expand levels back to fitted z
            int pos = 0;
            for (int b = 0; b < blocks; ++b)
            {
                // find how many original indices in this block
                // we do not track block lengths explicitly; easiest: re-run with monotone merging but track ranges
                // For simplicity, we recompute using standard PAV with stack of (level, weight, length)
            }

            // Simpler (and robust) implementation: use standard stack-based PAV with lengths
            // We'll implement a clear PAV now:
            var lvl = new List<double>();
            var wt  = new List<double>();
            var len = new List<int>();
            for (int i = 0; i < m; ++i)
            {
                lvl.Add(y[i]);
                wt.Add(wts[i]);
                len.Add(1);
                // Merge while last two levels violate monotonicity
                while (lvl.Count >= 2 && lvl[lvl.Count - 2] > lvl[lvl.Count - 1])
                {
                    int last = lvl.Count - 1;
                    double mergedW = wt[last] + wt[last - 1];
                    double mergedLvl = (lvl[last] * wt[last] + lvl[last - 1] * wt[last - 1]) / mergedW;
                    wt[last - 1] = mergedW;
                    lvl[last - 1] = mergedLvl;
                    len[last - 1] += len[last];
                    // pop last
                    wt.RemoveAt(last);
                    lvl.RemoveAt(last);
                    len.RemoveAt(last);
                }
            }

            var output = new double[m];
            int idx = 0;
            for (int b = 0; b < lvl.Count; ++b)
            {
                for (int j = 0; j < len[b]; ++j)
                {
                    output[idx++] = lvl[b];
                }
            }
            return output;
        }
    }
}