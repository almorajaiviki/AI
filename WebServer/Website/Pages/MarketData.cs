/* using Mkt = MarketData; 
using System.Text;

namespace LocalWebServer.Website.Pages;

public class MarketData
{
    public static string GetFinalHtml(string path, Mkt.MarketData marketData)
{
    string html = File.ReadAllText(path);

    var snapshotDto = marketData.AtomicSnapshot.ToDTO(); // only once, thread-safe

    html = html.Replace("{$MarketDataHtml}", 
        GetMarketInfoTable(snapshotDto) + GetOptionChainTable(snapshotDto) + GetFuturesTable(snapshotDto));

    html = html.Replace("{$IndexName}", snapshotDto.Index);

    return html;
}

    public static string GetMarketInfoTable(Mkt.AtomicMarketSnapDTO dto)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<h3>Market Snapshot</h3>");
        sb.AppendLine("<table border='1' style='border-collapse:collapse; width:100%; text-align:center;'>");

        // Header row
        sb.AppendLine("<tr>");
        sb.AppendLine("<th>Index</th>");
        sb.AppendLine("<th>Spot</th>");
        sb.AppendLine("<th>Implied Future</th>");
        sb.AppendLine("<th>RFR</th>");
        sb.AppendLine("<th>Div Yield</th>");
        sb.AppendLine("<th>Expiry</th>");
        sb.AppendLine("<th>Snap Time</th>");
        sb.AppendLine("</tr>");

        // Value row
        sb.AppendLine("<tr>");
        sb.AppendLine($"<td>{dto.Index}</td>");
        sb.AppendLine($"<td id='IndexSpot'>{dto.Spot.ToString("N2")}</td>");
        sb.AppendLine($"<td id='ImpFut'>{dto.ImpliedFuture.ToString("N2")}</td>");
        sb.AppendLine($"<td id='RFR'>{dto.RiskFreeRate.ToString("P2")}</td>");
        sb.AppendLine($"<td id='DivYield'>{dto.DivYield.ToString("P2")}</td>");
        sb.AppendLine($"<td>{dto.Expiry:dd-MMM-yyyy}</td>");
        sb.AppendLine($"<td id='LastUpdate'>{dto.SnapTime:HH:mm:ss.fff}</td>");
        sb.AppendLine("</tr>");

        sb.AppendLine("</table>");
        return sb.ToString();
    }    
    
    public static string GetOptionChainTable(Mkt.AtomicMarketSnapDTO dto)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<br />");
        sb.AppendLine("<h3>Option Chain (IV View)</h3>");
        sb.AppendLine("<table border='1' style='border-collapse:collapse; width:100%; text-align:center;'>");

        // Super header row
        sb.AppendLine("<tr>");
        sb.AppendLine("<th colspan='7'>Calls</th>");
        sb.AppendLine("<th rowspan='2'>Strike<br />(% of Forward)</th>");
        sb.AppendLine("<th colspan='7'>Puts</th>");
        sb.AppendLine("</tr>");

        // Column headers row
        sb.AppendLine("<tr>");

        // Call headers
        sb.AppendLine("<th>OI</th>");
        sb.AppendLine("<th>Bid</th>");
        sb.AppendLine("<th>BidSprd</th>");
        sb.AppendLine("<th>NPV</th>");
        sb.AppendLine("<th>AskSprd</th>");
        sb.AppendLine("<th>Ask</th>");
        sb.AppendLine("<th>IV Used</th>");

        // Put headers
        sb.AppendLine("<th>IV Used</th>");
        sb.AppendLine("<th>Bid</th>");
        sb.AppendLine("<th>BidSprd</th>");
        sb.AppendLine("<th>NPV</th>");
        sb.AppendLine("<th>AskSprd</th>");
        sb.AppendLine("<th>Ask</th>");
        sb.AppendLine("<th>OI</th>");

        sb.AppendLine("</tr>");

        var atmPair = dto.OptionPairs.OrderBy(p => Math.Abs(p.Call.Strike - dto.ImpliedFuture)).FirstOrDefault();
        double atmStrike = atmPair?.Call.Strike ?? dto.ImpliedFuture;

        foreach (var pair in dto.OptionPairs.OrderBy(p => p.Call.Strike))
        {
            double strike = pair.Call.Strike;
            double impFut = dto.ImpliedFuture;
            double strikePct = strike / impFut;

            bool isATM = Math.Abs(strike - atmStrike) < 1e-6;
            bool callIsITM = strike < impFut;
            bool putIsITM = strike > impFut;

            string boldStart = isATM ? "<b>" : "";
            string boldEnd = isATM ? "</b>" : "";

            string callStyle = callIsITM ? "" : " style='background-color:#f0f0f0;'";
            string putStyle = putIsITM ? "" : " style='background-color:#f0f0f0;'";

            string callId = pair.Call.TradingSymbol;
            string putId = pair.Put.TradingSymbol;

            sb.AppendLine("<tr>");

            // --- Call side (OI, Bid, BidSprd, NPV, AskSprd, Ask, IV_Used, IV)
            sb.AppendLine($"<td id='{callId}_oi'{callStyle}>{boldStart}{pair.Call.OI.ToString("N0")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{callId}_bid'{callStyle}>{boldStart}{pair.Call.Bid.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{callId}_bidsprd'{callStyle}>{boldStart}{pair.CallSpreads.BidSpread.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{callId}_npv'{callStyle}>{boldStart}{pair.CallGreeks.NPV.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{callId}_asksprd'{callStyle}>{boldStart}{pair.CallSpreads.AskSpread.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{callId}_ask'{callStyle}>{boldStart}{pair.Call.Ask.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{callId}_ivused'{callStyle}>{boldStart}{pair.CallGreeks.IV_Used.ToString("P2")}{boldEnd}</td>");

            // --- Strike column
            sb.AppendLine($"<td>{boldStart}{strike.ToString("N2")}<br />(@{strikePct.ToString("P2")}){boldEnd}</td>");

            // --- Put side (IV, IV_Used, Bid, BidSprd, NPV, AskSprd, Ask, OI)
            sb.AppendLine($"<td id='{putId}_ivused'{putStyle}>{boldStart}{pair.PutGreeks.IV_Used.ToString("P2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{putId}_bid'{putStyle}>{boldStart}{pair.Put.Bid.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{putId}_bidsprd'{putStyle}>{boldStart}{pair.PutSpreads.BidSpread.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{putId}_npv'{putStyle}>{boldStart}{pair.PutGreeks.NPV.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{putId}_asksprd'{putStyle}>{boldStart}{pair.PutSpreads.AskSpread.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{putId}_ask'{putStyle}>{boldStart}{pair.Put.Ask.ToString("N2")}{boldEnd}</td>");
            sb.AppendLine($"<td id='{putId}_oi'{putStyle}>{boldStart}{pair.Put.OI.ToString("N0")}{boldEnd}</td>");

            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        return sb.ToString();
    }

    public static string GetFuturesTable(Mkt.AtomicMarketSnapDTO dto)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<br />");
        sb.AppendLine("<h3>Futures</h3>");
        sb.AppendLine("<table border='1' style='border-collapse:collapse; width:100%; text-align:center;'>");

        // Header row
        sb.AppendLine("<tr>");
        sb.AppendLine("<th>Future</th>");
        foreach (var future in dto.Futures)
        {
            sb.AppendLine($"<th>{future.FutureSnapshot.TradingSymbol}</th>");
        }
        sb.AppendLine("</tr>");

        // Expiry row
        sb.AppendLine("<tr>");
        sb.AppendLine("<td><b>Expiry</b></td>");
        foreach (var future in dto.Futures)
        {
            sb.AppendLine($"<td>{future.FutureSnapshot.Expiry:dd-MMM-yyyy}</td>");
        }
        sb.AppendLine("</tr>");

        // Bid row
        sb.AppendLine("<tr>");
        sb.AppendLine("<td><b>Bid</b></td>");
        foreach (var future in dto.Futures)
        {
            sb.AppendLine($"<td id='{future.FutureSnapshot.TradingSymbol}_Bid'>{future.FutureSnapshot.Bid:N2}</td>");
        }
        sb.AppendLine("</tr>");

        // NPV row
        sb.AppendLine("<tr>");
        sb.AppendLine("<td><b>NPV</b></td>");
        foreach (var future in dto.Futures)
        {
            sb.AppendLine($"<td id='{future.FutureSnapshot.TradingSymbol}_NPV'>{future.FutureGreeks.NPV:N2}</td>");
        }
        sb.AppendLine("</tr>");

        // Ask row
        sb.AppendLine("<tr>");
        sb.AppendLine("<td><b>Ask</b></td>");
        foreach (var future in dto.Futures)
        {
            sb.AppendLine($"<td id='{future.FutureSnapshot.TradingSymbol}_Ask'>{future.FutureSnapshot.Ask:N2}</td>");
        }
        sb.AppendLine("</tr>");

        // OI row
        sb.AppendLine("<tr>");
        sb.AppendLine("<td><b>OI</b></td>");
        foreach (var future in dto.Futures)
        {
            sb.AppendLine($"<td id='{future.FutureSnapshot.TradingSymbol}_OI'>{future.FutureSnapshot.OI:N0}</td>");
        }
        sb.AppendLine("</tr>");

        sb.AppendLine("</table>");
        return sb.ToString();
    }

} */