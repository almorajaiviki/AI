using System.Collections.Immutable;
using QuantitativeAnalytics;
using InstrumentStatic;

namespace MarketData
{

    public readonly struct PriceUpdate
    {
        public readonly uint Token;
        public readonly double NewLtp;
        public readonly double? NewBid;
        public readonly double? NewAsk;
        public readonly double? NewOI;

        public PriceUpdate(
            uint token,
            double newLtp,
            double? newBid,
            double? newAsk,
            double? newOI)
        {
            Token = token;
            NewLtp = newLtp;
            NewBid = newBid;
            NewAsk = newAsk;
            NewOI = newOI;
        }
    }

    public sealed class MarketData
    {
        private DateTime _initializationTime;
        private readonly Index _index;
        private readonly RFR _rfr;
        private readonly Dictionary<uint, Option> _optionsByToken;
        private readonly Dictionary<string, Option> _optionsByTradingSymbol;
        private readonly Dictionary<double, OptionChainElement> _optionChainByStrike;
        private readonly object _snapshotLock = new();
        private AtomicMarketSnap _atomicSnapshot = null!;
        // Price update history tracking
        private readonly Dictionary<uint, Stack<PriceUpdate>> _priceUpdateHistory = new();
        private readonly object _priceUpdateLock = new();

        public AtomicMarketSnap AtomicSnapshot => _atomicSnapshot;

        public MarketData(DateTime now, Index index, RFR rfr, IEnumerable<Option> options)
        {
            if (now == default) 
                throw new ArgumentException("Invalid initialization time", nameof(now));
            
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _rfr = rfr ?? throw new ArgumentNullException(nameof(rfr));
            _initializationTime =now;
            
            _optionsByToken = new Dictionary<uint, Option>();
            _optionsByTradingSymbol = new Dictionary<string, Option>();
            
            var optionsList = options?.ToList() ?? throw new ArgumentNullException(nameof(options));
            
            foreach (var option in optionsList)
            {
                if (_optionsByToken.ContainsKey(option.Token))
                    throw new ArgumentException($"Duplicate option token: {option.Token}");
                
                if (_optionsByTradingSymbol.ContainsKey(option.TradingSymbol))
                    throw new ArgumentException($"Duplicate trading symbol: {option.TradingSymbol}");

                _optionsByToken.Add(option.Token, option);
                _optionsByTradingSymbol.Add(option.TradingSymbol, option);
            }

            _optionChainByStrike = ValidateAndCreateOptionChain(optionsList, now)
            .ToDictionary(
            element => element.Strike,  // Changed from CallOption.Strike to Strike
            element => element
        );

            UpdateAtomicSnapshot();
        }

        private void UpdateAtomicSnapshot()
        {
            lock (_snapshotLock)
            {
                var indexSnapshot = _index.GetSnapshot();
                var optionSnapshots = _optionsByToken.Values
                    .Select(o => o.GetSnapshot())
                    .ToImmutableArray();

                _atomicSnapshot = new AtomicMarketSnap(
                    _initializationTime,
                    _optionsByToken.Values.First().Expiry,
                    indexSnapshot.IndexSpot,
                    indexSnapshot.ImpliedFuture,
                    _rfr.Value,
                    optionSnapshots,
                    _index.Calendar);
            }
        }

        private List<OptionChainElement> ValidateAndCreateOptionChain(List<Option> options, DateTime now)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!options.Any()) throw new ArgumentException("Options collection cannot be empty");
            if (options.Count % 2 != 0) throw new ArgumentException("Must have even number of options (call/put pairs)");

            var firstExpiry = options[0].Expiry;
            if (options.Any(o => o.Expiry != firstExpiry))
                throw new ArgumentException("All options must have the same expiry");

            var strikeGroups = options
                .GroupBy(o => o.Strike)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Call: g.SingleOrDefault(o => o.OptionType == OptionType.CE),
                        Put: g.SingleOrDefault(o => o.OptionType == OptionType.PE)
                    ));

            var callData = new List<(double strike, double IV, double OI)>();
            var putData = new List<(double strike, double IV, double OI)>();
            
            foreach (var group in strikeGroups)
            {
                if (group.Value.Call == null || group.Value.Put == null)
                    throw new ArgumentException($"Incomplete option pair at strike {group.Key}");

                var callSnapshot = group.Value.Call.GetSnapshot();
                var putSnapshot = group.Value.Put.GetSnapshot();
                
                callData.Add((group.Key, callSnapshot.IV, callSnapshot.OI));
                putData.Add((group.Key, putSnapshot.IV, putSnapshot.OI));
            }

            var indexSnapshot = _index.GetSnapshot();
            var volSurface = new VolSurface(
                callData,
                putData,
                indexSnapshot.ImpliedFuture,
                _rfr.Value,
                _index.Calendar.GetYearFraction(_initializationTime, firstExpiry),
                OICutoff: 500000);

            return strikeGroups
                .OrderBy(g => g.Key)
                .Select(g => new OptionChainElement(
                    g.Value.Call ?? throw new InvalidOperationException($"Missing call option at strike {g.Key}"),
                    g.Value.Put ?? throw new InvalidOperationException($"Missing put option at strike {g.Key}"),
                    _index,
                    volSurface, _rfr,
                    now))
                .ToList();
        }

        public void HandlePriceUpdate(PriceUpdate update)        
        {
            // Validate token exists
            bool tokenExists = _optionsByToken.ContainsKey(update.Token) || 
                            (_index.Token == update.Token);
            
            if (!tokenExists)
            {
                throw new ArgumentException($"Token {update.Token} not found in market data");
            }

            lock (_priceUpdateLock)
            {
                if (!_priceUpdateHistory.TryGetValue(update.Token, out var stack))
                {
                    stack = new Stack<PriceUpdate>();
                    _priceUpdateHistory[update.Token] = stack;
                }
                stack.Push(update);
            }
        }
        
        public void ProcessPendingUpdates()
        {
            Dictionary<uint, PriceUpdate> latestUpdates = new Dictionary<uint, PriceUpdate>();
            DateTime now = DateTime.Now;

            // Step 1: Collect latest updates (must remain sequential)
            lock (_priceUpdateLock)
            {
                foreach (var kvp in _priceUpdateHistory)
                {
                    uint token = kvp.Key;
                    Stack<PriceUpdate> stack = kvp.Value;
                    
                    if (stack.Count > 0)
                    {
                        latestUpdates[token] = stack.Pop();
                        stack.Clear();
                    }
                }
            }

            if (latestUpdates.Count > 0)
            {
                lock (_snapshotLock)
                {
                    // Step 2: Update initialization time
                    _initializationTime = now;

                    // Step 3: Update index if its token matches
                    if (latestUpdates.TryGetValue(_index.Token, out var indexUpdate))
                    {
                        _index.UpdateSpot(
                            newSpot: indexUpdate.NewLtp,
                            rfr: _rfr,
                            now: now
                        );
                    }

                    // Step 4: Parallel option updates
                    Parallel.ForEach(_optionsByToken, optionKvp =>
                    {
                        uint token = optionKvp.Key;
                        Option option = optionKvp.Value;
                        var currentSnapshot = option.GetSnapshot();

                        if (latestUpdates.TryGetValue(token, out var priceUpdate))
                        {
                            option.UpdateMarketData(
                                ltp: priceUpdate.NewLtp,
                                bid: priceUpdate.NewBid ?? currentSnapshot.Bid,
                                ask: priceUpdate.NewAsk ?? currentSnapshot.Ask,
                                oi: priceUpdate.NewOI ?? currentSnapshot.OI,
                                now: now,
                                index: _index,
                                rfr: _rfr
                            );
                        }
                        else
                        {
                            option.UpdateMarketData(
                                ltp: currentSnapshot.LTP,
                                bid: currentSnapshot.Bid,
                                ask: currentSnapshot.Ask,
                                oi: currentSnapshot.OI,
                                now: now,
                                index: _index,
                                rfr: _rfr
                            );
                        }
                    });

                    // Step 5: Final snapshot update
                    UpdateAtomicSnapshot();

                    // Step 6: Parallel Greek updates
                    Parallel.ForEach(_optionChainByStrike.Values, chainElement =>
                    {
                        chainElement.UpdateGreeks(
                            index: _index,
                            volSurface: _atomicSnapshot.VolSurface,
                            rfr: _rfr,
                            now: now
                        );
                    });
                }
            }
        }
        
    }
}