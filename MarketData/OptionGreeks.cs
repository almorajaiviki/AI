using QuantitativeAnalytics;
using InstrumentStatic;
using System.Collections.Immutable;

namespace MarketData
{
    public readonly struct OptionGreeks
    {
        public uint Token { get; }
        public double NPV { get; }
        public double Delta { get; }
        public double Gamma { get; }
        public ImmutableArray<(string, double)> VolRisk { get; }
        public double Rho { get; }
        public double Theta { get; }
        public double IV_Used { get; }

        public OptionGreeks(
            OptionSnapshot optionSnapshot,
            double impliedFuture,
            double riskFreeRate,
            double timeToExpiry,
            IParametricModelSurface volSurface,
            IGreeksCalculator greeksCalculator)
        {
            Token = optionSnapshot.Token;

            bool isCall = optionSnapshot.OptionType == OptionType.CE;
            double strike = optionSnapshot.Strike;
            // compute log-moneyness (ln(K/F)) to match DTO/storage convention
            double logMoneyness = Math.Log(strike / impliedFuture);
            var productType = ProductType.Option;

            // ask the surface using log-moneyness
            IV_Used = volSurface.GetVol(timeToExpiry, logMoneyness);

            //for options, the decision to use benchmark future or spot is made in MarketData class
            NPV = greeksCalculator.NPV(
                productType, isCall, impliedFuture, strike,
                riskFreeRate, timeToExpiry, volSurface);

            Delta = greeksCalculator.Delta(
                productType, isCall, impliedFuture, strike,
                riskFreeRate, timeToExpiry, volSurface);

            Gamma = greeksCalculator.Gamma(
                productType, isCall, impliedFuture, strike,
                riskFreeRate, timeToExpiry, volSurface);

            VolRisk = greeksCalculator.VolRiskByParam(
                productType, isCall, impliedFuture, strike,
                riskFreeRate, timeToExpiry, volSurface)
                .ToImmutableArray();

            Rho = greeksCalculator.Rho(
                productType, isCall, impliedFuture, strike,
                riskFreeRate, timeToExpiry, volSurface);

            Theta = greeksCalculator.Theta(
                productType, isCall, impliedFuture, strike,
                riskFreeRate, timeToExpiry, volSurface);
        }
    }

    public class OptionGreeksDTO
    {
        public double NPV { get; set; }
        public double Delta { get; set; }
        public double Gamma { get; set; }
        public ImmutableArray<(string, double)> VolRisk { get; set; }
        public double Rho { get; set; }
        public double Theta { get; set; }
        public double IV_Used { get; set; }

        public OptionGreeksDTO(OptionGreeks greeks)
        {
            NPV = greeks.NPV;
            Delta = greeks.Delta;
            Gamma = greeks.Gamma;
            VolRisk = greeks.VolRisk;
            Rho = greeks.Rho;
            Theta = greeks.Theta;
            IV_Used = greeks.IV_Used;
        }
    }
}