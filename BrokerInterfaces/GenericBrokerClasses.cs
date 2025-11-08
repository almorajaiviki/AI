
namespace BrokerInterfaces
{
    public readonly struct PriceFeedUpdate
    {
        public uint Token { get; }
        public double LastTradedPrice { get; }
        public double? BidPrice { get; }
        public double? AskPrice { get; }
        public double? OpenInterest { get; }
        public PriceFeedUpdate(uint token, double ltp, double? bid, double? ask, double? oi)
        {
            Token = token;
            LastTradedPrice = ltp;
            BidPrice = bid;
            AskPrice = ask;
            OpenInterest = oi;
        }
    }

    public record QuoteRequest(string Exchange, string Token, string Symbol);

    public record InstrumentQuote(
        string Token,
        double LastTradedPrice,
        double BidPrice,
        double AskPrice,
        double OpenInterest
    );
}