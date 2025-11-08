using System;
using System.Collections.Generic;
using CalendarLib;  // Reference to CalendarLib library

namespace MarketHelperDict
{
    public class MarketInfo
    {
        public readonly string IndexSymbol, IndexTradingSymbol, IndexSymbol_Futures, IndexSymbol_Options, Segment;
        public readonly uint LotSize;
        public readonly int LotsQuantityFreeze;
        public readonly double LowerStrikePct, UpperStrikePct, TickSize;
        public readonly MarketCalendar NSECalendar;

        public MarketInfo(
            string indexSymbol,
            string indexTradingSymbol,
            string indexSymbol_Futures,
            string indexSymbol_Options,
            string segment,
            uint lotSize,
            int lotsFreeze,
            double lowerStrikePct,
            double upperStrikePct,
            double tickSize,
            MarketCalendar calendar)
        {
            IndexSymbol = indexSymbol;
            IndexTradingSymbol = indexTradingSymbol;
            IndexSymbol_Futures = indexSymbol_Futures;
            IndexSymbol_Options = indexSymbol_Options;
            Segment = segment;
            LotSize = lotSize;
            LotsQuantityFreeze = lotsFreeze;
            LowerStrikePct = lowerStrikePct;
            UpperStrikePct = upperStrikePct;
            TickSize = tickSize;
            NSECalendar = calendar;
        }
    }

    public static class MarketHelperDict
    {
        public static readonly Dictionary<string, MarketInfo> MarketInfoDict;

        static MarketHelperDict()
        {
            List<DateTime> holidays = new List<DateTime>
            {
                new DateTime(2025, 02, 26),
                new DateTime(2025, 03, 14),
                new DateTime(2025, 03, 31),
                new DateTime(2025, 04, 10),
                new DateTime(2025, 04, 14),
                new DateTime(2025, 04, 18),
                new DateTime(2025, 05, 01),
                new DateTime(2025, 08, 15),
                new DateTime(2025, 08, 27),
                new DateTime(2025, 10, 02),
                new DateTime(2025, 10, 21),
                new DateTime(2025, 10, 22),
                new DateTime(2025, 11, 05),
                new DateTime(2025, 12, 25)
            };

            List<DayOfWeek> weekendDays = new List<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday };
            TimeSpan marketOpen = new TimeSpan(9, 15, 0);
            TimeSpan marketClose = new TimeSpan(15, 30, 0);

            MarketCalendar NSECalendar = new MarketCalendar(holidays, weekendDays, marketOpen, marketClose);

            MarketInfo NSEMarketInfo = new MarketInfo(
                "NIFTY 50",      // IndexSymbol
                "NIFTY INDEX",   // IndexTradingSymbol
                "NIFTY",         // IndexSymbol_Futures
                "NIFTY",         // IndexSymbol_Options
                "NFO",           // Segment
                75,              // LotSize
                72,              // LotsFreeze
                -0.05,            // LowerStrikePct
                0.05,             // UpperStrikePct
                0.05,            // TickSize
                NSECalendar      // Calendar
            );

            MarketInfoDict = new Dictionary<string, MarketInfo>
            {
                { "NIFTY INDEX", NSEMarketInfo }
            };
        }
    }
}
