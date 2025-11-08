using System.Globalization;
using InstrumentStatic;

namespace Zerodha
{
    internal static class ZerodhaInstrumentCsvParser
    {
        public static NSEInstrument ParseNSEInstrument(string[] parts)
        {
            // Zerodha CSV columns:
            // 0 instrument_token, 1 exchange, 2 tradingsymbol, 3 name, 
            // 4 last_price, 5 expiry, 6 strike, 7 tick_size, 
            // 8 lot_size, 9 instrument_type, 10 segment, 11 exchange_token

            return new NSEInstrument(
                Exchange: parts[11], // NSE
                Token: uint.Parse(parts[0]),
                LotSize: int.Parse(parts[8]),
                Symbol: CleanField (parts[3]), // "name" field in Zerodha CSV
                TradingSymbol: parts[2],
                Instrument: BaseInstrument.ParseInstrumentType(parts[9]),
                TickSize: double.Parse(parts[7], CultureInfo.InvariantCulture)
            );
        }

                private static string CleanField(string field)
                {
                    return field.Trim().Trim('"');
                }

        public static NFOInstrument ParseNFOInstrument(string[] parts)
        {
            DateTime? expiry = null;
            if (!string.IsNullOrWhiteSpace(parts[5]))
            {
                // Zerodha expiry format: yyyy-MM-dd
                expiry = DateTime.ParseExact(parts[5], "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    .AddHours(15).AddMinutes(30); // Align to market close
            }

            double strikePrice = 0;
            if (!string.IsNullOrWhiteSpace(parts[6]))
            {
                strikePrice = double.Parse(parts[6], CultureInfo.InvariantCulture);
            }

            var tradingSymbol = parts[2].ToUpperInvariant();
            OptionType? optionType = tradingSymbol.EndsWith("CE") ? OptionType.CE :
                             tradingSymbol.EndsWith("PE") ? OptionType.PE :
                             null;
                             //throw new InvalidOperationException($"Unknown option type for {tradingSymbol}");

            return new NFOInstrument(
                Exchange: parts[11], // NFO
                Token: uint.Parse(parts[0]),
                LotSize: int.Parse(parts[8]),
                Symbol: CleanField(parts[3]), // "name" field in Zerodha CSV
                TradingSymbol: tradingSymbol,
                Expiry: expiry ?? DateTime.MinValue,
                Instrument: BaseInstrument.ParseInstrumentType(parts[9]),
                OptionType: optionType,
                StrikePrice: strikePrice,
                TickSize: double.Parse(parts[7], CultureInfo.InvariantCulture)
            );
        }
    }
}
