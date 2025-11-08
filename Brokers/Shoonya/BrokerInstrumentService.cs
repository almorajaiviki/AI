using System.IO.Compression;
using BrokerInterfaces;
using InstrumentStatic;
using System.Globalization;

namespace Shoonya
{
    public sealed class BrokerInstrumentService 
        : IBrokerInstrumentService<NSEInstrument, NFOInstrument, NFOInstrument>, IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.shoonya.com";

        private static readonly Dictionary<string, string> FileUrls = new()
        {
            { "NSE", $"{BaseUrl}/NSE_symbols.txt.zip" },
            { "NFO", $"{BaseUrl}/NFO_symbols.txt.zip" }
        };

        // --- Singleton implementation ---
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

        public async Task<(NSEInstrument index, List<NFOInstrument> options, DateTime latestExpiry)>
            GetOptionsForIndexAsync(string indexSymbol, string optionsIndexSymbol, DateTime now)
        {
            var (nseIndices, nfoIndexOptions) = await LoadFilteredInstrumentsAsync();

            var index = nseIndices
                .FirstOrDefault(inst => inst.Symbol.Equals(indexSymbol, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"NSE INDEX with symbol '{indexSymbol}' not found.");

            var filteredOptions = nfoIndexOptions
                .Where(opt => opt.Symbol.Equals(optionsIndexSymbol, StringComparison.OrdinalIgnoreCase) && opt.Expiry >= now)
                .ToList();

            if (!filteredOptions.Any())
                throw new InvalidOperationException($"No options found for symbol {optionsIndexSymbol} after {now}");

            var latestExpiryDate = filteredOptions.Select(o => o.Expiry.Date).Min();
            var latestExpiryOptions = filteredOptions.Where(o => o.Expiry.Date == latestExpiryDate).ToList();

            var strikeFilteredOptions = latestExpiryOptions.Where(opt => opt.StrikePrice % 100 == 0).ToList();

            return (index, strikeFilteredOptions, latestExpiryDate);
        }

        public static NSEInstrument NSEInstrumentFromCsvLine(string line)
        {
            string[] parts = line.Split(',');
            return new NSEInstrument(
                Exchange: parts[0],
                Token: uint.Parse(parts[1]),
                LotSize: int.Parse(parts[2]),
                Symbol: parts[3],
                TradingSymbol: parts[4],
                Instrument: BaseInstrument.ParseInstrumentType(parts[5]),
                TickSize: double.Parse(parts[6])
            );
        }

        public static NFOInstrument NFOInstrumentFromCsvLine(string line)
        {
            string[] parts = line.Split(',');
            return new NFOInstrument(
                Exchange: parts[0],
                Token: uint.Parse(parts[1]),
                LotSize: int.Parse(parts[2]),
                Symbol: parts[3],
                TradingSymbol: parts[4],
                Expiry: DateTime.ParseExact(parts[5], "dd-MMM-yyyy", CultureInfo.CurrentCulture)
                            .AddHours(15).AddMinutes(30),
                Instrument: BaseInstrument.ParseInstrumentType(parts[6]),
                OptionType: BaseInstrument.ParseOptionType(parts[7]),
                StrikePrice: string.IsNullOrEmpty(parts[8]) ? 0.0 : double.Parse(parts[8]),
                TickSize: double.Parse(parts[9])
            );
        }

        private async Task<(List<NSEInstrument> nseIndices, List<NFOInstrument> nfoIndexOptions)> 
            LoadFilteredInstrumentsAsync()
        {
            var nseIndices = new List<NSEInstrument>();
            var nfoIndexOptions = new List<NFOInstrument>();

            foreach (var (market, url) in FileUrls)
            {
                string tempZipPath = Path.GetTempFileName();
                string extractDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                try
                {
                    await DownloadFileAsync(url, tempZipPath);

                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(tempZipPath, extractDir);

                    string csvFile = Path.Combine(extractDir, $"{market}_symbols.txt");
                    if (!File.Exists(csvFile))
                        continue;

                    var lines = await File.ReadAllLinesAsync(csvFile);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(lines[i])) continue;

                            if (market == "NSE")
                            {
                                var instrument = NSEInstrumentFromCsvLine(lines[i]);
                                if (instrument.Instrument == InstrumentType.INDEX)
                                    nseIndices.Add(instrument);
                            }
                            else
                            {
                                var instrument = NFOInstrumentFromCsvLine(lines[i]);
                                if (instrument.Instrument == InstrumentType.OPTIDX)
                                    nfoIndexOptions.Add(instrument);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing line {i} in {market}: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                    if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
                }
            }

            return (nseIndices, nfoIndexOptions);
        }

        private async Task DownloadFileAsync(string url, string savePath)
        {
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var fs = new FileStream(savePath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
