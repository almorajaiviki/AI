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
            double spot, bool bUseMktFuture,
            double riskFreeRate,
            double dividendYield,
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
                productType, isCall, spot, futureSnapshot.Mid, bUseMktFuture, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Delta = greeksCalculator.Delta(
                productType, isCall, spot, futureSnapshot.Mid, bUseMktFuture, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Gamma = greeksCalculator.Gamma(
                productType, isCall, spot, futureSnapshot.Mid, bUseMktFuture, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Vega = greeksCalculator.VegaByParam(
                productType, isCall, spot, futureSnapshot.Mid, bUseMktFuture, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface)
                .ToImmutableArray();

            Rho = greeksCalculator.Rho(
                productType, isCall, spot, futureSnapshot.Mid, bUseMktFuture, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Theta = greeksCalculator.Theta(
                productType, isCall, spot, futureSnapshot.Mid, bUseMktFuture, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);
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
