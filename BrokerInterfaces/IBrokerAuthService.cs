namespace BrokerInterfaces
{
    public interface IBrokerAuthService<TRequest, TResponse> : IDisposable
    {
        // Current session data
        TRequest? CurrentAuthRequest { get; }
        TResponse? CurrentAuthResponse { get; }

        // Singleton accessor
        static abstract IBrokerAuthService<TRequest, TResponse> Instance(HttpClient httpClient);

        // Login / logout
        Task<TResponse> LoginAsync(TRequest request, CancellationToken ct = default);
        Task<bool> LogoutAsync(CancellationToken ct = default);
    }
}
