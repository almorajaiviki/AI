using CalendarLib;

namespace MarketData
{
    public enum InterpolationMethod
    {
        Linear,
        Spline
    }

    /// <summary>
    /// Immutable forward curve built from spot, dividend yield and futures quotes.
    /// Interpolates the implied continuous carry rate r(t), and evaluates forwards as:
    /// F(t) = spot * exp((r(t) - divYield) * t)
    /// </summary>
    public sealed class ForwardCurve
    {
        private const double MinPositiveTime = 1e-12;

        public double Spot { get; }
        public double DivYield { get; }
        public InterpolationMethod Interpolation { get; }

        private readonly double[] _knotTimes;      // monotonic increasing, in years
        private readonly double[] _impliedRates;   // r_i (annualized)
        private readonly NaturalCubicSpline? _rateSpline; // null when not used
        private readonly bool _useSpline;

        internal ForwardCurve(
            double spot,
            double divYield,
            double[] knotTimes,
            double[] impliedRates,
            InterpolationMethod interpolation)
        {
            if (spot <= 0) throw new ArgumentException("Spot must be positive.", nameof(spot));
            if (knotTimes == null) throw new ArgumentNullException(nameof(knotTimes));
            if (impliedRates == null) throw new ArgumentNullException(nameof(impliedRates));
            if (knotTimes.Length != impliedRates.Length) throw new ArgumentException("Knot times and rates length mismatch.");

            Spot = spot;
            DivYield = divYield;
            Interpolation = interpolation;

            _knotTimes = (double[])knotTimes.Clone();
            _impliedRates = (double[])impliedRates.Clone();

            _useSpline = interpolation == InterpolationMethod.Spline && _knotTimes.Length >= 3;
            if (_useSpline)
            {
                // NaturalCubicSpline constructor: (x[], y[]) and supports Evaluate()
                _rateSpline = new NaturalCubicSpline(_knotTimes, _impliedRates);
            }
            else
            {
                _rateSpline = null;
            }
        }

        /// <summary>
        /// Build ForwardCurve from raw (timeYears, futurePrice) pairs.
        /// Throws ArgumentException if no valid futures are present (policy 2 -> C).
        /// </summary>
        public static ForwardCurve BuildFromFutures(
            double spot,
            double divYield,
            IEnumerable<(double timeYears, double futurePrice)> futures,
            InterpolationMethod interpolation = InterpolationMethod.Spline)
        {
            if (futures == null) throw new ArgumentNullException(nameof(futures));
            var pts = futures
                .Where(f => f.futurePrice > 0 && f.timeYears > MinPositiveTime)
                .ToList();

            if (pts.Count == 0)
                throw new ArgumentException("No valid future points supplied to build ForwardCurve.");

            // sort by time
            pts.Sort((a, b) => a.timeYears.CompareTo(b.timeYears));

            var times = pts.Select(p => p.timeYears).ToArray();
            var rates = pts.Select(p => ComputeImpliedRate(spot, p.futurePrice, p.timeYears, divYield)).ToArray();

            return new ForwardCurve(spot, divYield, times, rates, interpolation);
        }

        /// <summary>
        /// Convenience builder using existing FutureDetail objects, MarketCalendar and a snapshot base time.
        /// Each FutureDetail.FutureSnapshot.Expiry is converted to a year-fraction via calendar.GetYearFraction(snapshotTime, expiry).
        /// </summary>
        public static ForwardCurve BuildFromFutureDetails(
            double spot,
            double divYield,
            IEnumerable<FutureDetailDTO> futureDetails,
            MarketCalendar calendar,
            DateTime snapshotTime,
            InterpolationMethod interpolation = InterpolationMethod.Spline)
        {
            if (futureDetails == null) throw new ArgumentNullException(nameof(futureDetails));
            if (calendar == null) throw new ArgumentNullException(nameof(calendar));

            var list = new List<(double timeYears, double futurePrice)>();
            foreach (var fd in futureDetails)
            {
                if (fd?.FutureSnapshot == null) continue;
                var snap = fd.FutureSnapshot;
                double t = calendar.GetYearFraction(snapshotTime, snap.Expiry);
                if (t <= MinPositiveTime) continue;
                if (snap.LTP <= 0) continue;
                list.Add((t, snap.LTP));
            }

            return BuildFromFutures(spot, divYield, list, interpolation);
        }

        /// <summary>
        /// Compute implied continuous rate r: F = S * exp((r - q) * t) => r = ln(F/S)/t + q
        /// </summary>
        private static double ComputeImpliedRate(double spot, double futurePrice, double tYears, double divYield)
        {
            // tYears guaranteed > MinPositiveTime by callers
            return Math.Log(futurePrice / spot) / tYears + divYield;
        }

        /// <summary>
        /// Number of knots (futures used to build this curve).
        /// </summary>
        public int KnotCount => _knotTimes.Length;

        /// <summary>
        /// Read-only view of knot times (years).
        /// </summary>
        public IReadOnlyList<double> KnotTimes => Array.AsReadOnly(_knotTimes);

        /// <summary>
        /// Read-only view of implied rates.
        /// </summary>
        public IReadOnlyList<double> ImpliedRates => Array.AsReadOnly(_impliedRates);

        /// <summary>
        /// Get interpolated implied rate r(t) (annualized).
        /// Extrapolation beyond last knot uses constant carry (last knot's rate).
        /// For t <= first knot time, returns first knot rate (no backward extrapolation).
        /// </summary>
        public double GetImpliedRate(double timeYears)
        {
            if (double.IsNaN(timeYears) || double.IsInfinity(timeYears))
                throw new ArgumentException("Invalid time supplied.", nameof(timeYears));

            // if time is before first knot, return first rate
            if (timeYears <= _knotTimes[0]) return _impliedRates[0];

            // if time beyond last knot, policy: constant carry -> return last rate
            if (timeYears >= _knotTimes[^1]) return _impliedRates[^1];

            // between knots
            if (_useSpline && _rateSpline != null)
            {
                // spline assumed to be defined on knot range; evaluate directly
                return _rateSpline.Evaluate(timeYears);
            }
            else
            {
                // linear interpolation between bracketing knots
                int idx = Array.BinarySearch(_knotTimes, timeYears);
                if (idx >= 0) return _impliedRates[idx]; // exact match
                int upper = ~idx;
                int lower = upper - 1;
                double tL = _knotTimes[lower];
                double tU = _knotTimes[upper];
                double rL = _impliedRates[lower];
                double rU = _impliedRates[upper];
                double w = (timeYears - tL) / (tU - tL);
                return rL + w * (rU - rL);
            }
        }

        /// <summary>
        /// Get forward price at time t (years): F(t) = S * exp((r(t) - q) * t)
        /// </summary>
        public double GetForwardPrice(double timeYears)
        {
            double r = GetImpliedRate(timeYears);
            return Spot * Math.Exp((r - DivYield) * timeYears);
        }

        /// <summary>
        /// Return a bumped copy of the ForwardCurve by shifting implied rates by 'shift' (absolute, in rate units).
        /// </summary>
        public ForwardCurve Bump(double shift)
        {
            var bumpedRates = (double[])_impliedRates.Clone();
            for (int i = 0; i < bumpedRates.Length; ++i) bumpedRates[i] += shift;

            return new ForwardCurve(Spot, DivYield, _knotTimes, bumpedRates, Interpolation);
        }

        /// <summary>
        /// Return a copy with replaced spot (useful for scenario testing without rebuilding knots).
        /// </summary>
        public ForwardCurve WithSpot(double newSpot)
        {
            return new ForwardCurve(newSpot, DivYield, _knotTimes, _impliedRates, Interpolation);
        }

        /// <summary>
        /// Return a copy with replaced div yield.
        /// </summary>
        public ForwardCurve WithDivYield(double newDivYield)
        {
            // recompute forward evaluation will use new div yield; implied rates kept as-is
            return new ForwardCurve(Spot, newDivYield, _knotTimes, _impliedRates, Interpolation);
        }
    }
}