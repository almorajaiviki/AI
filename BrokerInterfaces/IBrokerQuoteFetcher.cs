namespace BrokerInterfaces
{
    public interface IBrokerQuoteFetcher<TRequest, TResponse>
    {
        // Singleton accessor
        static abstract IBrokerQuoteFetcher<TRequest, TResponse> Instance(
            IBrokerAuthService<TRequest, TResponse> authService,
            HttpClient httpClient);

        // Fetch quotes for one or more instruments
        Task<List<InstrumentQuote>> FetchQuotesAsync(List<QuoteRequest> requests);
    }
}
