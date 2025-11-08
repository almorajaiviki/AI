namespace QuantitativeAnalytics
{
    /// <summary>
    /// Black76 volatility skew surface implemented as a spline over moneyness -> IV.
    /// Implements both IVolSurface and IParametricModelSkew.
    /// </summary>
    public class Black76VolSkew : IParametricModelSkew
    {
        internal const string AtmVolParam = "ATMVol";
        private readonly NaturalCubicSpline volSpline;
        private readonly double _timeToExpiry;

        public double TimeToExpiry => _timeToExpiry;

        public Black76VolSkew(
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
        private Black76VolSkew(NaturalCubicSpline spline, double timeToExpiry)
        {
            volSpline = spline ?? throw new ArgumentNullException(nameof(spline));
            _timeToExpiry = timeToExpiry;
        }

        private static Black76VolSkew Create(
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
            return new Black76VolSkew(spline, timeToExpiry);
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
            return new Black76VolSkew(volSpline.Bump(bumpAmount), TimeToExpiry);
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
        public VolSkewDTO ToDTO()
        {

            return new VolSkewDTO
            {
                VolCurve = volSpline.RawPoints
                    .Select(p => new VolPoint { Moneyness = p.X, IV = p.Y })
                    .ToList()
            };
        }

        public IParametricModelSkew FromDTO(VolSkewDTO dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            if (dto.VolCurve == null || dto.VolCurve.Count == 0)
                throw new ArgumentException("VolSurfaceDTO.VolCurve is empty.", nameof(dto));

            var ordered = dto.VolCurve.OrderBy(p => p.Moneyness).ToArray();

            var spline = new NaturalCubicSpline(
                ordered.Select(p => p.Moneyness).ToArray(),
                ordered.Select(p => p.IV).ToArray());

            return new Black76VolSkew(spline, dto.timeToExpiry);
        }
    }
    
    
    /// <summary>
    /// Black76 volatility surface (2D: moneyness × timeToExpiry).
    /// Built from multiple Black76VolSkewSurface objects (one per expiry).
    /// </summary>
    public class Black76VolSurface : IParametricModelSurface
    {
        // Internal list of skews; expiry is available via each skew’s internal field.
        private readonly List<Black76VolSkew> _skews;

        /// <summary>
        /// Construct a multi-expiry vol surface from parameter tuples.
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

            _skews = new List<Black76VolSkew>();

            foreach (var p in skewParamsList)
            {
                if (p.callData == null) throw new ArgumentException("callData cannot be null", nameof(p.callData));
                if (p.putData == null) throw new ArgumentException("putData cannot be null", nameof(p.putData));
                if (p.forwardPrice <= 0) throw new ArgumentException("forwardPrice must be positive", nameof(p.forwardPrice));
                if (p.timeToExpiry <= 0) throw new ArgumentException("timeToExpiry must be positive", nameof(p.timeToExpiry));
                if (p.OICutoff < 0) throw new ArgumentException("OICutoff must be non-negative", nameof(p.OICutoff));

                var skew = new Black76VolSkew(
                    p.callData,
                    p.putData,
                    p.forwardPrice,
                    p.riskFreeRate,
                    p.timeToExpiry,
                    p.OICutoff);

                _skews.Add(skew);
            }

            // Sort by timeToExpiry from the skew objects
            _skews = _skews.OrderBy(s => s.TimeToExpiry).ToList();
        }

        /// <summary>
        /// Construct a multi-expiry vol surface directly from existing skews.
        /// </summary>
        public Black76VolSurface(IEnumerable<Black76VolSkew> skews)
        {
            if (skews == null)
                throw new ArgumentNullException(nameof(skews));

            _skews = skews
                .OrderBy(s => s.TimeToExpiry)
                .ToList();
        }

        /// <summary>
        /// Convenience constructor for a single-expiry surface.
        /// Internally wraps the parameters into a single skew and constructs a 1-expiry surface.
        /// </summary>
        public Black76VolSurface(
            IEnumerable<(double strike, double Price, double OI)> callData,
            IEnumerable<(double strike, double Price, double OI)> putData,
            double forwardPrice,
            double riskFreeRate,
            double timeToExpiry,
            double OICutoff)
            : this(new[]
            {
                (callData, putData, forwardPrice, riskFreeRate, timeToExpiry, OICutoff)
            })
        {
        }

        /// <summary>
        /// Read-only list of constituent skew surfaces ordered by expiry.
        /// </summary>
        public IReadOnlyList<Black76VolSkew> SkewSurfaces => _skews.AsReadOnly();

        /// <summary>
        /// List of expiry times (TimeToExpiry) from the underlying skews.
        /// </summary>
        public IReadOnlyList<double> Expiries =>
            _skews.Select(s => s.TimeToExpiry).ToList().AsReadOnly();

        /// <summary>
        /// Returns implied volatility at given (timeToExpiry, moneyness) via spline + time interpolation.
        /// </summary>
        public double GetVol(double timeToExpiry, double moneyness)
        {
            if (_skews == null || _skews.Count == 0)
                throw new InvalidOperationException("Vol surface has no skews.");

            // If the query time is before the first expiry
            if (timeToExpiry <= _skews.First().TimeToExpiry)
                return _skews.First().GetVol(moneyness);

            // If the query time is beyond the last expiry
            if (timeToExpiry >= _skews.Last().TimeToExpiry)
                return _skews.Last().GetVol(moneyness);

            // Find the two nearest skews by expiry
            for (int i = 0; i < _skews.Count - 1; i++)
            {
                var lower = _skews[i];
                var upper = _skews[i + 1];

                if (timeToExpiry >= lower.TimeToExpiry && timeToExpiry <= upper.TimeToExpiry)
                {
                    double t1 = lower.TimeToExpiry;
                    double t2 = upper.TimeToExpiry;

                    double vol1 = lower.GetVol(moneyness);
                    double vol2 = upper.GetVol(moneyness);

                    // Linear interpolation in time
                    double w = (timeToExpiry - t1) / (t2 - t1);
                    return vol1 + w * (vol2 - vol1);
                }
            }

            // Should not reach here, but safe fallback
            return _skews.Last().GetVol(moneyness);
        }

        /// <summary>
        /// Returns all parameter values for this vol surface in 2D form:
        /// (parameterName, expiryString, value)
        /// Currently includes ATMVol for each expiry.
        /// </summary>
        public IEnumerable<(string parameterName, string expiryString, double value)> GetParameters()
        {
            if (_skews == null || _skews.Count == 0)
                yield break;

            foreach (var skew in _skews)
            {
                var paramDict = skew.GetParameters();

                foreach (var kvp in paramDict)
                {
                    string expiryString = skew.TimeToExpiry.ToString("0.######");
                    yield return (kvp.Key, expiryString, kvp.Value);
                }
            }
        }

        /// <summary>
        /// Parallel absolute volatility bump across all skews.
        /// </summary>
        public IParametricModelSurface Bump(double bumpAmount)
        {
            if (_skews == null || _skews.Count == 0)
                throw new InvalidOperationException("Cannot bump an empty vol surface.");

            var bumpedSkews = _skews
                .Select(skew => (Black76VolSkew)skew.Bump(bumpAmount))
                .ToList();

            return new Black76VolSurface(bumpedSkews);
        }

        /// <summary>
        /// Pointwise bump by parameter name (delegates to multi-bump).
        /// </summary>
        public IParametricModelSurface Bump(string parameterName, double bumpAmount)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                throw new ArgumentNullException(nameof(parameterName));

            return Bump(new[] { (parameterName, bumpAmount) });
        }

        /// <summary>
        /// Applies multiple parameter bumps sequentially across all skews.
        /// </summary>
        public IParametricModelSurface Bump(IEnumerable<(string parameterName, double bumpAmount)> bumps)
        {
            if (bumps == null)
                throw new ArgumentNullException(nameof(bumps));

            var bumpList = bumps.ToList();
            if (!bumpList.Any())
                return this;

            if (_skews == null || _skews.Count == 0)
                throw new InvalidOperationException("Cannot bump an empty vol surface.");

            var bumpedSkews = new List<Black76VolSkew>(_skews.Count);

            foreach (var skew in _skews)
            {
                var bumped = skew.Bump(bumpList);
                if (bumped is not Black76VolSkew bumpedSkew)
                    throw new InvalidOperationException("Bump operation on skew returned an unexpected type.");

                bumpedSkews.Add(bumpedSkew);
            }

            return new Black76VolSurface(bumpedSkews);
        }

        /// <summary>
        /// Returns the list of bumpable parameter names for this surface.
        /// </summary>
        public IEnumerable<string> GetBumpParamNames()
        {
            return new List<string> { Black76VolSkew.AtmVolParam };
        }

        /// <summary>
        /// Converts the full volatility surface into a DTO.
        /// </summary>
        public VolSurfaceDTO ToDTO()
        {
            if (_skews == null || _skews.Count == 0)
                throw new InvalidOperationException("Cannot create DTO from an empty vol surface.");

            var dto = new VolSurfaceDTO();

            foreach (var skew in _skews)
            {
                var skewDto = skew.ToDTO() as VolSkewDTO;
                skewDto.timeToExpiry = skew.TimeToExpiry;
                dto.Skews.Add(skewDto);
            }

            return dto;
        }

        /// <summary>
        /// Reconstructs a volatility surface from its DTO representation.
        /// </summary>
        public IParametricModelSurface FromDTO(VolSurfaceDTO dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            if (dto.Skews == null || dto.Skews.Count == 0)
                throw new ArgumentException("VolSurfaceDTO.Skews is empty.", nameof(dto));

            var skews = new List<Black76VolSkew>();

            foreach (var skewDto in dto.Skews)
            {
                var skew = (Black76VolSkew)new Black76VolSkew(
                    new List<(double, double, double)>(),
                    new List<(double, double, double)>(),
                    1.0, 0.0, skewDto.timeToExpiry, 0.0)
                    .FromDTO(skewDto);

                skews.Add(skew);
            }

            return new Black76VolSurface(skews);
        }
    }


    // ---------- DTOs kept as-is ----------
    public class VolSkewDTO
    {
        public double timeToExpiry {get; set;}
        public List<VolPoint> VolCurve { get; set; } = new();
    }

    
    /// <summary>
    /// Data Transfer Object (DTO) representing a full 2D volatility surface.
    /// Each surface consists of multiple skews (one per expiry).
    /// </summary>
    public class VolSurfaceDTO
    {
        /// <summary>
        /// Collection of skew slices (vol vs moneyness) for each expiry.
        /// </summary>
        public List<VolSkewDTO> Skews { get; set; } = new();

        /// <summary>
        /// Optional model name or tag (for serialization / identification).
        /// Example: "Black76"
        /// </summary>
        public string? ModelName { get; set; }

    }


    public class VolPoint
    {
        public double Moneyness { get; set; }
        public double IV { get; set; }
    }
}