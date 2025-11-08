namespace BrokerInterfaces
{
    public interface IBrokerWebSocketService<TSubscriptionAck> : IDisposable
    {
        // Singleton accessor
        static abstract IBrokerWebSocketService<TSubscriptionAck> Instance(
            string userId,
            string clientId,
            string authToken);

        // Events
        event Action<PriceFeedUpdate> OnPriceFeedUpdate;

        // Connection & subscription
        Task ConnectAsync();
        Task SubscribeToBatchAsync(IEnumerable<(string Exchange, uint Token)> instruments);

        // Control feed processing
        void SetProcessFeedMessages(bool enable);
    }
}
