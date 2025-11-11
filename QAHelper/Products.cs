// ============================================================
// Products.cs
// Core product hierarchy for virtual valuation objects
// ============================================================

using QuantitativeAnalytics; // For Black76.NPV()
using MarketData;            // For AtomicMarketSnap

namespace QAHelper
{
    /// <summary>
    /// Enumeration of product categories.
    /// </summary>
    public enum ProductType
    {
        Future,
        Option
    }

    /// <summary>
    /// Enumeration of option types.
    /// </summary>
    public enum OptionType
    {
        Call,
        Put
    }

    /// <summary>
    /// Enumeration of supported model types.
    /// </summary>
    public enum ModelType
    {
        Black76,
        Heston
    }

    /// <summary>
    /// Base abstract class for valuation products.
    /// Contains only core identifiers and computed NPV.
    /// </summary>
    public abstract class Product
    {
        public readonly ProductType ProductType;
        public readonly DateTime Expiry;
        public readonly ModelType Model;
        public readonly double NPV;

        protected Product(ProductType productType, DateTime expiry, AtomicMarketSnap ams, ModelType model = ModelType.Black76)
        {
            ProductType = productType;
            Expiry = expiry;
            Model = model;
            NPV = ComputeNPV(productType, ams, model, this);
        }

        private static double ComputeNPV(ProductType productType, AtomicMarketSnap ams, ModelType model, Product product)
        {
            if (ams == null)
                throw new ArgumentNullException(nameof(ams));

            switch (model)
            {
                case ModelType.Black76:
                    if (productType == ProductType.Future)
                    {
                        return ams.ImpliedFuture;
                    }
                    else if (productType == ProductType.Option && product is OptionProduct opt)
                    {
                        // Compute time-to-expiry as year fraction between snapshot initialization and contract expiry
                        // Assumes CalendarLib.GetYearFraction(DateTime start, DateTime end) exists and returns double (years)
                        var marketInfo = MarketHelperDict.MarketHelperDict.MarketInfoDict.First().Value;
                        double tte = marketInfo.NSECalendar.GetYearFraction(ams.InitializationTime, product.Expiry);
                        bool isCall = opt.OptionType == OptionType.Call;
                        double forward = ams.ImpliedFuture;
                        double strike = opt.Strike;
                        double rfr = ams.RiskFreeRate;
                        double div = ams.DivYield;
                        var volSurface = ams.VolSurface;
                        
                        IGreeksCalculator _greeksCalculator = Black76GreeksCalculator.Instance;

                        return  _greeksCalculator.NPV(
                            productType: QuantitativeAnalytics.ProductType.Option,
                            isCall: isCall,
                            spot: ams.IndexSpot,
                            forward: forward,                            
                            strike: strike,
                            rate: rfr,
                            dividendYield: div,
                            tte: tte,
                            surface: volSurface
                        );
                    }
                    break;

                case ModelType.Heston:
                    throw new NotImplementedException("Heston model not yet supported.");
            }

            throw new ArgumentException($"Unsupported product type {productType} for model {model}");
        }

        public override string ToString()
        {
            return $"{ProductType} | Exp: {Expiry:dd-MMM-yyyy} | Model: {Model} | NPV: {NPV:F4}";
        }
    }

    /// <summary>
    /// Represents a futures contract product (valuation-only).
    /// </summary>
    public sealed class FutureProduct : Product
    {
        public FutureProduct(DateTime expiry, AtomicMarketSnap ams, ModelType model = ModelType.Black76)
            : base(ProductType.Future, expiry, ams, model)
        {
        }

        public override string ToString()
        {
            return $"{base.ToString()} | Future";
        }
    }

    /// <summary>
    /// Represents an option contract product (valuation-only).
    /// </summary>
    public sealed class OptionProduct : Product
    {
        public readonly double Strike;
        public readonly OptionType OptionType;

        public OptionProduct(double strike, OptionType optionType, DateTime expiry, AtomicMarketSnap ams, ModelType model = ModelType.Black76)
            : base(ProductType.Option, expiry, ams, model)
        {
            Strike = strike;
            OptionType = optionType;
        }

        public override string ToString()
        {
            return $"{base.ToString()} | {OptionType} | Strike: {Strike}";
        }
    }
}