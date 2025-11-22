// File: QuantitativeAnalytics/Models/Black76/Black76PriceSpaceVolSkew.cs
using System.Collections.Concurrent;

namespace QuantitativeAnalytics
{
    /// <summary>
    /// Price-space based volatility skew.
    /// Implements IParametricModelSkew and uses a price-backbone (put-price) that is convex/monotone.
    /// Interpolates prices piecewise-linearly and inverts to implied vol on demand (node IVs cached).
    /// Constructor signature is identical to Black76VolSkew.
    /// </summary>
    internal class Black76PriceSpaceVolSkew : IParametricModelSkew
    {
        // Immutable core fields
        private readonly double[] strikes;    // node strikes (K_i), ascending
        private readonly double[] putPrices;  // node put prices P(K_i)
        private readonly double[] ivNodes;    // cached node implied volatilities sigma_i
        private readonly double forward;
        private readonly double r;
        private readonly double _timeToExpiry;
        private readonly int n;

        // Optional memo cache for non-node GetVol queries (bounded behavior can be added later)
        // Keyed by K (strike); store IV
        private readonly ConcurrentDictionary<double, double> nonNodeIvCache = new ConcurrentDictionary<double, double>();

        // small tolerance for equality checks
        private const double StrikeEqualityTol = 1e-12;

        public double TimeToExpiry => _timeToExpiry;

        /// <summary>
        /// Constructor (same signature as Black76VolSkew).
        /// Implements pipeline: filter by OI -> intrinsic bounds -> convert to puts for shape checks -> convexity/monotonicity cleaning -> choose side -> build backbone -> cache node IVs.
        /// Assumes provided Prices are NPV-like (consistent with existing codebase).
        /// </summary>
        public Black76PriceSpaceVolSkew(
            IEnumerable<(double strike, double Price, double OI)> callData,
            IEnumerable<(double strike, double Price, double OI)> putData,
            double forwardPrice,
            double riskFreeRate,
            double timeToExpiry,
            double OICutoff)
        {
            // Validate inputs
            if (callData == null) throw new ArgumentNullException(nameof(callData));
            if (putData == null) throw new ArgumentNullException(nameof(putData));
            if (forwardPrice <= 0) throw new ArgumentException("forwardPrice must be positive", nameof(forwardPrice));
            if (timeToExpiry <= 0) throw new ArgumentException("timeToExpiry must be positive", nameof(timeToExpiry));
            if (OICutoff < 0) throw new ArgumentException("OICutoff must be non-negative", nameof(OICutoff));

            forward = forwardPrice;
            r = riskFreeRate;
            _timeToExpiry = timeToExpiry;

            // 1) Filter by OI
            var calls = callData.Where(c => c.OI >= OICutoff).ToList();
            var puts = putData.Where(p => p.OI >= OICutoff).ToList();

            // Build map of strikes present
            var allStrikes = new SortedSet<double>(calls.Select(c => c.strike).Concat(puts.Select(p => p.strike)));
            if (!allStrikes.Any())
                throw new ArgumentException($"No option data remain after applying OICutoff={OICutoff}");

            // Precompute discount if needed (used for parity conversions when interpreting as NPV)
            double discount = Math.Exp(-r * _timeToExpiry);

            // 2) Build candidate list and apply intrinsic / trivial bounds
            var candidatesByStrike = new Dictionary<double, List<Candidate>>(allStrikes.Count);
            foreach (var K in allStrikes)
            {
                candidatesByStrike[K] = new List<Candidate>();
            }

            // Add call candidates (assume Price is NPV / mid)
            foreach (var c in calls)
            {
                // Basic sanity
                if (c.Price < 0) continue;

                // Intrinsic bound for call (NPV): lower bound is max(exp(-rT)*(forward - K), 0)
                double lowerCall = Math.Max(0.0, forward - c.strike) * discount;
                if (c.Price + 1e-12 < lowerCall) continue; // violates intrinsic lower bound
                if (c.Price > forward + 1e-6) continue;    // absurdly large

                candidatesByStrike[c.strike].Add(new Candidate
                {
                    Strike = c.strike,
                    IsCall = true,
                    Price = c.Price,
                    OI = c.OI
                });
            }

            // Add put candidates
            foreach (var p in puts)
            {
                if (p.Price < 0) continue;

                double lowerPut = Math.Max(0.0, p.strike - forward) * discount;
                if (p.Price + 1e-12 < lowerPut) continue; // violates intrinsic lower bound
                if (p.Price > p.strike * discount + 1e-6) continue; // absurdly large

                candidatesByStrike[p.strike].Add(new Candidate
                {
                    Strike = p.strike,
                    IsCall = false,
                    Price = p.Price,
                    OI = p.OI
                });
            }

            // Remove strikes without any surviving candidate
            var strikeList = candidatesByStrike.Where(kv => kv.Value != null && kv.Value.Count > 0)
                                               .OrderBy(kv => kv.Key)
                                               .Select(kv => kv.Key)
                                               .ToList();
            if (!strikeList.Any())
                throw new ArgumentException("No valid candidates after intrinsic bounds filtering.");

            // 3) Convert candidates to put-price domain for shape checks (but keep original info)
            foreach (var kv in candidatesByStrike)
            {
                foreach (var cand in kv.Value)
                {
                    if (cand.IsCall)
                    {
                        // Put = Call - discount*(forward - K)  (assuming Price is NPV)
                        cand.PutPrice = cand.Price - discount * (forward - cand.Strike);
                    }
                    else
                    {
                        cand.PutPrice = cand.Price;
                    }
                }
                // sort by OI desc so primary candidate is first
                kv.Value.Sort((a, b) => b.OI.CompareTo(a.OI));
            }

            // 4) Build initial backbone (one put-price per strike) using the top candidate per strike as default
            var backbone = strikeList.Select(K =>
            {
                var list = candidatesByStrike[K];
                var top = list.OrderByDescending(x => x.OI).First();
                return new BackboneNode
                {
                    Strike = K,
                    PutPrice = top.PutPrice,
                    Candidates = list
                };
            }).OrderBy(b => b.Strike).ToList();

            // 5) Iteratively enforce monotonicity & convexity on backbone (price-space)
            // Use discrete slope test with small tolerance
            const double tol = 1e-12;
            bool didRemove;
            do
            {
                didRemove = false;
                int m = backbone.Count;
                if (m < 3) break;

                // compute slopes between adjacent nodes
                var slopes = new double[m - 1];
                for (int i = 0; i < m - 1; ++i)
                    slopes[i] = (backbone[i + 1].PutPrice - backbone[i].PutPrice) / (backbone[i + 1].Strike - backbone[i].Strike);

                // find any i where slopes[i+1] + tol < slopes[i] -> convexity violation on triple (i,i+1,i+2) -> offending middle node i+1
                var offendingIndices = new List<int>();
                for (int i = 0; i < slopes.Length - 1; ++i)
                {
                    if (slopes[i + 1] + tol < slopes[i])
                        offendingIndices.Add(i + 1);
                }

                if (!offendingIndices.Any()) break;

                // choose which offending node to remove deterministically: among offending nodes pick the one whose top candidate has lowest OI
                int removeIndex = -1;
                double minOi = double.MaxValue;
                foreach (var oiIdx in offendingIndices.Distinct())
                {
                    var candList = backbone[oiIdx].Candidates;
                    var topOI = candList.Max(c => c.OI);
                    if (topOI < minOi)
                    {
                        minOi = topOI;
                        removeIndex = oiIdx;
                    }
                }

                if (removeIndex >= 0)
                {
                    backbone.RemoveAt(removeIndex);
                    didRemove = true;
                }
            } while (didRemove);

            // final sanity
            if (backbone.Count < 2)
                throw new InvalidOperationException("Not enough strikes remain after convexity filtering.");

            // 6) Final per-strike selection (after shape cleaning): choose the most-liquid surviving candidate for each strike
            var finalNodes = new List<BackboneNode>(backbone.Count);
            foreach (var node in backbone)
            {
                // select the candidate among node.Candidates with highest OI (they were filtered earlier)
                var chosen = node.Candidates.OrderByDescending(c => c.OI).First();
                // compute final put price consistent with chosen side
                double finalPutPrice = chosen.IsCall ? (chosen.Price - discount * (forward - node.Strike)) : chosen.Price;

                finalNodes.Add(new BackboneNode
                {
                    Strike = node.Strike,
                    PutPrice = finalPutPrice,
                    Candidates = node.Candidates
                });
            }

            finalNodes = finalNodes.OrderBy(nod => nod.Strike).ToList();

            // commit arrays
            n = finalNodes.Count;
            strikes = finalNodes.Select(nod => nod.Strike).ToArray();
            putPrices = finalNodes.Select(nod => nod.PutPrice).ToArray();
            ivNodes = new double[n];

            // 7) Precompute IVs at nodes (cache)
            for (int i = 0; i < n; ++i)
            {
                double K = strikes[i];
                double p = putPrices[i];
                double iv;
                try
                {
                    // Use Black76.ComputeIV (assumed available). Using isCall=false because we invert put price.
                    // NOTE: ComputeIV parameter ordering assumed compatible with your project (we used earlier calls elsewhere)
                    iv = Black76.ComputeIV(false, forward, K, _timeToExpiry, r, p, initialGuess: 0.2);
                }
                catch
                {
                    // fallback conservative guess
                    iv = 0.2;
                }
                if (double.IsNaN(iv) || iv < 0) iv = 0.0;
                ivNodes[i] = iv;
            }
        }

        /// <summary>
        /// Evaluate piecewise-linear put price P(K) using the backbone nodes.
        /// Extrapolates linearly using end segments.
        /// </summary>
        private double PriceAtK(double K)
        {
            if (K <= strikes[0] + StrikeEqualityTol) return putPrices[0];
            if (K >= strikes[n - 1] - StrikeEqualityTol) return putPrices[n - 1];

            int idx = Array.BinarySearch(strikes, K);
            if (idx >= 0) return putPrices[idx];

            int j = ~idx - 1;
            if (j < 0) j = 0;
            if (j >= n - 1) j = n - 2;

            double xL = strikes[j], xR = strikes[j + 1];
            double yL = putPrices[j], yR = putPrices[j + 1];
            double t = (K - xL) / (xR - xL);
            return yL + t * (yR - yL);
        }

        /// <summary>
        /// Public GetVol: returns implied vol for input moneyness m = K / forward.
        /// Uses piecewise-linear price interpolation and inverts to implied vol (warm-started from node IVs).
        /// </summary>
        public double GetVol(double moneyness)
        {
            if (moneyness <= 0) return 0.0;
            double Kq = moneyness * forward;

            // exact node match?
            int idx = Array.BinarySearch(strikes, Kq);
            if (idx >= 0)
            {
                double ivExact = ivNodes[idx];
                return ivExact;
            }

            // use cache for repeated non-node queries
            if (nonNodeIvCache.TryGetValue(Kq, out double cachedIv))
                return cachedIv;

            // find segment
            int seg = ~Array.BinarySearch(strikes, Kq) - 1;
            if (seg < 0) seg = 0;
            if (seg > n - 2) seg = n - 2;

            double Kleft = strikes[seg], Kright = strikes[seg + 1];
            double Pleft = putPrices[seg], Pright = putPrices[seg + 1];
            double t = (Kq - Kleft) / (Kright - Kleft);
            double Pq = Pleft + t * (Pright - Pleft);

            // warm start iv by linear interp of node ivs
            double ivGuess = ivNodes[seg] + t * (ivNodes[seg + 1] - ivNodes[seg]);

            double ivq;
            try
            {
                ivq = Black76.ComputeIV(false, forward, Kq, _timeToExpiry, r, Pq, initialGuess: ivGuess);
            }
            catch
            {
                // fallback robust bisection
                ivq = InvertPutPriceBisection(Pq, forward, Kq, r, _timeToExpiry);
            }

            if (double.IsNaN(ivq) || ivq < 0) ivq = 0.0;

            // cache and return
            nonNodeIvCache[Kq] = ivq;
            return ivq;
        }

        // Fallback bisection inversion for put price -> implied vol (very robust)
        private double InvertPutPriceBisection(double price, double F, double K, double rLocal, double T)
        {
            double lo = 1e-6, hi = 5.0;
            for (int iter = 0; iter < 80; ++iter)
            {
                double mid = 0.5 * (lo + hi);
                // Use Black76.NPVIV signature: (isCall, forwardPrice, strike, riskFreeRate, iv, timeToExpiry)
                double p = Black76.NPVIV(false, F, K, rLocal, mid, T);
                if (p > price) hi = mid; else lo = mid;
            }
            return 0.5 * (lo + hi);
        }

        // ---------- Bumping and param interface ----------
        public IParametricModelSkew Bump(double bumpAmount)
        {
            // Parallel absolute vol bump on cached node IVs and rebuild putPrices from bumped IVs.
            var bumpedPutPrices = new double[n];
            for (int i = 0; i < n; ++i)
            {
                double bumpedIv = Math.Max(0.0, ivNodes[i] + bumpAmount);
                // Black76.NPVIV signature: (isCall, forwardPrice, strike, riskFreeRate, iv, timeToExpiry)
                bumpedPutPrices[i] = Black76.NPVIV(false, forward, strikes[i], r, bumpedIv, _timeToExpiry);
            }

            return new Black76PriceSpaceVolSkew(bumpedPutPrices, strikes, forward, r, _timeToExpiry);
        }

        // Bump by parameter name (support ATMVol as in other skew)
        public IParametricModelSkew Bump(string parameterName, double bumpAmount)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) throw new ArgumentNullException(nameof(parameterName));
            if (string.Equals(parameterName, "ATMVol", StringComparison.OrdinalIgnoreCase))
            {
                return Bump(bumpAmount);
            }
            throw new ArgumentException($"Unsupported parameter: {parameterName}", nameof(parameterName));
        }

        public IParametricModelSkew Bump(IEnumerable<(string parameterName, double bumpAmount)> bumps)
        {
            if (bumps == null) throw new ArgumentNullException(nameof(bumps));
            IParametricModelSkew current = this;
            foreach (var (param, amt) in bumps)
            {
                current = current.Bump(param, amt);
            }
            return current;
        }

        public IReadOnlyDictionary<string, double> GetParameters()
        {
            // Minimal param view: ATM vol (m=1.0)
            double atmVol = GetVol(1.0);
            return new Dictionary<string, double> { { "ATMVol", atmVol } };
        }

        public IEnumerable<string> GetBumpParamNames()
        {
            return new[] { "ATMVol" };
        }

        // ---------- DTO (serialize cached nodes as moneyness->IV) ----------
        public VolSkewDTO ToDTO()
        {
            var volPoints = new List<VolPoint>(n);
            for (int i = 0; i < n; ++i)
            {
                double m = strikes[i] / forward;
                volPoints.Add(new VolPoint { Moneyness = m, IV = ivNodes[i] });
            }
            return new VolSkewDTO { timeToExpiry = _timeToExpiry, VolCurve = volPoints };
        }

        public IParametricModelSkew FromDTO(VolSkewDTO dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.VolCurve == null || dto.VolCurve.Count == 0) throw new ArgumentException("dto.VolCurve empty");

            // Build arrays from dto (dto.timeToExpiry used)
            var ordered = dto.VolCurve.OrderBy(p => p.Moneyness).ToArray();
            var strikesArr = ordered.Select(p => p.Moneyness * forward).ToArray();
            var ivArr = ordered.Select(p => p.IV).ToArray();

            // Compute putPrices from ivs using Black76.NPVIV signature
            var putPricesArr = new double[ivArr.Length];
            for (int i = 0; i < ivArr.Length; ++i)
            {
                putPricesArr[i] = Black76.NPVIV(false, forward, strikesArr[i], r, ivArr[i], dto.timeToExpiry);
            }

            return new Black76PriceSpaceVolSkew(putPricesArr, strikesArr, forward, r, dto.timeToExpiry);
        }

        // internal convenience ctor used by bump/fromDTO to avoid repeating the whole pipeline
        private Black76PriceSpaceVolSkew(double[] putPricesNodes, double[] strikesNodes, double forwardPrice, double riskFreeRate, double tte)
        {
            if (putPricesNodes == null) throw new ArgumentNullException(nameof(putPricesNodes));
            if (strikesNodes == null) throw new ArgumentNullException(nameof(strikesNodes));
            if (putPricesNodes.Length != strikesNodes.Length) throw new ArgumentException("length mismatch");

            forward = forwardPrice;
            r = riskFreeRate;
            _timeToExpiry = tte;

            n = strikesNodes.Length;
            strikes = (double[])strikesNodes.Clone();
            putPrices = (double[])putPricesNodes.Clone();
            ivNodes = new double[n];

            for (int i = 0; i < n; ++i)
            {
                double K = strikes[i];
                double p = putPrices[i];
                double iv;
                try
                {
                    iv = Black76.ComputeIV(false, forward, K, _timeToExpiry, r, p, initialGuess: 0.2);
                }
                catch
                {
                    iv = 0.2;
                }
                if (double.IsNaN(iv) || iv < 0) iv = 0.0;
                ivNodes[i] = iv;
            }
        }

        // ---------- small internal helper types ----------
        private class Candidate
        {
            public double Strike;
            public bool IsCall;
            public double Price;
            public double PutPrice;
            public double OI;
        }

        private class BackboneNode
        {
            public double Strike;
            public double PutPrice;
            public List<Candidate> Candidates;
        }
    }

    /// <summary>
    /// Black-76 price-space volatility surface (moneyness × expiry),
    /// built from multiple Black76PriceSpaceVolSkew objects.
    /// Mirrors the structure of Black76VolSurface.
    /// </summary>
    public class Black76PriceSpaceVolSurface : IParametricModelSurface
    {
        private readonly List<Black76PriceSpaceVolSkew> _skews;

        // ===============================================================
        //  CONSTRUCTORS
        // ===============================================================

        /// <summary>
        /// Construct a multi-expiry surface from raw skew parameters.
        /// Same signature pattern as Black76VolSurface.
        /// </summary>
        public Black76PriceSpaceVolSurface(
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

            var built = new List<Black76PriceSpaceVolSkew>();

            foreach (var p in skewParamsList)
            {
                if (p.callData == null) throw new ArgumentException("callData cannot be null");
                if (p.putData == null) throw new ArgumentException("putData cannot be null");
                if (p.forwardPrice <= 0) throw new ArgumentException("forwardPrice must be positive");
                if (p.timeToExpiry <= 0) throw new ArgumentException("timeToExpiry must be positive");
                if (p.OICutoff < 0) throw new ArgumentException("OICutoff must be non-negative");

                // pre-filter by OI so we avoid building a skew for dead expiries
                var callFiltered = p.callData.Where(c => c.OI >= p.OICutoff).ToList();
                var putFiltered  = p.putData.Where(q => q.OI >= p.OICutoff).ToList();

                // Skip expiry if no liquid strikes at all (calls + puts combined < 1)
                if (callFiltered.Count + putFiltered.Count < 1)
                {
                    Console.WriteLine(
                        $"⚠️ Skipping expiry slice (TTE={p.timeToExpiry:F4}): no liquid strikes (OI cutoff={p.OICutoff}).");
                    continue;
                }
                // Build skew using the filtered lists (skew internally will still perform bounds/convexity cleaning)
                try
                {
                    var skew = new Black76PriceSpaceVolSkew(
                        callFiltered,
                        putFiltered,
                        p.forwardPrice,
                        p.riskFreeRate,
                        p.timeToExpiry,
                        p.OICutoff);

                    built.Add(skew);
                }
                catch (Exception ex)
                {
                    // If skew construction fails for some reason, log and skip this expiry
                    Console.WriteLine(
                        $"⚠️ Skipping expiry slice (TTE={p.timeToExpiry:F4}): skew construction failed: {ex.Message}");
                    continue;
                }
            }

            if (built.Count == 0)
            throw new InvalidOperationException("No valid skews were constructed for the surface (all expiries skipped).");

            _skews = built.OrderBy(s => s.TimeToExpiry).ToList();
        }

        /// <summary>
        /// Construct from existing skews.
        /// Pattern matches Black76VolSurface constructor.
        /// </summary>
        internal Black76PriceSpaceVolSurface(IEnumerable<Black76PriceSpaceVolSkew> skews)
        {
            if (skews == null)
                throw new ArgumentNullException(nameof(skews));

            var list = skews.ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("Cannot construct vol surface with zero skews.");

            if (list.Any(s => s == null))
                throw new ArgumentException("One or more skews are null.");

            _skews = list
                .OrderBy(s => s.TimeToExpiry)
                .ToList();
        }

        /// <summary>
        /// Convenience constructor: single-expiry surface.
        /// Mirrors Black76VolSurface's convenience constructor.
        /// </summary>
        public Black76PriceSpaceVolSurface(
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

        // ===============================================================
        //  CORE EVALUATION
        // ===============================================================

        /// <summary>
        /// GetVol(t, m) using total variance interpolation, identical structure to Black76VolSurface.
        /// </summary>
        public double GetVol(double timeToExpiry, double moneyness)
        {
            if (_skews.Count == 0)
                throw new InvalidOperationException("Vol surface has no skews.");

            var first = _skews[0];
            var last  = _skews[^1];

            if (timeToExpiry <= first.TimeToExpiry)
                return first.GetVol(moneyness);

            if (timeToExpiry >= last.TimeToExpiry)
                return last.GetVol(moneyness);

            // Find bracketing skews
            for (int i = 0; i < _skews.Count - 1; i++)
            {
                var lower = _skews[i];
                var upper = _skews[i + 1];

                if (lower.TimeToExpiry <= timeToExpiry && timeToExpiry <= upper.TimeToExpiry)
                {
                    double tL = lower.TimeToExpiry;
                    double tU = upper.TimeToExpiry;

                    double vL = lower.GetVol(moneyness);
                    double vU = upper.GetVol(moneyness);

                    double wL = vL * vL * tL;  // total variance
                    double wU = vU * vU * tU;

                    double α = (timeToExpiry - tL) / (tU - tL);
                    double wMid = (1 - α) * wL + α * wU;

                    return Math.Sqrt(wMid / timeToExpiry);
                }
            }

            return last.GetVol(moneyness);
        }

        // ===============================================================
        //  DTO SUPPORT
        // ===============================================================

        public VolSurfaceDTO ToDTO()
        {
            if (_skews == null || _skews.Count == 0)
                throw new InvalidOperationException("Cannot create DTO from an empty vol surface.");

            var dto = new VolSurfaceDTO();

            foreach (var skew in _skews)
            {
                var skewDto = skew.ToDTO();
                skewDto.timeToExpiry = skew.TimeToExpiry;
                dto.Skews.Add(skewDto);
            }

            return dto;
        }

        public IParametricModelSurface FromDTO(VolSurfaceDTO dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            if (dto.Skews == null || dto.Skews.Count == 0)
                throw new ArgumentException("VolSurfaceDTO.Skews is empty.", nameof(dto));

            var skews = new List<Black76PriceSpaceVolSkew>();

            foreach (var skewDto in dto.Skews)
            {
                var skew = (Black76PriceSpaceVolSkew) new Black76PriceSpaceVolSkew(
                    new List<(double, double, double)>(),
                    new List<(double, double, double)>(),
                    1.0, 0.0, skewDto.timeToExpiry, 0.0)
                    .FromDTO(skewDto);

                skews.Add(skew);
            }

            return new Black76PriceSpaceVolSurface(skews);
        }

        // ===============================================================
        //  BUMPING
        // ===============================================================

        public IParametricModelSurface Bump(double bumpAmount)
        {
            var bumped = _skews
                .Select(s => (Black76PriceSpaceVolSkew)s.Bump(bumpAmount))
                .ToList();

            return new Black76PriceSpaceVolSurface(bumped);
        }

        public IParametricModelSurface Bump(string paramName, double bumpAmount)
        {
            var bumped = _skews
                .Select(s => (Black76PriceSpaceVolSkew)s.Bump(paramName, bumpAmount))
                .ToList();

            return new Black76PriceSpaceVolSurface(bumped);
        }

        public IParametricModelSurface Bump(IEnumerable<(string parameterName, double bumpAmount)> bumps)
        {
            var current = this;

            foreach (var (name, amt) in bumps)
                current = current.Bump(name, amt) as Black76PriceSpaceVolSurface
                        ?? throw new InvalidOperationException("Bump failed.");

            return current;
        }

        // ===============================================================
        //  PARAMETERS + BUMP NAMES
        // ===============================================================

        public IEnumerable<string> GetBumpParamNames()
        {
            yield return "ATMVol";
        }

        public IEnumerable<(string parameterName, string expiryString, double value)> GetParameters()
        {
            double atm = _skews[0].GetVol(1.0);
            yield return ("ATMVol", "Spot", atm);
        }
    }
}