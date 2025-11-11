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
        public event Action<AtomicMarketSnapDTO>? OnMarketDataUpdated;
        private Task? _backgroundUpdaterTask; // Add this field
        //private readonly CancellationTokenSource _cts = new();
        private readonly CancellationToken _token;   // ✅ externally provided token
        private DateTime _initializationTime;
        private readonly Index _index;
        private readonly RFR _rfr;
        private readonly double _OICutoff;
        private readonly Dictionary<uint, Option> _optionsByToken;
        private readonly Dictionary<string, Option> _optionsByTradingSymbol;
        private readonly Dictionary<uint, Future> _futuresByToken;
        private readonly Dictionary<string, Future> _futuresByTradingSymbol;
        private readonly Dictionary<double, OptionChainElement> _optionChainByStrike;
        private readonly Dictionary<uint, FutureElement> _futureElements;
        private ForwardCurve? _forwardCurve;
        private readonly object _snapshotLock = new();
        private AtomicMarketSnap _atomicSnapshot = null!;
        private readonly IGreeksCalculator _greeksCalculator;
        private readonly VolatilityModel _volatilityModel;
        // Price update history tracking
        private readonly Dictionary<uint, Stack<PriceUpdate>> _priceUpdateHistory = new();
        private readonly object _priceUpdateLock = new();

        public AtomicMarketSnap AtomicSnapshot => _atomicSnapshot;

        public MarketData(DateTime now, Index index, RFR rfr, double OICutoff, IEnumerable<Option> options, IEnumerable<Future> futures, VolatilityModel volatilityModel, CancellationToken token)
        {
            if (now == default)
                throw new ArgumentException("Invalid initialization time", nameof(now));
            if (options == null || !options.Any())
                throw new ArgumentException("Options collection cannot be null or empty", nameof(options));
            if (futures == null || !futures.Any())
                throw new ArgumentException("Futures collection cannot be null or empty", nameof(futures));
            ArgumentNullException.ThrowIfNull(index);

            _index = index ?? throw new ArgumentNullException(nameof(index));
            _rfr = rfr ?? throw new ArgumentNullException(nameof(rfr));
            _initializationTime = now;
            _volatilityModel = volatilityModel;
            _optionsByToken = new Dictionary<uint, Option>();
            _optionsByTradingSymbol = new Dictionary<string, Option>();
            _futuresByToken = new Dictionary<uint, Future>();
            _futuresByTradingSymbol = new Dictionary<string, Future>();
            _futureElements = new Dictionary<uint, FutureElement>();
            _OICutoff = OICutoff;
            _token = token;   // ✅ store external token

            var optionsList = options?.ToList() ?? throw new ArgumentNullException(nameof(options));
            var futuresList = futures?.ToList() ?? throw new ArgumentNullException(nameof(futures));

            foreach (var option in optionsList)
            {
                if (_optionsByToken.ContainsKey(option.Token))
                    throw new ArgumentException($"Duplicate option token: {option.Token}");

                if (_optionsByTradingSymbol.ContainsKey(option.TradingSymbol))
                    throw new ArgumentException($"Duplicate trading symbol: {option.TradingSymbol}");

                _optionsByToken.Add(option.Token, option);
                _optionsByTradingSymbol.Add(option.TradingSymbol, option);
            }

            if (volatilityModel == VolatilityModel.Black76)
            {
                _greeksCalculator = Black76GreeksCalculator.Instance;
            }
            else
            {
                throw new ArgumentException($"Volatility model '{volatilityModel}' is not supported yet.");
            }

            // === Create futures and elements first ===
            foreach (var future in futuresList)
            {
                if (_futuresByToken.ContainsKey(future.Token))
                    throw new ArgumentException($"Duplicate future token: {future.Token}");

                if (_futuresByTradingSymbol.ContainsKey(future.TradingSymbol))
                    throw new ArgumentException($"Duplicate trading symbol: {future.TradingSymbol}");

                _futuresByToken.Add(future.Token, future);
                _futuresByTradingSymbol.Add(future.TradingSymbol, future);
                _futureElements.Add(future.Token, new FutureElement(future, _index, null, _rfr, now, _greeksCalculator));
            }

            // === Build ForwardCurve once ===
            ForwardCurve? forwardCurve = null;
            try
            {
                var futureDetails = _futureElements.Values
                    .Select(fe => new FutureDetailDTO(
                        fe.Future.GetSnapshot(), fe.FutureGreeks, fe.FutureSpreads))
                    .ToList();

                if (futureDetails.Count > 0)
                {
                    forwardCurve = ForwardCurve.BuildFromFutureDetails(
                        spot: _index.GetSnapshot().IndexSpot,
                        divYield: _index.GetSnapshot().DivYield,
                        futureDetails: futureDetails,
                        calendar: _index.Calendar,
                        snapshotTime: _initializationTime,
                        interpolation: InterpolationMethod.Spline);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to build ForwardCurve: {ex.Message}");
                forwardCurve = null;
            }

            _forwardCurve = forwardCurve;

            IParametricModelSurface volSurface = CreateVolatilitySurface(OICutoff, _forwardCurve);

            

            _optionChainByStrike = ValidateAndCreateOptionChain(optionsList, now, volSurface, _forwardCurve, _greeksCalculator)
            .ToDictionary(
            element => element.Strike,
            element => element
            );

            UpdateAtomicSnapshot(volSurface, _forwardCurve);

            // Start background updater to process price updates
            StartBackgroundUpdater();

        }

        private IParametricModelSurface CreateVolatilitySurface(double OICutoff, ForwardCurve? forwardCurve = null)
        {
            var indexSnapshot = _index.GetSnapshot();
            var expiry = _optionsByToken.Values.First().Expiry;
            double timeToExpiry = _index.Calendar.GetYearFraction(_initializationTime, expiry);

            var optionSnapshots = _optionsByToken.Values
                .Select(o => o.GetSnapshot())
                .ToImmutableArray();

            var callData = optionSnapshots
                .Where(s => s.OptionType == OptionType.CE)
                .Select(s => (s.Strike, Price: s.Mid, s.OI))
                .ToList();

            var putData = optionSnapshots
                .Where(s => s.OptionType == OptionType.PE)
                .Select(s => (s.Strike, Price: s.Mid, s.OI))
                .ToList();

            double forwardPrice = forwardCurve != null
                ? forwardCurve.GetForwardPrice(timeToExpiry)
                : throw new InvalidOperationException("ForwardCurve is required to create volatility surface.");

            IParametricModelSurface volSurface;
            switch (_volatilityModel)
            {
                case VolatilityModel.Black76:
                    volSurface = new Black76VolSurface(
                        callData,
                        putData,
                        forwardPrice, // forwardPrice
                        _rfr.Value,                  // riskFreeRate
                        timeToExpiry,
                        OICutoff);                     // OICutoff
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_volatilityModel), $"Volatility model {_volatilityModel} not supported.");
            }
            return volSurface;
        }

        private void UpdateAtomicSnapshot(IParametricModelSurface volSurface, ForwardCurve? forwardCurve = null)
        {
            lock (_snapshotLock)
            {
                var indexSnapshot = _index.GetSnapshot();
                var expiry = _optionsByToken.Values.First().Expiry; // Get expiry once

                // Get fresh option snapshots
                var optionSnapshots = _optionsByToken.Values
                    .Select(o => o.GetSnapshot())
                    .ToImmutableArray();

                // Reuse greeks from existing chain elements
                var greeks = _optionChainByStrike.Values
                    .SelectMany(ce => new[]
                    {
                        ce.CallGreeks,
                        ce.PutGreeks
                    })
                    .ToImmutableArray();

                // Get fresh future elements
                var futureElements = _futureElements.Values
                    .ToImmutableArray();

                
                // Use forward curve to derive forwardPrice (for current expiry)
                double forwardPrice = forwardCurve != null
                    ? forwardCurve.GetForwardPrice(
                        _index.Calendar.GetYearFraction(_initializationTime, expiry))
                    : throw new InvalidOperationException("ForwardCurve is required to get forward price.");

                // Create the atomic snapshot and include the forwardCurve (may be null)
                _atomicSnapshot = new AtomicMarketSnap(
                    _initializationTime,
                    expiry, // Use variable
                    indexSnapshot.IndexSpot,
                    forwardPrice,
                    indexSnapshot.Token,
                    _rfr.Value,
                    indexSnapshot.DivYield,
                    optionSnapshots,
                    greeks,
                    futureElements, // New parameter
                    _index.Calendar,
                    _index.TradingSymbol,
                    forwardCurve,   // <-- pass forwardCurve here
                    volSurface);
            }
        }

        private List<OptionChainElement> ValidateAndCreateOptionChain(List<Option> options, DateTime now, IParametricModelSurface volSurface, ForwardCurve forwardCurve, IGreeksCalculator greeksCalculator)
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

            return strikeGroups
                .OrderBy(g => g.Key)
                .Select(g => new OptionChainElement(
                    g.Value.Call ?? throw new InvalidOperationException($"Missing call option at strike {g.Key}"),
                    g.Value.Put ?? throw new InvalidOperationException($"Missing put option at strike {g.Key}"),
                    _index, forwardCurve,
                    volSurface,
                    _rfr,
                    now,
                    greeksCalculator))
                .ToList();
        }

        public void HandlePriceUpdate(PriceUpdate update)
        {
            // Validate token exists
            bool tokenExists = _optionsByToken.ContainsKey(update.Token) ||
                            _futuresByToken.ContainsKey(update.Token) ||
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
                                rfr: _rfr
                            );
                        }
                    });

                    // Step 5: Parallel future updates
                    Parallel.ForEach(_futuresByToken, futureKvp =>
                    {
                        uint token = futureKvp.Key;
                        Future future = futureKvp.Value;
                        var currentSnapshot = future.GetSnapshot();

                        if (latestUpdates.TryGetValue(token, out var priceUpdate))
                        {
                            future.UpdateMarketData(
                                ltp: priceUpdate.NewLtp,
                                bid: priceUpdate.NewBid ?? currentSnapshot.Bid,
                                ask: priceUpdate.NewAsk ?? currentSnapshot.Ask,
                                oi: priceUpdate.NewOI ?? currentSnapshot.OI,
                                rfr: _rfr
                            );
                        }
                        else
                        {
                            future.UpdateMarketData(
                                ltp: currentSnapshot.LTP,
                                bid: currentSnapshot.Bid,
                                ask: currentSnapshot.Ask,
                                oi: currentSnapshot.OI,
                                rfr: _rfr
                            );
                        }
                    });

                    // === Build ForwardCurve once ===
                    ForwardCurve? forwardCurve = null;
                    try
                    {
                        var futureDetails = _futureElements.Values
                            .Select(fe => new FutureDetailDTO(
                                fe.Future.GetSnapshot(), fe.FutureGreeks, fe.FutureSpreads))
                            .ToList();

                        if (futureDetails.Count > 0)
                        {
                            forwardCurve = ForwardCurve.BuildFromFutureDetails(
                                spot: _index.GetSnapshot().IndexSpot,
                                divYield: _index.GetSnapshot().DivYield,
                                futureDetails: futureDetails,
                                calendar: _index.Calendar,
                                snapshotTime: _initializationTime,
                                interpolation: InterpolationMethod.Spline);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to build ForwardCurve: {ex.Message}");
                        forwardCurve = null;
                    }

                    _forwardCurve = forwardCurve;

                    // Step 6: Create Volatility Surface
                    IParametricModelSurface volSurface = CreateVolatilitySurface(_OICutoff);

                    // Step 7: Parallel Greek updates for options
                    Parallel.ForEach(_optionChainByStrike.Values, chainElement =>
                    {
                        chainElement.UpdateGreeks(
                            index: _index,  forwardCurve: _forwardCurve!,
                            volSurface: volSurface,
                            rfr: _rfr,
                            now: now
                        );
                    });

                    // Step 8: Parallel Greek updates for futures
                    Parallel.ForEach(_futureElements.Values, futureElement =>
                    {
                        futureElement.UpdateGreeks(
                            index: _index,
                            volSurface: volSurface,
                            rfr: _rfr,
                            now: now,
                            greeksCalculator: _greeksCalculator
                        );
                    });

                    // Step 9: Final snapshot update
                    UpdateAtomicSnapshot(volSurface);

                    // Step 10: Raise event with a thread-safe snapshot reference
                    OnMarketDataUpdated?.Invoke(_atomicSnapshot.ToDTO());
                }
            }
        }

        // ✅ Start background updater (it will respect external cancellation)
        private void StartBackgroundUpdater()
        {
            _backgroundUpdaterTask = Task.Run(async () =>
            {
                while (!_token.IsCancellationRequested)
                {
                    try
                    {
                        ProcessPendingUpdates();
                        await Task.Delay(500, _token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Background updater error: {ex.Message}");
                    }
                }

                Console.WriteLine("🟡 MarketData background updater stopped gracefully.");
            }, _token);
        }

        // ✅ Add this async shutdown method
        public async Task StopAsync()
        {
            try
            {
                // Wait for the background task to finish if still running
                if (_backgroundUpdaterTask != null)
                {
                    await _backgroundUpdaterTask;
                }
            }
            catch (TaskCanceledException)
            {
                // Expected on cancellation — ignore
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during MarketData shutdown: {ex.Message}");
            }
        }
    }
}