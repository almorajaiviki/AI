using QuantitativeAnalytics;

namespace Program
{    
    public class VolDebugConsole
    {
        public static void Main(string[] args)
        {
            double timeToExpiry = 0.004048582995951417;
            double forwardPrice = 25962.1;
            double rfr = 0.054251;
            double bump = 0.001;

            Console.WriteLine("inputs are timeToExpiry: " + timeToExpiry);
            Console.WriteLine("forwardPrice: " + forwardPrice);
            Console.WriteLine("risk free rate: " + rfr);
            

            IEnumerable<(
                IEnumerable<(double strike, double Price, double OI)> callData,
                IEnumerable<(double strike, double Price, double OI)> putData,
                double forwardPrice,
                double riskFreeRate,
                double timeToExpiry,
                double OICutoff)> skewParamsList = 
                
                new List<(
                IEnumerable<(double strike, double Price, double OI)> callData,
                IEnumerable<(double strike, double Price, double OI)> putData,
                double forwardPrice,
                double riskFreeRate,
                double timeToExpiry,
                double OICutoff)>
            {
                (
                    new List<(double strike, double Price, double OI)>
                    {   
                        (24700, (1253.55 + 1276.5)/2, 33900),
                        (24800,  (1153.8 + 1176.55)/2, 39675)
                    }, 
                    new List<(double strike, double Price, double OI)>
                    {   
                        (24700, (0.65 + 0.85)/2, 1525425),
                        (24800, (0.75 + 1.15)/2, 2163375),
                        (24900, (0.70 + 1.05)/2, 2163375),
                        (25000, (0.80 + 0.85)/2, 2163375),
                        (25100, (1.00 + 1.10)/2, 2163375),
                        (25200, (1.05 + 1.15)/2, 2163375),
                        (25300, (1.05 + 1.15)/2, 2163375),
                        (25400, (1.40 + 1.50)/2, 2163375)
                    }, 
                    forwardPrice, 
                    rfr, 
                    timeToExpiry,
                    1000000
                )
            };


            // try and create a volsurface
            Black76PriceSpaceVolSurface black76PriceSpaceVolSurface = new Black76PriceSpaceVolSurface( skewParamsList);
            Console.WriteLine("vol surface created");

            double moneyness = Math.Log (24700/forwardPrice);
            Console.WriteLine($"moneyness at 24700 strike: {moneyness}");

            //try and get a vol at 24700 strike
            double iv_24700 = black76PriceSpaceVolSurface.GetVol(timeToExpiry, moneyness);
            Console.WriteLine($"iv at 24700 strike: {iv_24700} at moneyness: {moneyness}");

            //try to get npv at 24700 strike
            double npv_24700_put = Black76GreeksCalculator.Instance.NPV ( ProductType.Option, false, forwardPrice , forwardPrice, 24700, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"npv at 24700 strike: {npv_24700_put}");


            double gamma_24700 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 24700, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 24700 strike: {gamma_24700}");

            double npv_up = Black76GreeksCalculator.Instance.NPV ( ProductType.Option, false, forwardPrice * (1 + bump) , forwardPrice * (1 + bump), 24700, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"npv up: {npv_up}");
            double moneyness_up = Math.Log( 24700/(forwardPrice * (1 + bump)));
            double iv_up = black76PriceSpaceVolSurface.GetVol(timeToExpiry, moneyness_up);            
            Console.WriteLine($"iv up: {iv_up} at moneyness up: {moneyness_up} ");
            double npv_down = Black76GreeksCalculator.Instance.NPV ( ProductType.Option, false, forwardPrice * (1 - bump) , forwardPrice * (1 - bump), 24700, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"npv down: {npv_down}");
            double moneyness_down = Math.Log (24700/(forwardPrice * (1 - bump)));
            double iv_down = black76PriceSpaceVolSurface.GetVol(timeToExpiry, moneyness_down);
            Console.WriteLine($"iv down: {iv_down} at moneyness down: {moneyness_down} ");
            double gamma_calculated = (npv_up - 2 * npv_24700_put + npv_down) ;
            Console.WriteLine($"gamma calculated: {gamma_calculated}");


        }
    }
}