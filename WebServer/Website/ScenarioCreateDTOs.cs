namespace Server
{
 
    internal sealed record ScenarioCreateRequestInternal(
        string ScenarioName,
        IEnumerable<OptionLegInternal> Options,
        IEnumerable<FutureLegInternal> Futures
    );

    internal sealed record OptionLegInternal(
        DateTime Expiry,
        double Strike,
        int CallLots,
        int PutLots
    );

    internal sealed record FutureLegInternal(
        DateTime Expiry,
        int Lots
    );

}