using System.Collections.Concurrent;
using MarketData;
using QuantitativeAnalytics;
using InstrumentStatic;

namespace RiskGen
{
    // ---------------------------------------------------------------
    // Raw GUI Inputs (first version)
    // ---------------------------------------------------------------
    public sealed record OptionInput(
        string TradingSymbol,
        OptionType OptionType,
        double Strike,
        DateTime Expiry,
        int Lots
    );

    public sealed record FutureInput(
        string TradingSymbol,
        DateTime Expiry,
        int Lots
    );

    // ---------------------------------------------------------------
    // Scenario Orchestrator / Manager (Singleton)
    // ---------------------------------------------------------------    
    public sealed class ScenarioOrchestrator : IDisposable
    {
        private static readonly Lazy<ScenarioOrchestrator> _instance =
            new(() => new ScenarioOrchestrator());

        public static ScenarioOrchestrator Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, Scenario> _scenarios = new();

        // 0 = stopped, 1 = running
        private int _running = 0;

        private bool IsRunning => Volatile.Read(ref _running) == 1;

        private ScenarioOrchestrator() { }

        // -----------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------
        public void Start()
        {
            Interlocked.Exchange(ref _running, 1);
        }

        public void Shutdown()
        {
            Interlocked.Exchange(ref _running, 0);
            _scenarios.Clear();
        }

        public void Dispose() => Shutdown();

        // -----------------------------------------------------------
        // Market updates
        // -----------------------------------------------------------
        public void OnMarketUpdate(AtomicMarketSnap snap)
        {
            if (!IsRunning || snap == null)
                return;

            foreach (var scenario in _scenarios.Values)
            {
                scenario.CalculateGreeks(snap);
            }
        }

        // -----------------------------------------------------------
        // Scenario creation
        // -----------------------------------------------------------
        public Scenario CreateScenario(
            string scenarioName,
            IEnumerable<OptionInput> optionInputs,
            IEnumerable<FutureInput> futureInputs,
            AtomicMarketSnap snap)
        {
            if (!IsRunning)
                throw new InvalidOperationException("ScenarioOrchestrator is not running.");

            if (string.IsNullOrWhiteSpace(scenarioName))
                throw new ArgumentException("Scenario name cannot be empty.", nameof(scenarioName));

            if (snap == null)
                throw new ArgumentNullException(nameof(snap));

            var trades = new List<Trade>();

            // Options
            foreach (var o in optionInputs)
            {
                if (!snap.OptionsByTradingSymbol.TryGetValue(o.TradingSymbol, out var optSnap))
                    throw new InvalidOperationException($"Option not found: {o.TradingSymbol}");

                var inst = new Instrument(
                    tradingSymbol: o.TradingSymbol,
                    lotSize: optSnap.LotSize,
                    productType: ProductType.Option,
                    strike: o.Strike,
                    optionType: o.OptionType,
                    expiry: o.Expiry
                );

                trades.Add(new Trade(inst, o.Lots));
            }

            // Futures
            foreach (var f in futureInputs)
            {
                if (!snap.FuturesByTradingSymbol.TryGetValue(f.TradingSymbol, out var fut))
                    throw new InvalidOperationException($"Future not found: {f.TradingSymbol}");

                var inst = new Instrument(
                    tradingSymbol: f.TradingSymbol,
                    lotSize: fut.FutureSnapshot.LotSize,
                    productType: ProductType.Future,
                    strike: null,
                    optionType: null,
                    expiry: f.Expiry
                );

                trades.Add(new Trade(inst, f.Lots));
            }

            var scenario = new Scenario(trades, snap);
            _scenarios[scenarioName] = scenario;

            return scenario;
        }

        // -----------------------------------------------------------
        // Accessors
        // -----------------------------------------------------------
        public bool DeleteScenario(string scenarioName)
            => _scenarios.TryRemove(scenarioName, out _);

        public Scenario? GetScenario(string scenarioName)
            => _scenarios.TryGetValue(scenarioName, out var s) ? s : null;

        public IReadOnlyDictionary<string, Scenario> GetAllScenarios()
            => _scenarios;
    }
}