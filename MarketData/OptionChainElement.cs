using QuantitativeAnalytics;
using InstrumentStatic;

namespace MarketData
{
    public class OptionChainElement
    {
        private readonly Option _callOption;
        private readonly Option _putOption;
        private readonly double _strike;
        
        private OptionGreeks _callGreeks;
        private OptionGreeks _putGreeks;
        private readonly object _lock = new();

        public OptionChainElement(
            Option callOption, 
            Option putOption, 
            Index index, 
            VolSurface volSurface, 
            RFR rfr,
            DateTime now)
        {
            // Input validation
            if (callOption == null) throw new ArgumentNullException(nameof(callOption));
            if (putOption == null) throw new ArgumentNullException(nameof(putOption));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (volSurface == null) throw new ArgumentNullException(nameof(volSurface));
            if (rfr == null) throw new ArgumentNullException(nameof(rfr));
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

            // Initial Greeks calculation
            var indexSnapshot = index.GetSnapshot();
            double timeToExpiry = index.Calendar.GetYearFraction(now, callOption.Expiry);
            
            _callGreeks = new OptionGreeks(
                optionSnapshot: callOption.GetSnapshot(),
                impliedFuture: indexSnapshot.ImpliedFuture,
                riskFreeRate: rfr.Value,  // Using passed RFR
                timeToExpiry: timeToExpiry,
                volSurface: volSurface);
                
            _putGreeks = new OptionGreeks(
                optionSnapshot: putOption.GetSnapshot(),
                impliedFuture: indexSnapshot.ImpliedFuture,
                riskFreeRate: rfr.Value,  // Using passed RFR
                timeToExpiry: timeToExpiry,
                volSurface: volSurface);
        }

        public void UpdateGreeks(
            Index index, 
            VolSurface volSurface, 
            RFR rfr,
            DateTime now)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (volSurface == null) throw new ArgumentNullException(nameof(volSurface));
            if (rfr == null) throw new ArgumentNullException(nameof(rfr));

            lock (_lock)
            {
                var indexSnapshot = index.GetSnapshot();
                double timeToExpiry = index.Calendar.GetYearFraction(now, _callOption.Expiry);
                
                _callGreeks = new OptionGreeks(
                    optionSnapshot: _callOption.GetSnapshot(),
                    impliedFuture: indexSnapshot.ImpliedFuture,
                    riskFreeRate: rfr.Value,  // Using passed RFR
                    timeToExpiry: timeToExpiry,
                    volSurface: volSurface);
                    
                _putGreeks = new OptionGreeks(
                    optionSnapshot: _putOption.GetSnapshot(),
                    impliedFuture: indexSnapshot.ImpliedFuture,
                    riskFreeRate: rfr.Value,  // Using passed RFR
                    timeToExpiry: timeToExpiry,
                    volSurface: volSurface);
            }
        }

        // Properties - lock-free reads
        public Option CallOption => _callOption;
        public Option PutOption => _putOption;
        public double Strike => _strike;
        public OptionGreeks CallGreeks => _callGreeks;
        public OptionGreeks PutGreeks => _putGreeks;
    }
}