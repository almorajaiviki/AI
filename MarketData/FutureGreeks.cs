using QuantitativeAnalytics;
using System.Collections.Immutable;

namespace MarketData
{
    public readonly struct FutureGreeks
    {
        public uint Token { get; }
        public double NPV { get; }
        public double Delta { get; }
        public double Gamma { get; }
        public ImmutableArray<(string, double)> Vega { get; }
        public double Rho { get; }
        public double Theta { get; }

        public FutureGreeks(
            FutureSnapshot futureSnapshot,
            double riskFreeRate,
            double timeToExpiry,
            IParametricModelSurface volSurface,
            IGreeksCalculator greeksCalculator)
        {
            Token = futureSnapshot.Token;

            // Futures don’t have strike or call/put type
            bool isCall = true; // Dummy (not used for futures)
            double strike = double.NaN; // Not applicable
            var productType = ProductType.Future;

            NPV = greeksCalculator.NPV(
                productType, isCall, futureSnapshot.Mid, strike,
                riskFreeRate, timeToExpiry, volSurface);

            Delta = greeksCalculator.Delta(
                productType, isCall, futureSnapshot.Mid, strike,
                riskFreeRate, timeToExpiry, volSurface);

            Gamma = greeksCalculator.Gamma(
                productType, isCall, futureSnapshot.Mid, strike,
                riskFreeRate, timeToExpiry, volSurface);

            Vega = greeksCalculator.VegaByParam(
                productType, isCall, futureSnapshot.Mid, strike,
                riskFreeRate, timeToExpiry, volSurface)
                .ToImmutableArray();

            Rho = greeksCalculator.Rho(
                productType, isCall, futureSnapshot.Mid, strike,
                riskFreeRate, timeToExpiry, volSurface);

            Theta = greeksCalculator.Theta(
                productType, isCall, futureSnapshot.Mid, strike,
                riskFreeRate, timeToExpiry, volSurface);
        }
    }

    public class FutureGreeksDTO
    {
        public double NPV { get; set; }
        public double Delta { get; set; }
        public double Gamma { get; set; }
        public ImmutableArray<(string, double)> Vega { get; set; }
        public double Rho { get; set; }
        public double Theta { get; set; }

        public FutureGreeksDTO(FutureGreeks greeks)
        {
            NPV = greeks.NPV;
            Delta = greeks.Delta;
            Gamma = greeks.Gamma;
            Vega = greeks.Vega;
            Rho = greeks.Rho;
            Theta = greeks.Theta;
        }
    }
}
