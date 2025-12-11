using QuantitativeAnalytics;
using InstrumentStatic;

namespace MarketData
{
    public class OptionChainElement
    {
        private readonly Option _callOption;
        private readonly Option _putOption;
        private readonly double _strike;
        private readonly IGreeksCalculator _greeksCalculator;

        private OptionGreeks _callGreeks;
        private OptionGreeks _putGreeks;

        private OptionSpreads _callSpreads;
        private OptionSpreads _putSpreads;

        private readonly object _lock = new();

        public OptionChainElement(
            Option callOption,
            Option putOption,
            Index index, ForwardCurve forwardCurve,
            IParametricModelSurface volSurface,
            RFR rfr,
            DateTime now,
            IGreeksCalculator greeksCalculator)
        {
            // Input validation
            if (callOption == null) throw new ArgumentNullException(nameof(callOption));
            if (putOption == null) throw new ArgumentNullException(nameof(putOption));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (volSurface == null) throw new ArgumentNullException(nameof(volSurface));
            if (rfr == null) throw new ArgumentNullException(nameof(rfr));
            if (greeksCalculator == null) throw new ArgumentNullException(nameof(greeksCalculator));
            if (callOption.OptionType != OptionType.CE)
                throw new ArgumentException("First option must be a call");
            if (putOption.OptionType != OptionType.PE)
                throw new ArgumentException("Second option must be a put");
            if (callOption.Strike != putOption.Strike)
                throw new ArgumentException($"Strike mismatch: {callOption.Strike} vs {putOption.Strike}");
            if (callOption.Expiry != putOption.Expiry)
                throw new ArgumentException($"Expiry mismatch: {callOption.Expiry} vs {putOption.Expiry}");

            _callOption = callOption;
            _putOption = putOption;
            _strike = callOption.Strike;
            _greeksCalculator = greeksCalculator;

            var indexSnapshot = index.GetSnapshot();
            double timeToExpiry = index.Calendar.GetYearFraction(now, callOption.Expiry);

            double forwardPrice = forwardCurve.GetForwardPrice(timeToExpiry);

            // Greeks
            _callGreeks = new OptionGreeks(callOption.GetSnapshot(), forwardPrice, rfr.Value, timeToExpiry, volSurface, _greeksCalculator);
            _putGreeks = new OptionGreeks(putOption.GetSnapshot(), forwardPrice, rfr.Value, timeToExpiry, volSurface, _greeksCalculator);

            // Spreads
            _callSpreads = new OptionSpreads(callOption.GetSnapshot(), _callGreeks);
            _putSpreads = new OptionSpreads(putOption.GetSnapshot(), _putGreeks);
        }

        public void UpdateGreeks(Index index, ForwardCurve forwardCurve, IParametricModelSurface volSurface, RFR rfr, DateTime now)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (volSurface == null) throw new ArgumentNullException(nameof(volSurface));
            if (rfr == null) throw new ArgumentNullException(nameof(rfr));

            lock (_lock)
            {
                var indexSnapshot = index.GetSnapshot();
                double timeToExpiry = index.Calendar.GetYearFraction(now, _callOption.Expiry);
                double forwardPrice = forwardCurve.GetForwardPrice(timeToExpiry);

                // Update Greeks
                _callGreeks = new OptionGreeks(_callOption.GetSnapshot(), forwardPrice, rfr.Value, timeToExpiry, volSurface, _greeksCalculator);
                _putGreeks = new OptionGreeks(_putOption.GetSnapshot(), forwardPrice, rfr.Value, timeToExpiry, volSurface, _greeksCalculator);

                // Update Spreads
                _callSpreads = new OptionSpreads(_callOption.GetSnapshot(), _callGreeks);
                _putSpreads = new OptionSpreads(_putOption.GetSnapshot(), _putGreeks);
            }
        }

        // Properties - lock-free reads
        public Option CallOption => _callOption;
        public Option PutOption => _putOption;
        public double Strike => _strike;

        public OptionGreeks CallGreeks => _callGreeks;
        public OptionGreeks PutGreeks => _putGreeks;

        public OptionSpreads CallSpreads => _callSpreads;
        public OptionSpreads PutSpreads => _putSpreads;
    }
}