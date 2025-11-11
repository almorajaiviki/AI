namespace QuantitativeAnalytics
{
    public interface IGreeksCalculator
    {
        double NPV(
            ProductType productType,
            bool isCall,
            double spot,
            double forward,
            double strike,
            double rate,
            double dividendYield,
            double tte,
            IParametricModelSurface surface);

        double Delta(
            ProductType productType,
            bool isCall,
            double spot,
            double forward,
            double strike,
            double rate,
            double dividendYield,
            double tte,
            IParametricModelSurface surface);

        double Gamma(
            ProductType productType,
            bool isCall,
            double spot,
            double forward,
            double strike,
            double rate,
            double dividendYield,
            double tte,
            IParametricModelSurface surface);

        /// <summary>
        /// Granular vega with parameterwise bumping support.
        /// The enumerable contains (parameterName, bumpAmount) tuples.
        /// Example for Black76: { ("ATMVol", 0.01) } = flat 1 vol point shift.
        /// If null, defaults to { ("ATM", 0.01) }.
        /// </summary>
        IEnumerable<(string ParamName, double Amount)> VegaByParam(
            ProductType productType,
            bool isCall,
            double spot,
            double forward,
            double strike,
            double rate,
            double dividendYield,
            double tte,
            IParametricModelSurface surface,
            IEnumerable<(string parameterName, double bumpAmount)>? bumps = null);

        double Theta(
            ProductType productType,
            bool isCall,
            double spot,
            double forward,
            double strike,
            double rate,
            double dividendYield,
            double tte,
            IParametricModelSurface surface);

        double Rho(
            ProductType productType,
            bool isCall,
            double spot,
            double forward,
            double strike,
            double rate,
            double dividendYield,
            double tte,
            IParametricModelSurface surface);
    }
}
