using System.Collections.Immutable;
using CalendarLib;
using QuantitativeAnalytics;
using InstrumentStatic;

namespace MarketData
{
    public sealed class AtomicMarketSnap
    {
        // Core market data
        private readonly DateTime _initializationTime;
        private readonly DateTime _expiry;
        private readonly double _indexSpot;
        private readonly double _impliedFuture;
        private readonly double _riskFreeRate;
        private readonly VolSurface _volSurface;
        private readonly MarketCalendar _calendar;

        // Option data stores
        private readonly ImmutableDictionary<uint, OptionSnapshot> _optionsByToken;
        private readonly ImmutableDictionary<string, OptionSnapshot> _optionsByTradingSymbol;
        private readonly ImmutableDictionary<double, OptionPair> _optionChainElements;

        public AtomicMarketSnap(
            DateTime initializationTime,
            DateTime expiry,
            double indexSpot,
            double impliedFuture,
            double riskFreeRate,
            ImmutableArray<OptionSnapshot> optionSnapshots,
            MarketCalendar calendar)
        {
            // Validate core market data
            if (initializationTime == default)
                throw new ArgumentException("Invalid initialization time", nameof(initializationTime));
            if (expiry <= initializationTime)
                throw new ArgumentException("Expiry must be after initialization time", nameof(expiry));
            if (indexSpot <= 0 || impliedFuture <= 0)
                throw new ArgumentException("Index spot and implied future must be positive");
            if (riskFreeRate < 0)
                throw new ArgumentException("Risk-free rate cannot be negative");
            if (indexSpot > impliedFuture)
                throw new ArgumentException("Index spot cannot exceed implied future");
            if (calendar == null)
                throw new ArgumentNullException(nameof(calendar));

            // Validate snapshots
            if (optionSnapshots.IsDefault)
                throw new ArgumentNullException(nameof(optionSnapshots));
            if (optionSnapshots.IsEmpty)
                throw new ArgumentException("At least one option snapshot required");

            // Store core data
            _initializationTime = initializationTime;
            _expiry = expiry;
            _indexSpot = indexSpot;
            _impliedFuture = impliedFuture;
            _riskFreeRate = riskFreeRate;
            _calendar = calendar;

            // Build immutable stores
            var (byToken, bySymbol, byStrike) = BuildOptionStores(optionSnapshots);
            _optionsByToken = byToken;
            _optionsByTradingSymbol = bySymbol;
            _optionChainElements = byStrike;

            // Construct VolSurface using calendar-aware year fraction
            _volSurface = CreateVolSurface(optionSnapshots);
        }

        private VolSurface CreateVolSurface(ImmutableArray<OptionSnapshot> snapshots)
        {
            double timeToExpiry = _calendar.GetYearFraction(_initializationTime, _expiry);

            var callData = snapshots
                .Where(s => s.OptionType == OptionType.CE)
                .Select(s => (s.Strike, s.IV, s.OI))
                .ToList();

            var putData = snapshots
                .Where(s => s.OptionType == OptionType.PE)
                .Select(s => (s.Strike, s.IV, s.OI))
                .ToList();

            return new VolSurface(
                callData,
                putData,
                forwardPrice: _impliedFuture,
                riskFreeRate: _riskFreeRate,
                timeToExpiry: timeToExpiry);
        }

        // Core market properties
        public DateTime InitializationTime => _initializationTime;
        public DateTime Expiry => _expiry;
        public double IndexSpot => _indexSpot;
        public double ImpliedFuture => _impliedFuture;
        public double RiskFreeRate => _riskFreeRate;
        public VolSurface VolSurface => _volSurface;

        // Option accessors
        public ImmutableDictionary<uint, OptionSnapshot> OptionsByToken => _optionsByToken;
        public ImmutableDictionary<string, OptionSnapshot> OptionsByTradingSymbol => _optionsByTradingSymbol;
        public ImmutableDictionary<double, OptionPair> OptionChainElements => _optionChainElements;

        private static (
            ImmutableDictionary<uint, OptionSnapshot>,
            ImmutableDictionary<string, OptionSnapshot>,
            ImmutableDictionary<double, OptionPair>)
            BuildOptionStores(ImmutableArray<OptionSnapshot> snapshots)
        {
            var byTokenBuilder = ImmutableDictionary.CreateBuilder<uint, OptionSnapshot>();
            var bySymbolBuilder = ImmutableDictionary.CreateBuilder<string, OptionSnapshot>();
            var byStrikeBuilder = ImmutableDictionary.CreateBuilder<double, OptionPair>();

            // First pass - build token and symbol dictionaries
            foreach (var snapshot in snapshots)
            {
                if (byTokenBuilder.ContainsKey(snapshot.Token))
                    throw new ArgumentException($"Duplicate option token: {snapshot.Token}");
                if (bySymbolBuilder.ContainsKey(snapshot.TradingSymbol))
                    throw new ArgumentException($"Duplicate trading symbol: {snapshot.TradingSymbol}");

                byTokenBuilder.Add(snapshot.Token, snapshot);
                bySymbolBuilder.Add(snapshot.TradingSymbol, snapshot);
            }

            // Second pass - build strike dictionary
            var strikeGroups = snapshots.GroupBy(s => s.Strike);
            foreach (var group in strikeGroups)
            {
                OptionSnapshot? call = null, put = null;
                foreach (var snapshot in group)
                {
                    if (snapshot.OptionType == OptionType.CE) call = snapshot;
                    if (snapshot.OptionType == OptionType.PE) put = snapshot;
                    if (call != null && put != null) break;
                }

                if (call == null || put == null)
                    throw new ArgumentException($"Incomplete option pair at strike {group.Key}");

                byStrikeBuilder.Add(group.Key, new OptionPair(call.Value, put.Value));
            }

            return (
                byTokenBuilder.ToImmutable(),
                bySymbolBuilder.ToImmutable(),
                byStrikeBuilder.ToImmutable()
            );
        }
    }

    public readonly struct OptionPair
    {
        public OptionSnapshot Call { get; }
        public OptionSnapshot Put { get; }
        public OptionPair(OptionSnapshot call, OptionSnapshot put) => (Call, Put) = (call, put);
    }
}