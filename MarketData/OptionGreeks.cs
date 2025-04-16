using QuantitativeAnalytics;
using InstrumentStatic;

namespace MarketData
{
    public readonly struct OptionGreeks
    {
        public double NPV { get; }
        public double Delta { get; }
        public double Gamma { get; }
        public double Vega { get; }
        public double Rho { get; }
        public double Theta { get; }

        public OptionGreeks(
            OptionSnapshot optionSnapshot,
            double impliedFuture,
            double riskFreeRate,
            double timeToExpiry,
            VolSurface volSurface)
        {
            NPV = Black76.NPV(
                optionSnapshot.OptionType == OptionType.CE,
                impliedFuture,
                optionSnapshot.Strike,
                timeToExpiry,
                riskFreeRate,
                volSurface
            );

            Delta = Greeks.Delta(
                optionSnapshot.OptionType == OptionType.CE,
                impliedFuture,
                optionSnapshot.Strike,
                timeToExpiry,
                riskFreeRate,
                volSurface
            );

            Gamma = Greeks.Gamma(
                optionSnapshot.OptionType == OptionType.CE,
                impliedFuture,
                optionSnapshot.Strike,
                timeToExpiry,
                riskFreeRate,
                volSurface
            );

            Vega = Greeks.Vega(
                optionSnapshot.OptionType == OptionType.CE,
                impliedFuture,
                optionSnapshot.Strike,
                timeToExpiry,
                riskFreeRate,
                volSurface
            );

            Rho = Greeks.Rho(
                optionSnapshot.OptionType == OptionType.CE,
                impliedFuture,
                optionSnapshot.Strike,
                timeToExpiry,
                riskFreeRate,
                volSurface
            );

            Theta = Greeks.Theta(
                optionSnapshot.OptionType == OptionType.CE,
                impliedFuture,
                optionSnapshot.Strike,
                timeToExpiry,
                riskFreeRate,
                volSurface
            );
        }
    }
}