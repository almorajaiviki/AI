using BrokerInterfaces;
using InstrumentStatic;
using System.Globalization;

namespace Zerodha
{
    public sealed class BrokerInstrumentService : IBrokerInstrumentService<NSEInstrument, NFOInstrument>, IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string InstrumentsUrl = "https://api.kite.trade/instruments"; // CSV endpoint

        // Singleton
        private static BrokerInstrumentService? _instance;
        private static readonly object _lock = new();

        public static IBrokerInstrumentService<NSEInstrument, NFOInstrument> Instance(HttpClient? httpClient = null)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    httpClient ??= new HttpClient();
                    _instance = new BrokerInstrumentService(httpClient);
                }
                return _instance;
            }
        }


        private BrokerInstrumentService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<(NSEInstrument index, List<NFOInstrument> options, List<NFOInstrument> futures, DateTime latestExpiry)> 
            GetOptionsForIndexAsync(string indexSymbol, string IndexSymbol, DateTime now)
        {
            var (nseIndices, nfoIndexOptions, nfoIndexFutures) = await LoadFilteredInstrumentsWithFuturesAsync();

            // Find index instrument
            var index = nseIndices
                .FirstOrDefault(inst => inst.Symbol.Equals(indexSymbol, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"NSE INDEX '{indexSymbol}' not found.");

            // Filter NFO options
            var filteredOptions = nfoIndexOptions
                .Where(opt => opt.Symbol.Equals(IndexSymbol, StringComparison.OrdinalIgnoreCase) && opt.Expiry >= now)
                .ToList();

            var filteredFutures = nfoIndexFutures
                .Where(fut => fut.Symbol.Equals(IndexSymbol, StringComparison.OrdinalIgnoreCase) && fut.Expiry >= now)
                .ToList();

            if (!filteredOptions.Any())
                throw new InvalidOperationException($"No options found for symbol {IndexSymbol} after {now}");

            if (!filteredFutures.Any())
                throw new InvalidOperationException($"No options found for symbol {IndexSymbol} after {now}");

            var futureExpiry = filteredFutures.Select(f => f.Expiry).Min();
            //var latestExpiryOptions = filteredOptions.Where(o => o.Expiry.Date == futureExpiry.Date).ToList();

            // Optional: filter strikes
            var strikeFilteredOptions = filteredOptions.Where(opt => opt.StrikePrice % 100 == 0).ToList();

            return (index, strikeFilteredOptions, filteredFutures, futureExpiry);
        }

        private async Task<(List<NSEInstrument> nseIndices, List<NFOInstrument> nfoIndexOptions, List<NFOInstrument> nfoIndexFutures)> LoadFilteredInstrumentsWithFuturesAsync()
        {
            var nseIndices = new List<NSEInstrument>();
            var nfoIndexOptions = new List<NFOInstrument>();
            var nfoIndexFutures = new List<NFOInstrument>();

            var csvData = await _httpClient.GetStringAsync(InstrumentsUrl);
            var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Skip header line
            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    var parts = lines[i].Split(',');

                    var exchange = parts[11];        // NSE / NFO
                    var instrumentType = parts[9];   // EQ / OPTIDX / FUTIDX
                    var segment = parts[10];         // INDICES / NFO-OPT / NFO-FUT

                    // ✅ Index filter
                    if (exchange == "NSE" && segment == "INDICES" && instrumentType == "EQ")
                    {
                        var instrument = ZerodhaInstrumentCsvParser.ParseNSEInstrument(parts);
                        nseIndices.Add(instrument);
                    }
                    // ✅ Options filter (Index Options)
                    else if (exchange == "NFO" && segment == "NFO-OPT" && (instrumentType == "CE" || instrumentType == "PE"))
                    {
                        var instrument = ZerodhaInstrumentCsvParser.ParseNFOInstrument(parts);
                        nfoIndexOptions.Add(instrument);
                    }
                    // ✅ Futures filter (Index Futures)
                    else if (exchange == "NFO" && segment == "NFO-FUT" && instrumentType == "FUT")
                    {
                        var instrument = ZerodhaInstrumentCsvParser.ParseNFOInstrument(parts);
                        nfoIndexFutures.Add(instrument);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing line {i}: {ex.Message}");
                }
            }

            return (nseIndices, nfoIndexOptions, nfoIndexFutures);
        }

        private NFOInstrument ParseNFOOption(string[] parts)
        {
            DateTime expiry = DateTime.ParseExact(parts[5], "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .AddHours(15).AddMinutes(30); // Market close time

            var tradingSymbol = parts[2].ToUpperInvariant();
            var optionType = tradingSymbol.EndsWith("CE") ? OptionType.CE :
                             tradingSymbol.EndsWith("PE") ? OptionType.PE :
                             throw new InvalidOperationException($"Unknown option type for {tradingSymbol}");

            double strikePrice = 0;
            if (!string.IsNullOrWhiteSpace(parts[6]))
                strikePrice = double.Parse(parts[6], CultureInfo.InvariantCulture);

            return new NFOInstrument(
                Exchange: parts[1], // NFO
                Token: uint.Parse(parts[0]),
                LotSize: int.Parse(parts[8]),
                Symbol: parts[3],   // "name" field in Zerodha CSV
                TradingSymbol: parts[2],
                Expiry: expiry,
                Instrument: BaseInstrument.ParseInstrumentType(parts[9]),
                OptionType: optionType,
                StrikePrice: strikePrice,
                TickSize: double.Parse(parts[7], CultureInfo.InvariantCulture)
            );
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
