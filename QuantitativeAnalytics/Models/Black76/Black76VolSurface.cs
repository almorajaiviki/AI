namespace QuantitativeAnalytics
{
    /// <summary>
    /// Black76 volatility skew surface implemented as a spline over moneyness -> IV.
    /// Implements both IVolSurface and IParametricModelSkew.
    /// </summary>
    public class Black76VolSkewSurface : IParametricModelSkew
    {
        internal const string AtmVolParam = "ATMVol";
        private readonly NaturalCubicSpline volSpline;
        private readonly double _timeToExpiry;
        public Black76VolSkewSurface(
            IEnumerable<(double strike, double Price, double OI)> callData,
            IEnumerable<(double strike, double Price, double OI)> putData,
            double forwardPrice,
            double riskFreeRate,
            double timeToExpiry,
            double OICutoff)
        {
            var tempSurface = Create(callData, putData, forwardPrice, riskFreeRate, timeToExpiry, OICutoff);
            this.volSpline = tempSurface.volSpline;
        }

        // ---------- private ctor for internal rebuilds ----------
        private Black76VolSkewSurface(NaturalCubicSpline spline, double timeToExpiry)
        {
            volSpline = spline ?? throw new ArgumentNullException(nameof(spline));
            _timeToExpiry = timeToExpiry;
        }

        private static Black76VolSkewSurface Create(
            IEnumerable<(double strike, double Price, double OI)> callData,
            IEnumerable<(double strike, double Price, double OI)> putData,
            double forwardPrice,
            double riskFreeRate,
            double timeToExpiry,
            double OICutoff)
        {
            // 1. Validation
            if (callData == null) throw new ArgumentNullException(nameof(callData));
            if (putData == null) throw new ArgumentNullException(nameof(putData));
            if (OICutoff < 0) throw new ArgumentException("OI Cutoff must be non-negative", nameof(OICutoff));
            if (forwardPrice <= 0) throw new ArgumentException("Forward price must be positive", nameof(forwardPrice));
            if (timeToExpiry <= 0) throw new ArgumentException("Time to expiry must be positive", nameof(timeToExpiry));

            // 2. Combine and Filter
            var callDataDict = callData.ToDictionary(c => c.strike, c => (c.Price, c.OI));
            var putDataDict = putData.ToDictionary(p => p.strike, p => (p.Price, p.OI));
            var allStrikes = new HashSet<double>(callDataDict.Keys.Concat(putDataDict.Keys));

            var volData = new List<(double m, double iv)>();

            foreach (var strike in allStrikes)
            {
                bool hasCall = callDataDict.TryGetValue(strike, out var call);
                bool hasPut = putDataDict.TryGetValue(strike, out var put);

                (double Price, double OI, bool isCall) selectedOption = (0,0,false);
                bool optionSelected = false;

                if (hasCall && hasPut)
                {
                    if (call.OI >= put.OI)
                    {
                        if (call.OI >= OICutoff)
                        {
                            selectedOption = (call.Price, call.OI, true);
                            optionSelected = true;
                        }
                    }
                    else
                    {
                        if (put.OI >= OICutoff)
                        {
                            selectedOption = (put.Price, put.OI, false);
                            optionSelected = true;
                        }
                    }
                }
                else if (hasCall)
                {
                    if (call.OI >= OICutoff)
                    {
                        selectedOption = (call.Price, call.OI, true);
                        optionSelected = true;
                    }
                }
                else if (hasPut)
                {
                    if (put.OI >= OICutoff)
                    {
                        selectedOption = (put.Price, put.OI, false);
                        optionSelected = true;
                    }
                }

                if (optionSelected)
                {
                    // 3. Calculate IV
                    double iv = Black76.ComputeIV(selectedOption.isCall, forwardPrice, strike, timeToExpiry, riskFreeRate, selectedOption.Price);
                    double m = strike / forwardPrice;
                    volData.Add((m, iv));
                }
            }

            // 4. Build Spline
            if (!volData.Any())
                throw new ArgumentException($"No data points meet the OI cutoff requirement of {OICutoff}.");

            var ordered = volData
                .GroupBy(t => t.m)
                .Select(g => (m: g.Key, iv: g.Average(x => x.iv)))
                .OrderBy(t => t.m)
                .ToArray();

            var spline = new NaturalCubicSpline(
                ordered.Select(x => x.m).ToArray(),
                ordered.Select(x => x.iv).ToArray());

            // 5. Return
            return new Black76VolSkewSurface(spline, timeToExpiry);
        }

        // ---------- IVolSurface ----------
        public double GetVol(double moneyness) => volSpline.Evaluate(moneyness);

        // ---------- IParametricModelSkew ----------
        public IReadOnlyDictionary<string, double> GetParameters()
        {
            // Minimal param view for Black: just ATM vol (m=1.0).
            return new Dictionary<string, double> { { AtmVolParam, volSpline.Evaluate(1.0) } };
        }

        public IParametricModelSkew Bump(double bumpAmount)
        {
            // Parallel absolute vol bump across all nodes.
            return new Black76VolSkewSurface(volSpline.Bump(bumpAmount), _timeToExpiry);
        }

        public IParametricModelSkew Bump(string parameterName, double bumpAmount)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                throw new ArgumentNullException(nameof(parameterName));

            // Special-case ATMVol (convenience)
            if (string.Equals(parameterName, AtmVolParam, StringComparison.OrdinalIgnoreCase))
            {
                return Bump(bumpAmount);
            }

            throw new ArgumentException(
                $"Unsupported parameter: {parameterName}. Only '{AtmVolParam}' is supported for Black76VolSkewSurface.",
                nameof(parameterName));
        }

        // ---------- IParametricModelSkew (continued) ----------
        public IParametricModelSkew Bump(IEnumerable<(string parameterName, double bumpAmount)> bumps)
        {
            if (bumps == null) throw new ArgumentNullException(nameof(bumps));

            IParametricModelSkew current = this;

            foreach (var (param, bump) in bumps)
            {
                if (string.Equals(param, AtmVolParam, StringComparison.OrdinalIgnoreCase))
                {
                    // ATMVol is a parallel shift
                    current = current.Bump(bump);
                }
                else
                {
                    current = current.Bump(param, bump);
                }
            }

            return current;
        }

        public IEnumerable<string> GetBumpParamNames()
        {
            return GetBumpingParameters();
        }

        private static IEnumerable<string> GetBumpingParameters()
        {
            var parameters = new List<string> { AtmVolParam };
            return parameters;
        }

        // ---------- DTO (unchanged shape except the class name) ----------
        public VolSurfaceDTO ToDTO()
        {

            return new VolSurfaceDTO
            {
                VolCurve = volSpline.RawPoints
                    .Select(p => new VolPoint { Moneyness = p.X, IV = p.Y })
                    .ToList()
            };
        }

        public IParametricModelSkew FromDTO(VolSurfaceDTO dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            if (dto.VolCurve == null || dto.VolCurve.Count == 0)
                throw new ArgumentException("VolSurfaceDTO.VolCurve is empty.", nameof(dto));

            var ordered = dto.VolCurve.OrderBy(p => p.Moneyness).ToArray();

            var spline = new NaturalCubicSpline(
                ordered.Select(p => p.Moneyness).ToArray(),
                ordered.Select(p => p.IV).ToArray());

            return new Black76VolSkewSurface(spline, dto.timeToExpiry);
        }
    }
    
    /// <summary>
    /// Black76 volatiltiy surface complete (i.e. multiple skews for multiple expiries)
    /// </summary>
    public class Black76VolSurface
    {
        // Store pairs of (timeToExpiry, skewSurface) because Black76VolSkewSurface does not expose expiry.
        private readonly List<(double timeToExpiry, Black76VolSkewSurface skew)> _skewEntries;

        /// <summary>
        /// Construct a multi-expiry vol surface. Each tuple in skewParamsList corresponds to one expiry
        /// and is used to construct a Black76VolSkewSurface for that expiry.
        /// Tuple format:
        /// (callData, putData, forwardPrice, riskFreeRate, timeToExpiry, OICutoff)
        /// </summary>
        public Black76VolSurface(
            IEnumerable<(
                IEnumerable<(double strike, double Price, double OI)> callData,
                IEnumerable<(double strike, double Price, double OI)> putData,
                double forwardPrice,
                double riskFreeRate,
                double timeToExpiry,
                double OICutoff)> skewParamsList)
        {
            if (skewParamsList == null)
                throw new ArgumentNullException(nameof(skewParamsList));

            _skewEntries = new List<(double, Black76VolSkewSurface)>();

            foreach (var p in skewParamsList)
            {
                // Basic validation similar to single-skew constructor expectations
                if (p.callData == null) throw new ArgumentException("callData cannot be null", nameof(p.callData));
                if (p.putData == null) throw new ArgumentException("putData cannot be null", nameof(p.putData));
                if (p.forwardPrice <= 0) throw new ArgumentException("forwardPrice must be positive", nameof(p.forwardPrice));
                if (p.timeToExpiry <= 0) throw new ArgumentException("timeToExpiry must be positive", nameof(p.timeToExpiry));
                if (p.OICutoff < 0) throw new ArgumentException("OICutoff must be non-negative", nameof(p.OICutoff));

                var skew = new Black76VolSkewSurface(
                    p.callData,
                    p.putData,
                    p.forwardPrice,
                    p.riskFreeRate,
                    p.timeToExpiry,
                    p.OICutoff);

                _skewEntries.Add((p.timeToExpiry, skew));
            }

            // Optional: keep the internal list ordered by timeToExpiry ascending for predictable behavior
            _skewEntries = _skewEntries.OrderBy(e => e.timeToExpiry).ToList();
        }

        /// <summary>
        /// Read-only list of constructed skew surfaces in ascending time-to-expiry order.
        /// </summary>
        public IReadOnlyList<Black76VolSkewSurface> SkewSurfaces =>
            _skewEntries.Select(e => e.skew).ToList().AsReadOnly();

        /// <summary>
        /// Corresponding list of expiries (time-to-expiry) that map one-to-one with SkewSurfaces.
        /// </summary>
        public IReadOnlyList<double> Expiries =>
            _skewEntries.Select(e => e.timeToExpiry).ToList().AsReadOnly();
    }


    // ---------- DTOs kept as-is ----------
    public class VolSurfaceDTO
    {
        public double timeToExpiry {get; set;}
        public List<VolPoint> VolCurve { get; set; } = new();
    }

    public class VolPoint
    {
        public double Moneyness { get; set; }
        public double IV { get; set; }
    }
}