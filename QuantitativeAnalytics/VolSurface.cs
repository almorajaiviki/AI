namespace QuantitativeAnalytics
{
    public class VolSurface
    {
        private readonly NaturalCubicSpline volSpline;
        private readonly NaturalCubicSpline putImpactSpline;
        private readonly NaturalCubicSpline callImpactSpline;

        public VolSurface(IEnumerable<(double strike, double IV, double OI)> callData,
                 IEnumerable<(double strike, double IV, double OI)> putData,
                 double forwardPrice,
                 double riskFreeRate,
                 double timeToExpiry,
                 double OICutoff = 500000)  // Changed default to 500,000
        {
            // Input validation
            if (callData == null) throw new ArgumentNullException(nameof(callData));
            if (putData == null) throw new ArgumentNullException(nameof(putData));
            if (OICutoff < 0) throw new ArgumentException("OI Cutoff must be non-negative", nameof(OICutoff));
            if (forwardPrice <= 0) throw new ArgumentException("Forward price must be positive", nameof(forwardPrice));
            if (timeToExpiry <= 0) throw new ArgumentException("Time to expiry must be positive", nameof(timeToExpiry));

            // Filter and convert to dictionaries (only include points meeting OI cutoff)
            var validCallData = callData
                .Where(x => x.OI >= OICutoff)
                .ToDictionary(x => x.strike, x => (x.IV, x.OI));

            var validPutData = putData
                .Where(x => x.OI >= OICutoff)
                .ToDictionary(x => x.strike, x => (x.IV, x.OI));

            if (!validCallData.Any() && !validPutData.Any())
                throw new ArgumentException($"No data points meet the OI cutoff requirement of {OICutoff}.");

            List<(double moneyness, double IV)> volData = new();
            List<(double moneyness, double impact)> putImpacts = new();
            List<(double moneyness, double impact)> callImpacts = new();

            // Process only strikes that have at least one valid side
            var strikesWithData = new HashSet<double>(validCallData.Keys.Concat(validPutData.Keys));

            foreach (double strike in strikesWithData)
            {
                double moneyness = strike / forwardPrice;
                bool hasCall = validCallData.TryGetValue(strike, out var call);
                bool hasPut = validPutData.TryGetValue(strike, out var put);

                if (hasCall && hasPut)
                {
                    // Both sides exist - choose based on higher OI
                    if (call.OI > put.OI)
                    {
                        volData.Add((moneyness, call.IV));
                        // Calculate put impact
                        double putPrice = Black76.NPVIV(false, forwardPrice, strike, riskFreeRate, put.IV, timeToExpiry);
                        double putPriceChosen = Black76.NPVIV(false, forwardPrice, strike, riskFreeRate, call.IV, timeToExpiry);
                        putImpacts.Add((moneyness, putPrice - putPriceChosen));
                        callImpacts.Add((moneyness, 0));
                    }
                    else
                    {
                        volData.Add((moneyness, put.IV));
                        // Calculate call impact
                        double callPrice = Black76.NPVIV(true, forwardPrice, strike, riskFreeRate, call.IV, timeToExpiry);
                        double callPriceChosen = Black76.NPVIV(true, forwardPrice, strike, riskFreeRate, put.IV, timeToExpiry);
                        callImpacts.Add((moneyness, callPrice - callPriceChosen));
                        putImpacts.Add((moneyness, 0));
                    }
                }
                else if (hasCall)
                {
                    // Only call exists
                    volData.Add((moneyness, call.IV));
                    callImpacts.Add((moneyness, 0));
                    putImpacts.Add((moneyness, 0));
                }
                else // hasPut
                {
                    // Only put exists
                    volData.Add((moneyness, put.IV));
                    callImpacts.Add((moneyness, 0));
                    putImpacts.Add((moneyness, 0));
                }
            }

            if (!volData.Any())
                throw new ArgumentException("No valid volatility data available after OI filtering.");

            // Create splines
            volSpline = new NaturalCubicSpline(
                volData.Select(x => x.moneyness).ToArray(),
                volData.Select(x => x.IV).ToArray());

            putImpactSpline = new NaturalCubicSpline(
                putImpacts.Select(x => x.moneyness).ToArray(),
                putImpacts.Select(x => x.impact).ToArray());

            callImpactSpline = new NaturalCubicSpline(
                callImpacts.Select(x => x.moneyness).ToArray(),
                callImpacts.Select(x => x.impact).ToArray());
        }
        
        
        public double GetVol(double moneyness) => volSpline.Evaluate(moneyness);

        public double GetPutPremia(double moneyness) => putImpactSpline.Evaluate(moneyness);

        public double GetCallPremia(double moneyness) => callImpactSpline.Evaluate(moneyness);

        public VolSurface Bump(double bumpAmount)
        {
            return new VolSurface(volSpline.Bump(bumpAmount), putImpactSpline, callImpactSpline);
        }

        private VolSurface(NaturalCubicSpline volSpline, NaturalCubicSpline putImpactSpline, NaturalCubicSpline callImpactSpline)
        {
            this.volSpline = volSpline;
            this.putImpactSpline = putImpactSpline;
            this.callImpactSpline = callImpactSpline;
        }
    }
}