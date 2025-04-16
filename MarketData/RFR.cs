namespace MarketData
{
    /// <summary>
    /// Represents the risk-free rate (RFR) used in financial calculations.
    /// </summary>
    public class RFR
    {
        private double _value;
        private readonly object _lock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="RFR"/> class with the specified risk-free rate.
        /// </summary>
        /// <param name="rfr">The initial risk-free rate.</param>
        /// <exception cref="ArgumentException">Thrown when the input is not a finite number.</exception>
        public RFR(double rfr)
        {
            if (!double.IsFinite(rfr))
                throw new ArgumentException("RFR must be a finite number.", nameof(rfr));

            _value = rfr;
        }

        /// <summary>
        /// Gets the current risk-free rate.
        /// No lock is needed as reading a double is atomic.
        /// </summary>
        public double Value => _value;

        /// <summary>
        /// Updates the risk-free rate to a new value.
        /// </summary>
        /// <param name="newRFR">The new risk-free rate value.</param>
        /// <exception cref="ArgumentException">Thrown when the input is not a finite number.</exception>
        public void Update(double newRFR)
        {
            if (!double.IsFinite(newRFR))
                throw new ArgumentException("RFR must be a finite number.", nameof(newRFR));

            lock (_lock)
            {
                _value = newRFR;
            }
        }
    }
}
