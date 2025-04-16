using CalendarLib;

namespace MarketData
{
    public class Index
    {
        public string IndexName { get; }
        public uint Token { get; }
        public MarketCalendar Calendar => _calendar;

        private readonly MarketCalendar _calendar;
        private readonly DateTime _expiry;
        private double _indexSpot;
        private double _impliedFuture;
        private readonly object _lock = new();

        public Index(string indexName, uint token, double indexSpot, 
                   MarketCalendar calendar, RFR rfr, DateTime expiry, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("Index name cannot be null or empty", nameof(indexName));
            
            if (token == 0)
                throw new ArgumentException("Token must be greater than zero", nameof(token));
            
            if (indexSpot < 0)
                throw new ArgumentException("Index spot price must be non-negative", nameof(indexSpot));
            
            if (rfr.Value < 0)
                throw new ArgumentException("Risk-free rate must be non-negative", nameof(rfr));
            
            if (now >= expiry)
                throw new ArgumentException("Expiry must be in the future", nameof(expiry));

            IndexName = indexName;
            Token = token;
            _calendar = calendar;
            _expiry = expiry;
            _indexSpot = indexSpot;
            _impliedFuture = CalculateImpliedFuture(indexSpot, rfr, now);
        }

        public void UpdateSpot(double newSpot, RFR rfr, DateTime now)
        {
            if (newSpot < 0)
                throw new ArgumentOutOfRangeException(nameof(newSpot), "Spot price must be non-negative");
            if (rfr.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(rfr), "RFR must be non-negative");

            lock (_lock)
            {
                _indexSpot = newSpot;
                _impliedFuture = CalculateImpliedFuture(newSpot, rfr, now);
            }
        }

        private double CalculateImpliedFuture(double spot, RFR rfr, DateTime now)
        {
            double timeToExpiry = _calendar.GetYearFraction(now, _expiry);
            return spot * Math.Exp(rfr.Value * timeToExpiry);
        }

        public IndexSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new IndexSnapshot(_indexSpot, _impliedFuture);
            }
        }

    }

    public readonly struct IndexSnapshot
    {
        public double IndexSpot { get; }
        public double ImpliedFuture { get; }

        public IndexSnapshot(double indexSpot, double impliedFuture)
        {
            IndexSpot = indexSpot;
            ImpliedFuture = impliedFuture;
        }

        public override string ToString()
            => $"Spot: {IndexSpot}, Future: {ImpliedFuture}";
    }
}