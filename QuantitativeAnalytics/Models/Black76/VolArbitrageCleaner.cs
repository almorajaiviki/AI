namespace QuantitativeAnalytics.Volatility
{
    // Simple VolPoint class assumed to exist in your project:
    // public class VolPoint { public double Moneyness { get; set; } public double IV { get; set; } }
    // The cleaner returns List<VolPoint>. If your VolPoint type name/namespace differs, adapt the using or the type name.

    public static class VolArbitrageCleaner
    {
        // Tolerances
        private const double PriceTol = 1e-12;
        private const double SlopeTol = 1e-12;

        /// <summary>
        /// Main entry. Takes raw vol points (moneyness, iv) for a single expiry and returns cleaned points.
        /// - forward, r, tte are used to map iv <-> price (Black-76).
        /// - liquidityByStrike and spreadByStrike are optional; keys are strike (double).
        ///   If provided, cleaner will prefer removing low-liquidity / high-spread nodes when resolving violations.
        /// </summary>
        public static List<VolPoint> Clean(
            IEnumerable<VolPoint> rawVolPoints,
            double forward,
            double r,
            double tte,
            IDictionary<double, double>? liquidityByStrike = null,
            IDictionary<double, double>? spreadByStrike = null)
        {
            if (rawVolPoints == null)
                throw new ArgumentNullException(nameof(rawVolPoints));

            // Convert rawVolPoints -> internal nodes with strike,moneyness,iv,price
            var pts = rawVolPoints
                .Select(p =>
                {
                    double m = p.Moneyness;
                    double strike = m * forward;
                    double iv = p.IV;
                    double price = PriceFromBlack76Put(forward, strike, r, iv, tte);
                    return new Node(strike, m, iv, price);
                })
                .OrderBy(x => x.Strike)
                .ToList();

            // If fewer than 3 strikes, nothing to enforce except monotonicity.
            if (pts.Count < 2)
                return rawVolPoints.OrderBy(p => p.Moneyness).ToList();

            // Iteratively enforce monotonicity and convexity
            bool changed = true;
            int safety = 0;
            while (changed && pts.Count >= 2 && safety++ < 1000)
            {
                changed = false;

                // 1) Monotonicity: P_i <= P_{i+1}
                for (int i = 0; i < pts.Count - 1; ++i)
                {
                    if (pts[i + 1].Price + PriceTol < pts[i].Price)
                    {
                        // Violation found: choose which of the two to remove
                        int removeIndex = ChooseIndexToRemoveForMonotonicity(pts, i, liquidityByStrike, spreadByStrike);
                        pts.RemoveAt(removeIndex);
                        changed = true;
                        break; // restart loop after modification
                    }
                }
                if (changed) continue;

                // 2) Convexity: discrete slope should be non-decreasing
                // slope_i = (P_i - P_{i-1})/(K_i - K_{i-1})
                if (pts.Count >= 3)
                {
                    bool convexViolationFound = false;
                    for (int i = 1; i < pts.Count - 1; ++i)
                    {
                        double k0 = pts[i - 1].Strike, k1 = pts[i].Strike, k2 = pts[i + 1].Strike;
                        double sLeft = (pts[i].Price - pts[i - 1].Price) / (k1 - k0);
                        double sRight = (pts[i + 1].Price - pts[i].Price) / (k2 - k1);
                        if (sRight + SlopeTol < sLeft)
                        {
                            // convexity violation at triplet (i-1,i,i+1)
                            int removeIndex = ChooseIndexToRemoveForConvexity(pts, i - 1, i, i + 1, liquidityByStrike, spreadByStrike);
                            pts.RemoveAt(removeIndex);
                            changed = true;
                            convexViolationFound = true;
                            break; // restart outer loop
                        }
                    }
                    if (convexViolationFound) continue;
                }
            }

            // Convert back to VolPoint list (moneyness, iv) using implied vol
            var cleaned = new List<VolPoint>(pts.Count);
            foreach (var n in pts)
            {
                // Recompute IV from price (Newton). If solver fails, fall back to original iv.
                double implied = ImpliedVolFromPriceBlack76Put(n.Price, forward, n.Strike, r, tte, n.Iv);
                cleaned.Add(new VolPoint { Moneyness = n.Moneyness, IV = implied });
            }

            // Ensure the cleaned list is sorted by moneyness
            cleaned = cleaned.OrderBy(p => p.Moneyness).ToList();
            return cleaned;
        }

        #region Internal helpers and node type

        private class Node
        {
            public double Strike;
            public double Moneyness;
            public double Iv;      // original iv used to compute price (fallback)
            public double Price;
            public Node(double strike, double m, double iv, double price)
            {
                Strike = strike; Moneyness = m; Iv = iv; Price = price;
            }
        }

        private static int ChooseIndexToRemoveForMonotonicity(List<Node> pts, int i, IDictionary<double, double>? liquidityByStrike, IDictionary<double, double>? spreadByStrike)
        {
            // Violation between i and i+1
            var left = pts[i];
            var right = pts[i + 1];

            // Prefer removing lower liquidity or higher spread
            int pick = PreferRemoveByLiquidityOrSpread(left.Strike, right.Strike, liquidityByStrike, spreadByStrike);
            if (pick >= 0) return pick;

            // fallback: remove the point whose price is more outlying relative to linear interpolation of neighbors (use 3-point local)
            double midStrike = 0.5 * (left.Strike + right.Strike);
            double interp = (left.Price + right.Price) / 2.0;
            double devLeft = Math.Abs(left.Price - interp);
            double devRight = Math.Abs(right.Price - interp);
            return devLeft > devRight ? i : i + 1;
        }

        private static int ChooseIndexToRemoveForConvexity(List<Node> pts, int i0, int i1, int i2, IDictionary<double, double>? liquidityByStrike, IDictionary<double, double>? spreadByStrike)
        {
            // Triplet indices: i0, i1, i2
            // Prefer removing the strike with least liquidity / worst spread
            int pick = PreferRemoveByLiquidityOrSpread(pts[i0].Strike, pts[i1].Strike, pts[i2].Strike, liquidityByStrike, spreadByStrike);
            if (pick >= 0) return pick;

            // fallback heuristics:
            // 1) prefer to remove middle if it is the biggest local residual vs line joining wings
            double k0 = pts[i0].Strike, k1 = pts[i1].Strike, k2 = pts[i2].Strike;
            double interpMid = pts[i0].Price + (pts[i2].Price - pts[i0].Price) * ((k1 - k0) / (k2 - k0));
            double devMid = Math.Abs(pts[i1].Price - interpMid);

            // residuals for wings as well
            double interpLeft = pts[i0].Price + (pts[i1].Price - pts[i0].Price) * (0.5); // dummy
            double devLeft = Math.Abs(pts[i0].Price - interpLeft);
            double interpRight = pts[i1].Price + (pts[i2].Price - pts[i1].Price) * (0.5);
            double devRight = Math.Abs(pts[i2].Price - interpRight);

            // If middle deviates most, remove middle; else remove the wing with larger deviation
            if (devMid >= devLeft && devMid >= devRight) return i1;
            return devLeft > devRight ? i0 : i2;
        }

        // Accepts 2 or 3 strikes. Returns index in the pts list to remove, or -1 if can't decide by liquidity/spread.
        private static int PreferRemoveByLiquidityOrSpread(params double[] strikesAndMaybeMore)
        {
            // This overload is not used directly. Use the below one with dictionaries.
            return -1;
        }

        private static int PreferRemoveByLiquidityOrSpread(double strikeA, double strikeB, IDictionary<double, double>? liquidityByStrike, IDictionary<double, double>? spreadByStrike)
        {
            if (liquidityByStrike != null || spreadByStrike != null)
            {
                double la = liquidityByStrike != null && liquidityByStrike.ContainsKey(strikeA) ? liquidityByStrike[strikeA] : double.NaN;
                double lb = liquidityByStrike != null && liquidityByStrike.ContainsKey(strikeB) ? liquidityByStrike[strikeB] : double.NaN;
                if (!double.IsNaN(la) && !double.IsNaN(lb))
                {
                    // remove the lower liquidity
                    return la < lb ? 0 : 1;
                }

                double sa = spreadByStrike != null && spreadByStrike.ContainsKey(strikeA) ? spreadByStrike[strikeA] : double.NaN;
                double sb = spreadByStrike != null && spreadByStrike.ContainsKey(strikeB) ? spreadByStrike[strikeB] : double.NaN;
                if (!double.IsNaN(sa) && !double.IsNaN(sb))
                {
                    // remove the higher spread (worse)
                    return sa > sb ? 0 : 1;
                }
            }
            return -1;
        }

        private static int PreferRemoveByLiquidityOrSpread(double strike0, double strike1, double strike2, IDictionary<double, double>? liquidityByStrike, IDictionary<double, double>? spreadByStrike)
        {
            // determine the lowest liquidity among the three (if available)
            if (liquidityByStrike != null)
            {
                bool a = liquidityByStrike.ContainsKey(strike0), b = liquidityByStrike.ContainsKey(strike1), c = liquidityByStrike.ContainsKey(strike2);
                if (a || b || c)
                {
                    double la = a ? liquidityByStrike[strike0] : double.PositiveInfinity;
                    double lb = b ? liquidityByStrike[strike1] : double.PositiveInfinity;
                    double lc = c ? liquidityByStrike[strike2] : double.PositiveInfinity;
                    if (la == double.PositiveInfinity && lb == double.PositiveInfinity && lc == double.PositiveInfinity) return -1;
                    if (la <= lb && la <= lc) return 0;
                    if (lb <= la && lb <= lc) return 1;
                    return 2;
                }
            }

            if (spreadByStrike != null)
            {
                bool a = spreadByStrike.ContainsKey(strike0), b = spreadByStrike.ContainsKey(strike1), c = spreadByStrike.ContainsKey(strike2);
                if (a || b || c)
                {
                    double sa = a ? spreadByStrike[strike0] : double.NegativeInfinity;
                    double sb = b ? spreadByStrike[strike1] : double.NegativeInfinity;
                    double sc = c ? spreadByStrike[strike2] : double.NegativeInfinity;
                    // remove the one with largest spread
                    if (sa >= sb && sa >= sc) return 0;
                    if (sb >= sa && sb >= sc) return 1;
                    return 2;
                }
            }

            return -1;
        }

        #endregion

        #region Black76 pricing & implied vol (local copies)

        // Price of a put under Black-76 (F is forward, K strike, r is continuously-compounded risk-free, T time)
        private static double PriceFromBlack76Put(double F, double K, double r, double iv, double T)
        {
            if (T <= 0.0) return Math.Max(K - F, 0.0);
            if (iv <= 0.0) return Math.Max(K - F, 0.0) * Math.Exp(-r * T);

            double sqrtT = Math.Sqrt(T);
            double d1 = (Math.Log(F / K) + 0.5 * iv * iv * T) / (iv * sqrtT);
            double d2 = d1 - iv * sqrtT;
            double nd1 = CdfNormal(d1);
            double nd2 = CdfNormal(d2);
            double disc = Math.Exp(-r * T);
            // Put price under Black-76: discount * (K * N(-d2) - F * N(-d1))
            return disc * (K * (1.0 - nd2) - F * (1.0 - nd1));
        }

        // Cumulative normal
        private static double CdfNormal(double x)
        {
            return 0.5 * (1.0 + Erf(x / Math.Sqrt(2.0)));
        }
        private static double Erf(double x)
        {
            // Use MathNet or native approximation; for portability we use the built-in System.Math.Erf if available (.NET Core 3+)
//#if NETCOREAPP || NET5_0_OR_GREATER
            //return System.Math.Erf(x);
//#else
            // A simple approximation if System.Math.Erf isn't available
            // Abramowitz and Stegun approximation (sufficient for implied vol seed)
            double t = 1.0 / (1.0 + 0.5 * Math.Abs(x));
            double tau = t * Math.Exp(-x*x - 1.26551223 +
                                     1.00002368*t +
                                     0.37409196*t*t +
                                     0.09678418*t*t*t -
                                     0.18628806*t*t*t*t +
                                     0.27886807*t*t*t*t*t -
                                     1.13520398*t*t*t*t*t*t +
                                     1.48851587*t*t*t*t*t*t*t -
                                     0.82215223*t*t*t*t*t*t*t*t +
                                     0.17087277*t*t*t*t*t*t*t*t*t);
            return x >= 0 ? 1.0 - tau : tau - 1.0;
//#endif
        }

        // Implied vol via Newton-Raphson, starting from initial guess (or originalIv if provided)
        private static double ImpliedVolFromPriceBlack76Put(double marketPrice, double forward, double strike, double r, double T, double originalIv)
        {
            if (marketPrice <= 0.0)
                return 0.0;

            double iv = originalIv > 0.0 ? originalIv : 0.2; // seed
            const int maxIter = 60;
            const double tol = 1e-10;

            for (int iter = 0; iter < maxIter; ++iter)
            {
                double price = PriceFromBlack76Put(forward, strike, r, iv, T);
                double diff = price - marketPrice;
                if (Math.Abs(diff) < tol) return iv;

                // compute vega (derivative of price wrt iv) under Black76
                double vega = VegaBlack76(forward, strike, r, iv, T);
                if (vega <= 1e-16) break;

                double step = diff / vega;
                iv -= step;

                // keep iv positive
                if (iv <= 1e-12) iv = 1e-12;
            }

            // fallback coarse bisection if Newton failed
            double lo = 1e-12, hi = 5.0;
            for (int i = 0; i < 80; ++i)
            {
                double mid = 0.5 * (lo + hi);
                double price = PriceFromBlack76Put(forward, strike, r, mid, T);
                if (price > marketPrice) hi = mid; else lo = mid;
            }
            return 0.5 * (lo + hi);
        }

        // Vega under Black-76 (derivative of option price w.r.t. sigma)
        private static double VegaBlack76(double F, double K, double r, double iv, double T)
        {
            if (T <= 0) return 0.0;
            double sqrtT = Math.Sqrt(T);
            double d1 = (Math.Log(F / K) + 0.5 * iv * iv * T) / (iv * sqrtT);
            double nd1pdf = PdfNormal(d1);
            double disc = Math.Exp(-r * T);
            // Vega under Black76 is discount * F * pdf(d1) * sqrt(T)?? 
            // But for put under Black76 with forward F and discount, vega formula for option on forward:
            // vega = disc * F * pdf(d1) * sqrt(T)  (for call); same for put (vega positive)
            // We compute via F * pdf(d1) * sqrt(T) * disc
            return disc * F * nd1pdf * sqrtT;
        }

        // Standard normal PDF
        private static double PdfNormal(double x)
        {
#if NETCOREAPP || NET5_0_OR_GREATER
            return Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
#else
            return Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
#endif
        }

        #endregion
    }

    // Lightweight VolPoint type — if your project already defines VolPoint, remove this and use that type.
    public class VolPoint
    {
        public double Moneyness { get; set; }
        public double IV { get; set; }
    }
}