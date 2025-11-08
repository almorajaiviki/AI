using InstrumentStatic;
using MarketHelperDict;
using MarketData;
using BrokerInterfaces;
using QuantitativeAnalytics;

namespace Server
{
    public class MarketDataGenerator
    {
        private readonly IBrokerInstrumentService<NSEInstrument, NFOInstrument> _instrumentProvider;
        private readonly MarketInfo _marketInfo;

        public MarketDataGenerator(
            IBrokerInstrumentService<NSEInstrument, NFOInstrument> instrumentProvider,
            MarketInfo marketInfo)
        {
            _instrumentProvider = instrumentProvider ?? throw new ArgumentNullException(nameof(instrumentProvider));
            _marketInfo = marketInfo ?? throw new ArgumentNullException(nameof(marketInfo));
        }

        public MarketData.MarketData GenerateMarketData(
            double rfr, double OICutoff, bool bUseMktFuture,
            DateTime now,
            List<NFOInstrument> options,
            List<InstrumentQuote> optionQuotes,
            List<NFOInstrument> futures,
            List<InstrumentQuote> futureQuotes,
            Dictionary<uint, double> optionOI, // token -> OI
            Dictionary<uint, double> futuresOI,
            VolatilityModel volatilityModel,
            MarketData.Index indexObj,
            CancellationToken token


        )
        {
            if (!double.IsFinite(rfr) || rfr <= 0)
                throw new ArgumentException("RFR must be a finite and positive number.", nameof(rfr));

            RFR rfrObject = new RFR(rfr);

            List<NFOInstrument> filteredOptions = options.Where(o =>
                o.StrikePrice >= indexObj.GetSnapshot().ImpliedFuture * (1 + _marketInfo.LowerStrikePct) &&
                o.StrikePrice <= indexObj.GetSnapshot().ImpliedFuture * (1 + _marketInfo.UpperStrikePct)
            ).ToList();

            List<Option> optionList = filteredOptions.Select(nfo =>
                new Option(
                    nfo.OptionType ?? throw new InvalidOperationException($"OptionType is null for token {nfo.Token}"),
                    nfo.TradingSymbol,
                    nfo.Token,
                    nfo.StrikePrice,
                    nfo.Expiry,
                    now,
                    rfrObject,
                    optionQuotes.FirstOrDefault(oq => oq.Token == nfo.Token.ToString())?.LastTradedPrice ?? 0d, // FIXED
                    optionQuotes.FirstOrDefault(oq => oq.Token == nfo.Token.ToString())?.BidPrice ?? 0d,         // FIXED
                    optionQuotes.FirstOrDefault(oq => oq.Token == nfo.Token.ToString())?.AskPrice ?? 0d,         // FIXED
                    optionOI.TryGetValue(nfo.Token, out var oi) ? oi : 0d
                )
            ).ToList();

            List<Future> futureList = futures.Select(nfo =>
                new Future(                    
                    nfo.TradingSymbol,
                    nfo.Token,                    
                    nfo.Expiry,
                    now,
                    rfrObject,
                    futureQuotes.FirstOrDefault(oq => oq.Token == nfo.Token.ToString())?.LastTradedPrice ?? 0d, // FIXED
                    futureQuotes.FirstOrDefault(oq => oq.Token == nfo.Token.ToString())?.BidPrice ?? 0d,         // FIXED
                    futureQuotes.FirstOrDefault(oq => oq.Token == nfo.Token.ToString())?.AskPrice ?? 0d,         // FIXED
                    futuresOI.TryGetValue(nfo.Token, out var oi) ? oi : 0d
                )
            ).ToList();

            return new MarketData.MarketData(
                now,
                indexObj,
                rfrObject, OICutoff, bUseMktFuture,
                optionList,
                futureList,
                volatilityModel,
                token // PASS the token
            );
        }
    }
}
