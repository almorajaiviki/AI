using System.IO.Compression;

namespace InstrumentStatic
{
    public interface IInstrumentProvider
    {
        Task<(NSEInstrument index, List<NFOInstrument> options, DateTime latestExpiry)> GetNfoOptionsForNseIndexAsync(string symbol, DateTime now);
    }

    // Add to InstrumentStatic namespace
    public class InstrumentProvider : IInstrumentProvider
    {
        
        public Task<(NSEInstrument index, List<NFOInstrument> options, DateTime latestExpiry)> GetNfoOptionsForNseIndexAsync(string symbol, DateTime now)
            => InstrumentDownloader.GetIndexOptionsWithLatestExpiryAsync(symbol, now);
    }

    public static class InstrumentDownloader 
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string BaseUrl = "https://api.shoonya.com";

        private static readonly Dictionary<string, string> FileUrls = new()
        {
            { "NSE", $"{BaseUrl}/NSE_symbols.txt.zip" },
            { "NFO", $"{BaseUrl}/NFO_symbols.txt.zip" }
        };

        public static async Task<(List<NSEInstrument> nseIndices, List<NFOInstrument> nfoIndexOptions)> LoadFilteredInstrumentsAsync()
        {
            var nseIndices = new List<NSEInstrument>();
            var nfoIndexOptions = new List<NFOInstrument>();

            foreach (var (market, url) in FileUrls)
            {
                string tempZipPath = Path.GetTempFileName();
                string extractDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                try
                {
                    // 1. Download ZIP
                    await DownloadFileAsync(url, tempZipPath);

                    // 2. Extract and parse
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(tempZipPath, extractDir);

                    string csvFile = Path.Combine(extractDir, $"{market}_symbols.txt");
                    if (File.Exists(csvFile))
                    {
                        var lines = File.ReadAllLines(csvFile);
                        for (int i = 1; i < lines.Length; i++) // Skip header
                        {
                            try
                            {
                                if (market == "NSE")
                                {
                                    var instrument = NSEInstrument.FromCsvLine(lines[i]);
                                    if (instrument.Instrument == InstrumentType.INDEX)
                                        nseIndices.Add(instrument);
                                }
                                else // NFO
                                {
                                    var instrument = NFOInstrument.FromCsvLine(lines[i]);
                                    if (instrument.Instrument == InstrumentType.OPTIDX)
                                        nfoIndexOptions.Add(instrument);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error parsing line {i}: {ex.Message}");
                            }
                        }
                    }
                }
                finally
                {
                    // 3. Cleanup
                    if (File.Exists(tempZipPath))
                        File.Delete(tempZipPath);
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, recursive: true);
                }
            }

            return (nseIndices, nfoIndexOptions);
        }

        // Make this private (reused internally)
        private static async Task<NSEInstrument> GetNseIndexBySymbolAsync(string symbol)
        {
            var (nseIndices, _) = await LoadFilteredInstrumentsAsync();
            return nseIndices.FirstOrDefault(inst => 
                inst.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)) 
                ?? throw new KeyNotFoundException($"NSE INDEX with symbol '{symbol}' not found.");
        }

        // Add this method to the InstrumentDownloader class
        public static async Task<(NSEInstrument index, List<NFOInstrument> options, DateTime latestExpiry)> 
        GetIndexOptionsWithLatestExpiryAsync(string nseIndexSymbol, DateTime now)
        {
            // 1. Get all filtered options (existing logic)
            var (index, allOptions) = await GetNfoOptionsForNseIndexAsync(nseIndexSymbol, now);

            // 2. Filter for the nearest expiry date (3:30 PM)
            var latestExpiryDate = allOptions
                .Select(opt => opt.Expiry.Date)
                .Min();

            var latestExpiryDateTime = latestExpiryDate.AddHours(15).AddMinutes(30); // 3:30 PM
            var latestExpiryOptions = allOptions
                .Where(opt => opt.Expiry.Date == latestExpiryDate)
                .ToList();

            return (index, latestExpiryOptions, latestExpiryDateTime);
        }

        // New public method with expiry filtering and tuple return
        private static async Task<(NSEInstrument nseIndex, List<NFOInstrument> nfoOptions)> 
        GetNfoOptionsForNseIndexAsync(string nseIndexSymbol, DateTime now)
        {
            var nseIndex = await GetNseIndexBySymbolAsync(nseIndexSymbol);
            var (_, allNfoOptions) = await LoadFilteredInstrumentsAsync();

            var filteredOptions = allNfoOptions
                .Where(opt => 
                    opt.Symbol.Equals(nseIndex.Symbol, StringComparison.OrdinalIgnoreCase) &&
                    opt.Expiry >= now)
                .ToList();

            return (nseIndex, filteredOptions);
        }
        private static async Task DownloadFileAsync(string url, string savePath)
        {
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var fs = new FileStream(savePath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
        }
    }
}