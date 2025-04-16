using InstrumentStatic;
using MarketHelperDict;
using MarketData;

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

            //  Need more information to construct the MarketData object.
            //  For now, I'll return null.  We'll fill in the details as you provide them.
            return null;
        }

    }

    public class BrokerException : Exception
    {
        public BrokerException(string message, Exception inner) : base(message, inner) { }
    }
}