namespace BrokerInterfaces
{
    public interface IBrokerInstrumentService<TIndexInstrument, TInstrument>
    {
        // Singleton accessor
        static abstract IBrokerInstrumentService<TIndexInstrument, TInstrument> Instance(HttpClient httpClient);

        // Main fetch
        Task<(TIndexInstrument index, List<TInstrument> options, List<TInstrument> futures, DateTime latestExpiry)>
            GetOptionsForIndexAsync(string indexSymbol, string optionsIndexSymbol, DateTime now);
    }
}
