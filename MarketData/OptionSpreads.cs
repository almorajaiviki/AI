namespace MarketData
{
    public readonly struct OptionSpreads
    {
        public uint Token { get; }

        public double BidSpread { get; }
        public double AskSpread { get; }

        public OptionSpreads(OptionSnapshot option, OptionGreeks greeks)
        {
            Token = option.Token;
            BidSpread = greeks.NPV - option.Bid;
            AskSpread = option.Ask - greeks.NPV;
        }
    }
    public class OptionSpreadsDTO
    {
        public double BidSpread { get; set; }
        public double AskSpread { get; set; }

        public OptionSpreadsDTO(OptionSpreads spreads)
        {
            BidSpread = spreads.BidSpread;
            AskSpread = spreads.AskSpread;
        }
    }
    
}
