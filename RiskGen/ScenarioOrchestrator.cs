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
        private bool _running = false;

        private ScenarioOrchestrator() { }

        // -----------------------------------------------------------
        // Start orchestrator
        // -----------------------------------------------------------
        public void Start()
        {
            _running = true;
        }

        // -----------------------------------------------------------
        // Called by market data pipeline when new AtomicMarketSnap arrives
        // -----------------------------------------------------------
        public void OnMarketUpdate(AtomicMarketSnap snap)
        {
            if (!_running) return;
            if (snap == null) return;

            foreach (var scenario in _scenarios.Values)
            {
                scenario.Recalculate(snap);
            }
        }

        // -----------------------------------------------------------
        // Create scenario from raw GUI inputs
        // -----------------------------------------------------------
        public Scenario CreateScenario(
            string scenarioName,
            IEnumerable<OptionInput> optionInputs,
            IEnumerable<FutureInput> futureInputs,
            AtomicMarketSnap snap)
        {
            if (string.IsNullOrWhiteSpace(scenarioName))
                throw new ArgumentException("Scenario name cannot be empty.", nameof(scenarioName));

            if (snap == null)
                throw new ArgumentNullException(nameof(snap));

            var trades = new List<Trade>();

            // -----------------------------------------
            // Convert Option Inputs → Trades
            // -----------------------------------------
            foreach (var o in optionInputs)
            {
                // Lookup lot size from option snapshot
                if (!snap.OptionsByTradingSymbol.TryGetValue(o.TradingSymbol, out var optSnap))
                    throw new InvalidOperationException($"Option not found in snapshot: {o.TradingSymbol}");

                int lotSize = optSnap.LotSize;

                var inst = new Instrument(
                    tradingSymbol: o.TradingSymbol,
                    lotSize: lotSize,
                    productType: ProductType.Option,
                    strike: o.Strike,
                    optionType: o.OptionType,
                    expiry: o.Expiry
                );

                trades.Add(new Trade(inst, o.Lots));
            }

            // -----------------------------------------
            // Convert Future Inputs → Trades
            // -----------------------------------------
            foreach (var f in futureInputs)
            {
                // Lookup future by trading symbol
                if (!snap.FuturesByTradingSymbol.TryGetValue(f.TradingSymbol, out var futDetail))
                    throw new InvalidOperationException($"Future not found in snapshot: {f.TradingSymbol}");

                int lotSize = futDetail.FutureSnapshot.LotSize;

                var inst = new Instrument(
                    tradingSymbol: f.TradingSymbol,
                    lotSize: lotSize,
                    productType: ProductType.Future,
                    strike: null,
                    optionType: null,
                    expiry: f.Expiry
                );

                trades.Add(new Trade(inst, f.Lots));
            }

            // -----------------------------------------
            // Create & store Scenario
            // -----------------------------------------
            var scenario = new Scenario(trades, snap);
            _scenarios[scenarioName] = scenario;

            return scenario;
        }
        // -----------------------------------------------------------
        // Delete scenario
        // -----------------------------------------------------------
        public bool DeleteScenario(string scenarioName)
        {
            return _scenarios.TryRemove(scenarioName, out _);
        }

        // -----------------------------------------------------------
        // Get a specific scenario
        // -----------------------------------------------------------
        public Scenario? GetScenario(string scenarioName)
        {
            _scenarios.TryGetValue(scenarioName, out var scenario);
            return scenario;
        }

        // -----------------------------------------------------------
        // Get all scenarios
        // -----------------------------------------------------------
        public IReadOnlyDictionary<string, Scenario> GetAllScenarios()
            => _scenarios;

        // -----------------------------------------------------------
        // Shutdown orchestrator
        // -----------------------------------------------------------
        public void Shutdown()
        {
            _running = false;
            _scenarios.Clear();
        }

        public void Dispose() => Shutdown();
    }
}