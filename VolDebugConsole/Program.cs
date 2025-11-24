using System.Diagnostics;
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
                        (25400, (1.40 + 1.50)/2, 2163375),
                        (25500, (2 + 2.1)/2, 2163375),
                        (25600, (2.8 + 2.9)/2, 2163375),
                        (25700, (5.95 + 6.05)/2, 2163375),
                        (25800, (17 + 17.15)/2, 2163375),
                        (26700, (735.15 + 739.75)/2, 2163375),
                        (26800, (832.4 + 840.15)/2, 2163375),
                        (26900, (931.4 + 945.8)/2, 2163375),
                        (27000, (1035.7 + 1040.3)/2, 2163375),
                        (27100, (1120.25 + 1147.6)/2, 2163375),
                        (27200, (1231.05 + 1247.6)/2, 2163375)

                    }, 
                    forwardPrice, 
                    rfr, 
                    timeToExpiry,
                    1000000
                )
            };

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            // try and create a volsurface
            Black76PriceSpaceVolSurface black76PriceSpaceVolSurface = new Black76PriceSpaceVolSurface( skewParamsList);
            stopwatch.Stop();
            Console.WriteLine($"Vol surface creation time: {stopwatch.ElapsedMilliseconds} ms");
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

            double gamma_25000 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 25000, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 25000 strike: {gamma_25000}");
            double gamma_25100 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 25100, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 25100 strike: {gamma_25100}");
            double gamma_25200 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 25200, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 25200 strike: {gamma_25200}");
            double gamma_25300 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 25300, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 25300 strike: {gamma_25300}");
            double gamma_26800 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 26800, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 26800 strike: {gamma_26800}");
            double gamma_26900 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 26900, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 26900 strike: {gamma_26900}");
            double gamma_27000 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 27000, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 27000 strike: {gamma_27000}");
            double gamma_27100 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 27100, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 27100 strike: {gamma_27100}");
            double gamma_27200 = Black76GreeksCalculator.Instance.Gamma(ProductType.Option, false, forwardPrice, forwardPrice, 27200, rfr, 0.01, timeToExpiry, black76PriceSpaceVolSurface);
            Console.WriteLine($"gamma at 27200 strike: {gamma_27200}");



        }
    }
}