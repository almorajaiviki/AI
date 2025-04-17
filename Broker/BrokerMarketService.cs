using InstrumentStatic;
using MarketHelperDict;
using MarketData;
using QuantitativeAnalytics;
using System.Reflection.Metadata;

namespace Broker
{
    public class BrokerMarketService
    {
        private readonly IInstrumentProvider _instrumentProvider;
        private readonly MarketInfo _marketInfo;

        // Constructor accepts MarketInfo (injected via DI or manual initialization)
        public BrokerMarketService(IInstrumentProvider instrumentProvider, MarketInfo marketInfo)
        {
            _instrumentProvider = instrumentProvider ?? throw new ArgumentNullException(nameof(instrumentProvider));
            _marketInfo = marketInfo ?? throw new ArgumentNullException(nameof(marketInfo));
        }

        /// <summary>
        /// Fetches NFO options for the MarketInfo's IndexTradingSymbol, filtered by expiry >= current date.
        /// </summary>
        private async Task<(NSEInstrument index, List<NFOInstrument> options, DateTime latestExpiry)> GetIndexOptionInstrumentsAsync(DateTime? now = null)
        {
            try
            {
                // Use MarketInfo's IndexTradingSymbol (e.g., "NIFTY INDEX")
                return await _instrumentProvider.GetNfoOptionsForNseIndexAsync(
                    _marketInfo.IndexTradingSymbol,
                    now ?? DateTime.Now
                );
            }
            catch (KeyNotFoundException ex)
            {
                throw new BrokerException(
                    $"Index '{_marketInfo.IndexTradingSymbol}' not found or has no options.", ex);
            }
        }
    
        // New method to generate a MarketData object.
        public  MarketData.MarketData GenerateMarketData(double rfr, DateTime now, BrokerAuthService brokerAuthService, HttpClient httpClient)
        {
            // Input validation for rfr.
            if (!double.IsFinite(rfr) || rfr <= 0) // corrected validation
            {
                throw new ArgumentException("RFR must be a finite and positive number.", nameof(rfr));
            }

            //  Create RFR object
            RFR rfrObject = new RFR(rfr);

            // Get NSEInstrument from index.
            var Instruments = GetIndexOptionInstrumentsAsync(now).Result;
            NSEInstrument indexInstrument =  Instruments.index;
            QuoteFetcher quoteFetcherService = new QuoteFetcher(brokerAuthService, httpClient);
            InstrumentQuote IndexQuote = quoteFetcherService.FetchQuotesAsync(new List<QuoteRequest> { new QuoteRequest( indexInstrument.Exchange, indexInstrument.Token, indexInstrument.TradingSymbol) }).Result.First();
            //create index object
            MarketData.Index index = new MarketData.Index(
                indexInstrument.TradingSymbol,
                indexInstrument.Token,
                IndexQuote.Ltp,
                _marketInfo.NSECalendar,
                rfrObject,
                Instruments.latestExpiry,
                now
            );

            //get options quotes
            List<NFOInstrument> options = Instruments.options;
            List<InstrumentQuote> optionQuotes = quoteFetcherService.FetchQuotesAsync(
                options.Select(o => new QuoteRequest(o.Exchange, o.Token, o.TradingSymbol)).ToList()
            ).Result;

            //subscribe to the options
            BrokerWebSocketService brokerWebSocketService = new(brokerAuthService.CurrentAuthResponse.UserName, brokerAuthService.CurrentAuthResponse.UserName, brokerAuthService.CurrentAuthResponse.Token);
            brokerWebSocketService.ConnectAsync().Wait();
            brokerWebSocketService.SubscribeToBatchAsync(options.Select(o => (o.Exchange ,o.Token))).Wait();

            //get OI for all options
            bool bIsOIFetched = false;
            while (!bIsOIFetched)
            {
                if (options.Select(o => o.Token).Any(token =>  !brokerWebSocketService._subscriptionAcks.Keys.Contains(token)))
                {
                    Task.Delay(100).Wait();
                }
                else
                {
                    bIsOIFetched = true;
                    brokerWebSocketService.MarkOIFetched();
                }
            }

            //calculate iv for each option
            Dictionary<uint, double> TokenIV =  options.Select(o => new KeyValuePair<uint, double>
            (o.Token,
            Black76.ComputeIV
                (o.OptionType == OptionType.CE, 
                index.GetSnapshot().ImpliedFuture,
                o.StrikePrice,
                _marketInfo.NSECalendar.GetYearFraction(now, o.Expiry),
                rfr,
                optionQuotes.Where(oq=>oq.Token == o.Token).First().Ltp            
                )
            )).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            //create options
            List<Option> optionList = options.Select
            (
                nfo => 
                new Option
                (nfo.OptionType ?? throw new InvalidOperationException($"OptionType is null for token {nfo.Token}"),
                nfo.TradingSymbol, nfo.Token, nfo.StrikePrice, nfo.Expiry, now, index, rfrObject, 
            optionQuotes.Where(oq=>oq.Token == nfo.Token).First().Ltp,
            optionQuotes.Where(oq=>oq.Token == nfo.Token).First().Bid,
            optionQuotes.Where(oq=>oq.Token == nfo.Token).First().Ask,
            double.Parse (brokerWebSocketService._subscriptionAcks[nfo.Token].OpenInterest),
            TokenIV[nfo.Token]
             ) ).ToList();

             //now create the MarketData object
             MarketData.MarketData marketData = new MarketData.MarketData(
                now,
                index,
                rfrObject,
                optionList
             );
             //  Return the MarketData object.
             //  Note: The MarketData object is created with the current date and time.
             //  The options are created with the latest expiry date.
             //  The index is created with the latest expiry date.
                

            //  Need more information to construct the MarketData object.
            //  For now, I'll return null.  We'll fill in the details as you provide them.
            return marketData;
        }

    }

    public class BrokerException : Exception
    {
        public BrokerException(string message, Exception inner) : base(message, inner) { }
    }
}