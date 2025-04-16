using InstrumentStatic;
using MarketHelperDict;

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
        public async Task<(NSEInstrument index, List<NFOInstrument> options, DateTime latestExpiry)> GetIndexOptionsAsync(DateTime? now = null)
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

    }

    public class BrokerException : Exception
    {
        public BrokerException(string message, Exception inner) : base(message, inner) { }
    }
}