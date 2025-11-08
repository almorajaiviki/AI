namespace MarketData
{
    public readonly struct FutureSpreads
    {
        public uint Token { get; }

        public double BidSpread { get; }
        public double AskSpread { get; }

        public FutureSpreads(FutureSnapshot future, FutureGreeks greeks)
        {
            Token = future.Token;
            BidSpread = greeks.NPV - future.Bid;
            AskSpread = future.Ask - greeks.NPV;
        }
    }

    public class FutureSpreadsDTO
    {
        public double BidSpread { get; set; }
        public double AskSpread { get; set; }

        public FutureSpreadsDTO(FutureSpreads spreads)
        {
            BidSpread = spreads.BidSpread;
            AskSpread = spreads.AskSpread;
        }
    }
}
