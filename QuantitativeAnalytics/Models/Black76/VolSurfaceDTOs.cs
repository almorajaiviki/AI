using System.Collections.Generic;

namespace QuantitativeAnalytics
{
    public class VolPoint
    {
        public double Moneyness { get; set; }
        public double IV { get; set; }
    }

    public class VolSkewDTO
    {
        public double timeToExpiry { get; set; }
        public List<VolPoint> VolCurve { get; set; }
    }

    public class VolSurfaceDTO
    {
        public List<VolSkewDTO> Skews { get; set; } = new List<VolSkewDTO>();
    }
}
