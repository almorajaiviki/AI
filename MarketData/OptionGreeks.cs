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
        public ImmutableArray<(string, double)> Vega { get; }
        public double Rho { get; }
        public double Theta { get; }
        public double IV_Used { get; }

        public OptionGreeks(
            OptionSnapshot optionSnapshot,
            double spot,
            double impliedFuture,
            double riskFreeRate,
            double timeToExpiry,
            IParametricModelSurface volSurface,
            IGreeksCalculator greeksCalculator)
        {
            Token = optionSnapshot.Token;

            bool isCall = optionSnapshot.OptionType == OptionType.CE;
            double strike = optionSnapshot.Strike;
            double moneyness = strike / impliedFuture;
            double dividendYield = 0.0; // Hardcoded for now
            var productType = ProductType.Option;            

            IV_Used = volSurface.GetVol(timeToExpiry ,moneyness);

            //for options, the decision to use benchmark future or spot is made in MarketData class
            NPV = greeksCalculator.NPV(
                productType, isCall, spot, impliedFuture, true, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Delta = greeksCalculator.Delta(
                productType, isCall, spot, impliedFuture, true, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Gamma = greeksCalculator.Gamma(
                productType, isCall, spot, impliedFuture, true, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Vega = greeksCalculator.VegaByParam(
                productType, isCall, spot, impliedFuture, true, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface)
                .ToImmutableArray();

            Rho = greeksCalculator.Rho(
                productType, isCall, spot, impliedFuture, true, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);

            Theta = greeksCalculator.Theta(
                productType, isCall, spot, impliedFuture, true, strike,
                riskFreeRate, dividendYield, timeToExpiry, volSurface);
        }
    }

    public class OptionGreeksDTO
    {
        public double NPV { get; set; }
        public double Delta { get; set; }
        public double Gamma { get; set; }
        public ImmutableArray<(string, double)> Vega { get; set; }
        public double Rho { get; set; }
        public double Theta { get; set; }
        public double IV_Used { get; set; }

        public OptionGreeksDTO(OptionGreeks greeks)
        {
            NPV = greeks.NPV;
            Delta = greeks.Delta;
            Gamma = greeks.Gamma;
            Vega = greeks.Vega;
            Rho = greeks.Rho;
            Theta = greeks.Theta;
            IV_Used = greeks.IV_Used;
        }
    }
}