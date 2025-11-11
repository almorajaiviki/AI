using System.Collections.Immutable;
using CalendarLib;
using QuantitativeAnalytics;
using InstrumentStatic;

namespace MarketData
{
    /// <summary>
    /// Represents a single atomic snapshot of all market data at a given timestamp.
    /// </summary>
    public sealed class AtomicMarketSnap
    {
        // Core market data
        private readonly string _indexTradingSymbol;
        private readonly DateTime _initializationTime;
        private readonly DateTime _expiry;
        private readonly double _indexSpot;
        private readonly double _impliedFuture;
        private readonly uint _token;
        private readonly double _riskFreeRate;
        private readonly double _divYield;
        private readonly IParametricModelSurface _volSurface;
        private readonly MarketCalendar _calendar;
        // --- inside namespace MarketData, in AtomicMarketSnap.cs ---
        // Add this private field near the other private readonly fields at top of class:
        private readonly ForwardCurve? _forwardCurve;

        // Add a public read-only accessor (near your other public properties):
        /// <summary>
        /// Forward curve built from the snapshot's futures and spot (may be null if not built).
        /// </summary>
        public ForwardCurve? ForwardCurve => _forwardCurve;

        // Option and Future stores
        private readonly ImmutableDictionary<uint, OptionSnapshot> _optionsByToken;
        private readonly ImmutableDictionary<string, OptionSnapshot> _optionsByTradingSymbol;
        private readonly ImmutableDictionary<double, OptionPair> _optionChainElements;
        private readonly ImmutableDictionary<uint, FutureDetailDTO> _futuresByToken;
        private readonly ImmutableDictionary<string, FutureDetailDTO> _futuresByTradingSymbol;

        public ImmutableDictionary<uint, OptionSnapshot> OptionsByToken => _optionsByToken;

        // === Replace the existing AtomicMarketSnap constructor signature and body with the following ===
        //
        // previous signature ended with: string indexTradingSymbol, IParametricModelSurface volSurface
        //
        // New signature inserts ForwardCurve? forwardCurve before volSurface (so we can keep
        // change-site localized in MarketData.UpdateAtomicSnapshot)
        public AtomicMarketSnap(
            DateTime initializationTime,
            DateTime expiry,
            double indexSpot,
            double impliedFuture,
            uint token,
            double riskFreeRate,
            double divYield,
            ImmutableArray<OptionSnapshot> optionSnapshots,
            ImmutableArray<OptionGreeks> greeks,
            ImmutableArray<FutureElement> futureElements,
            MarketCalendar calendar,
            string indexTradingSymbol,
            ForwardCurve? forwardCurve,             // <-- new parameter added here
            IParametricModelSurface volSurface)
        {
            // === Validation ===
            if (initializationTime == default)
                throw new ArgumentException("Invalid initialization time", nameof(initializationTime));
            if (expiry <= initializationTime)
                throw new ArgumentException("Expiry must be after initialization time", nameof(expiry));
            if (indexSpot <= 0 || impliedFuture <= 0)
                throw new ArgumentException("Index spot and implied future must be positive");
            if (riskFreeRate < 0)
                throw new ArgumentException("Risk-free rate cannot be negative");
            if (calendar == null)
                throw new ArgumentNullException(nameof(calendar));
            if (optionSnapshots.IsDefaultOrEmpty)
                throw new ArgumentException("At least one option snapshot required");
            if (futureElements.IsDefault)
                throw new ArgumentNullException(nameof(futureElements));

            _indexTradingSymbol = indexTradingSymbol ?? throw new ArgumentNullException(nameof(indexTradingSymbol));
            _initializationTime = initializationTime;
            _expiry = expiry;
            _indexSpot = indexSpot;
            _impliedFuture = impliedFuture;
            _token = token;
            _riskFreeRate = riskFreeRate;
            _divYield = divYield;
            _calendar = calendar;
            _volSurface = volSurface ?? throw new ArgumentNullException(nameof(volSurface));

            // store the forward curve (may be null if callers didn't build one)
            _forwardCurve = forwardCurve;

            var (byToken, bySymbol, byStrike) = BuildOptionStores(optionSnapshots, greeks);
            _optionsByToken = byToken;
            _optionsByTradingSymbol = bySymbol;
            _optionChainElements = byStrike;

            var (futuresByToken, futuresByTradingSymbol) = BuildFutureStores(futureElements);
            _futuresByToken = futuresByToken;
            _futuresByTradingSymbol = futuresByTradingSymbol;
        }
        
        // === Core Properties ===
        public string IndexTradingSymbol => _indexTradingSymbol;
        public DateTime InitializationTime => _initializationTime;
        public DateTime Expiry => _expiry;
        public double IndexSpot => _indexSpot;
        public double ImpliedFuture => _impliedFuture;
        public double RiskFreeRate => _riskFreeRate;
        public double DivYield => _divYield;
        public uint Token => _token;
        public IParametricModelSurface VolSurface => _volSurface;

        public ImmutableDictionary<double, OptionPair> OptionChainElements => _optionChainElements;
        public ImmutableDictionary<uint, FutureDetailDTO> FuturesByToken => _futuresByToken;

        // === Store Builders ===
        private static (
            ImmutableDictionary<uint, OptionSnapshot>,
            ImmutableDictionary<string, OptionSnapshot>,
            ImmutableDictionary<double, OptionPair>
        ) BuildOptionStores(ImmutableArray<OptionSnapshot> snapshots, ImmutableArray<OptionGreeks> greeks)
        {
            var byToken = snapshots.ToDictionary(s => s.Token);
            var greeksByToken = greeks.ToDictionary(g => g.Token);

            var strikeGroups = snapshots.GroupBy(s => s.Strike);
            var strikePairs = ImmutableDictionary.CreateBuilder<double, OptionPair>();

            foreach (var group in strikeGroups)
            {
                var call = group.FirstOrDefault(x => x.OptionType == OptionType.CE);
                var put = group.FirstOrDefault(x => x.OptionType == OptionType.PE);
                if (call.Equals(default(OptionSnapshot)) || put.Equals(default(OptionSnapshot)))
                    continue;

                if (!greeksByToken.TryGetValue(call.Token, out var callGreeks)) continue;
                if (!greeksByToken.TryGetValue(put.Token, out var putGreeks)) continue;

                strikePairs[group.Key] = new OptionPair(call, put, callGreeks, putGreeks);
            }

            return (
                byToken.ToImmutableDictionary(),
                byToken.Values.ToImmutableDictionary(s => s.TradingSymbol),
                strikePairs.ToImmutable()
            );
        }

        private static (
            ImmutableDictionary<uint, FutureDetailDTO>,
            ImmutableDictionary<string, FutureDetailDTO>
        ) BuildFutureStores(ImmutableArray<FutureElement> futureElements)
        {
            var byToken = ImmutableDictionary.CreateBuilder<uint, FutureDetailDTO>();
            var bySymbol = ImmutableDictionary.CreateBuilder<string, FutureDetailDTO>();

            foreach (var el in futureElements)
            {
                var detail = new FutureDetailDTO(el.Future.GetSnapshot(), el.FutureGreeks, el.FutureSpreads);
                byToken[el.Future.Token] = detail;
                bySymbol[el.Future.TradingSymbol] = detail;
            }

            return (byToken.ToImmutable(), bySymbol.ToImmutable());
        }

        // === DTO Conversion ===
        public AtomicMarketSnapDTO ToDTO()
        {
            var pairs = _optionChainElements.Values.Select(pair =>
            {
                var call = pair.Call;
                var put = pair.Put;
                var cg = pair.CallGreeks;
                var pg = pair.PutGreeks;
                var cs = pair.CallSpreads;
                var ps = pair.PutSpreads;

                return new OptionPairDTO
                {
                    strike = call.Strike,
                    expiry = _expiry,
                    timestampUnixMs = new DateTimeOffset(_initializationTime).ToUnixTimeMilliseconds(),

                    // Call
                    C_token = call.Token,
                    C_bid = call.Bid,
                    C_ask = call.Ask,
                    C_ltp = call.LTP,
                    C_oi = call.OI,
                    C_iv = cg.IV_Used,
                    C_delta = cg.Delta,
                    C_gamma = cg.Gamma,
                    C_vega = cg.Vega.Length > 0 ? cg.Vega[0].Item2 : 0,
                    C_theta = cg.Theta,
                    C_rho = cg.Rho,
                    C_npv = cg.NPV,
                    C_bidSpread = cs.BidSpread,
                    C_askSpread = cs.AskSpread,

                    // Put
                    P_token = put.Token,
                    P_bid = put.Bid,
                    P_ask = put.Ask,
                    P_ltp = put.LTP,
                    P_oi = put.OI,
                    P_iv = pg.IV_Used,
                    P_delta = pg.Delta,
                    P_gamma = pg.Gamma,
                    P_vega = pg.Vega.Length > 0 ? pg.Vega[0].Item2 : 0,
                    P_theta = pg.Theta,
                    P_rho = pg.Rho,
                    P_npv = pg.NPV,
                    P_bidSpread = ps.BidSpread,
                    P_askSpread = ps.AskSpread
                };
            }).ToArray();

            return new AtomicMarketSnapDTO
            {
                Index = _indexTradingSymbol,
                Spot = _indexSpot,
                ImpliedFuture = _impliedFuture,
                RiskFreeRate = _riskFreeRate,
                DivYield = _divYield,
                Expiry = _expiry,
                SnapTime = _initializationTime,
                OptionPairs = pairs,
                Futures = _futuresByToken.Values.ToArray(),
                VolSurface = _volSurface.ToDTO(),
                ForwardCurve = _forwardCurve != null ? ForwardCurveDTO.FromForwardCurve(_forwardCurve) : null
            };
        }

        /// <summary>
        /// Reconstructs a full AtomicMarketSnap from a DTO representation.
        /// This reverses the ToDTO() operation and rebuilds option/future dictionaries.
        /// </summary>
        public AtomicMarketSnap(AtomicMarketSnapDTO dto, MarketCalendar calendar, VolatilityModel volatilityModel)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (calendar == null) throw new ArgumentNullException(nameof(calendar));

            // === Assign core scalar fields ===
            _indexTradingSymbol = dto.Index ?? string.Empty;
            _initializationTime = dto.SnapTime;
            _expiry = dto.Expiry;
            _indexSpot = dto.Spot;
            _impliedFuture = dto.ImpliedFuture;
            _riskFreeRate = dto.RiskFreeRate;
            _divYield = dto.DivYield;
            _calendar = calendar;

            double tte = _calendar.GetYearFraction(_initializationTime, _expiry);

            // === Rebuild VolSurface ===
            if (dto.VolSurface == null)
                throw new ArgumentNullException(nameof(dto.VolSurface), "VolSurfaceDTO is required to rebuild AtomicMarketSnap.");
            _volSurface = VolSurface.FromDTO(dto.VolSurface); // assume VolSurface.FromDTO(VolSurfaceDTO dto)

            // === Rebuild ForwardCurve ===
            if (dto.ForwardCurve != null)
            {
                _forwardCurve = dto.ForwardCurve.ToForwardCurve();
            }
            else
            {
                _forwardCurve = null;
            }

            // === Rebuild options from OptionPairDTOs ===
            var optionSnapshots = new List<OptionSnapshot>();
            var optionGreeks = new List<OptionGreeks>();
            IGreeksCalculator _greeksCalculator;

            if (volatilityModel == VolatilityModel.Black76)
            {
                _greeksCalculator = Black76GreeksCalculator.Instance;
            }
            else
            {
                throw new ArgumentException($"Volatility model '{volatilityModel}' is not supported yet.");
            }

            if (dto.OptionPairs != null)
            {
                foreach (var pair in dto.OptionPairs)
                {
                    // Call snapshot + greeks
                    var callSnap = new OptionSnapshot(
                        optionType: OptionType.CE,
                        tradingSymbol: $"{pair.strike}CE", // temporary symbol if not available
                        token: pair.C_token,
                        strike: pair.strike,
                        expiry: pair.expiry,
                        ltp: pair.C_ltp,
                        bid: pair.C_bid,
                        ask: pair.C_ask,
                        oi: pair.C_oi
                        );

                    var callGreeks = new OptionGreeks(callSnap, _indexSpot, _impliedFuture, _riskFreeRate, tte, _volSurface, _greeksCalculator);                     
                    
                    // Put snapshot + greeks
                    var putSnap = new OptionSnapshot(
                        optionType: OptionType.PE,
                        tradingSymbol: $"{pair.strike}PE",
                        token: pair.P_token,
                        strike: pair.strike,
                        expiry: pair.expiry,
                        ltp: pair.P_ltp,
                        bid: pair.P_bid,
                        ask: pair.P_ask,
                        oi: pair.P_oi);
                    var putGreeks = new OptionGreeks(putSnap, _indexSpot, _impliedFuture, _riskFreeRate, tte, _volSurface, _greeksCalculator);

                    optionSnapshots.Add(callSnap);
                    optionSnapshots.Add(putSnap);
                    optionGreeks.Add(callGreeks);
                    optionGreeks.Add(putGreeks);
                }
            }

            // === Rebuild futures ===
            var futureElements = new List<FutureElement>();
            if (dto.Futures != null)
            {
                foreach (var f in dto.Futures)
                {
                    var snap = f.FutureSnapshot;
                    var fut = new Future(snap.TradingSymbol, snap.Token, snap.Expiry, dto.SnapTime, new RFR(dto.RiskFreeRate), snap.LTP, snap.Bid, snap.Ask, snap.OI);
                    var index = new Index(dto.Index, 0, dto.Spot, _calendar, new RFR(dto.RiskFreeRate), dto.DivYield, dto.Expiry, dto.SnapTime);
                    var futElem = new FutureElement(fut, index, _volSurface, new RFR(dto.RiskFreeRate), dto.SnapTime, _greeksCalculator);
                    futureElements.Add(futElem);
                }
            }

            // === Convert to Immutable and build internal stores ===
            var (byToken, bySymbol, byStrike) =
                BuildOptionStores(optionSnapshots.ToImmutableArray(), optionGreeks.ToImmutableArray());
            _optionsByToken = byToken;
            _optionsByTradingSymbol = bySymbol;
            _optionChainElements = byStrike;

            var (futuresByToken, futuresByTradingSymbol) =
                BuildFutureStores(futureElements.ToImmutableArray());
            _futuresByToken = futuresByToken;
            _futuresByTradingSymbol = futuresByTradingSymbol;

            // === Token placeholder ===
            _token = dto.OptionPairs != null && dto.OptionPairs.Length > 0
                ? dto.OptionPairs[0].C_token
                : 0;
        }
    }

    // === Supporting Structs / DTOs ===

    
    public readonly struct OptionPair
    {
        public OptionSnapshot Call { get; }
        public OptionSnapshot Put { get; }
        public OptionGreeks CallGreeks { get; }
        public OptionGreeks PutGreeks { get; }
        public OptionSpreads CallSpreads { get; }
        public OptionSpreads PutSpreads { get; }

        public OptionPair(OptionSnapshot call, OptionSnapshot put, OptionGreeks callGreeks, OptionGreeks putGreeks)
        {
            Call = call;
            Put = put;
            CallGreeks = callGreeks;
            PutGreeks = putGreeks;
            CallSpreads = new OptionSpreads(call, callGreeks);
            PutSpreads = new OptionSpreads(put, putGreeks);
        }
    }


    public sealed class AtomicMarketSnapDTO
    {
        public string Index { get; set; } = string.Empty;
        public double Spot { get; set; }
        public double ImpliedFuture { get; set; }
        public double RiskFreeRate { get; set; }
        public double DivYield { get; set; }
        public DateTime Expiry { get; set; }
        public DateTime SnapTime { get; set; }

        public OptionPairDTO[] OptionPairs { get; set; } = Array.Empty<OptionPairDTO>();
        public FutureDetailDTO[]? Futures { get; set; }
        public VolSurfaceDTO? VolSurface { get; set; }
        public ForwardCurveDTO? ForwardCurve { get; set; }
    }

    public sealed class OptionPairDTO
    {
        public double strike { get; set; }
        public DateTime expiry { get; set; }
        public long timestampUnixMs { get; set; }

        // Call
        public uint C_token { get; set; }
        public double C_bid { get; set; }
        public double C_ask { get; set; }
        public double C_ltp { get; set; }
        public double C_oi { get; set; }
        public double C_iv { get; set; }
        public double C_delta { get; set; }
        public double C_gamma { get; set; }
        public double C_vega { get; set; }
        public double C_theta { get; set; }
        public double C_rho { get; set; }
        public double C_npv { get; set; }
        public double C_bidSpread { get; set; }
        public double C_askSpread { get; set; }

        // Put
        public uint P_token { get; set; }
        public double P_bid { get; set; }
        public double P_ask { get; set; }
        public double P_ltp { get; set; }
        public double P_oi { get; set; }
        public double P_iv { get; set; }
        public double P_delta { get; set; }
        public double P_gamma { get; set; }
        public double P_vega { get; set; }
        public double P_theta { get; set; }
        public double P_rho { get; set; }
        public double P_npv { get; set; }
        public double P_bidSpread { get; set; }
        public double P_askSpread { get; set; }
    }

    public sealed class FutureDetailDTO
    {
        public FutureSnapshot FutureSnapshot { get; }
        public FutureGreeks FutureGreeks { get; }
        public FutureSpreads FutureSpreads { get; }

        public FutureDetailDTO(FutureSnapshot snapshot, FutureGreeks greeks, FutureSpreads spreads)
        {
            FutureSnapshot = snapshot;
            FutureGreeks = greeks;
            FutureSpreads = spreads;
        }
    }

   
    /// <summary>
    /// Serializable representation of a ForwardCurve for persistence or data transfer.
    /// Contains knot times (years), implied rates, spot, div yield, and interpolation mode.
    /// </summary>
    public sealed class ForwardCurveDTO
    {
        public double Spot { get; set; }
        public double DivYield { get; set; }
        public double[] KnotTimes { get; set; } = Array.Empty<double>();
        public double[] ImpliedRates { get; set; } = Array.Empty<double>();
        public string Interpolation { get; set; } = nameof(InterpolationMethod.Spline);

        public ForwardCurveDTO() { }

        public ForwardCurveDTO(
            double spot,
            double divYield,
            double[] knotTimes,
            double[] impliedRates,
            InterpolationMethod interpolation)
        {
            Spot = spot;
            DivYield = divYield;
            KnotTimes = knotTimes ?? Array.Empty<double>();
            ImpliedRates = impliedRates ?? Array.Empty<double>();
            Interpolation = interpolation.ToString();
        }

        /// <summary>
        /// Factory from ForwardCurve object.
        /// </summary>
        public static ForwardCurveDTO FromForwardCurve(ForwardCurve curve)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));

            return new ForwardCurveDTO(
                curve.Spot,
                curve.DivYield,
                curve.KnotTimes.ToArray(),
                curve.ImpliedRates.ToArray(),
                curve.Interpolation);
        }

        /// <summary>
        /// Rebuilds a ForwardCurve object from this DTO.
        /// </summary>
        public ForwardCurve ToForwardCurve()
        {
            // Try parse interpolation mode safely
            InterpolationMethod method = InterpolationMethod.Spline;
            if (!Enum.TryParse(Interpolation, true, out method))
                method = InterpolationMethod.Spline;

            return new ForwardCurve(
                spot: Spot,
                divYield: DivYield,
                knotTimes: KnotTimes ?? Array.Empty<double>(),
                impliedRates: ImpliedRates ?? Array.Empty<double>(),
                interpolation: method);
        }
    }

}