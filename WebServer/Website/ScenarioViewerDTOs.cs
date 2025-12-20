namespace Server
{
    public sealed record ScenarioViewerSnapshotDto(
        IReadOnlyList<ScenarioDto> Scenarios
    );

    public sealed record ScenarioDto(
        string ScenarioName,
        IReadOnlyList<ScenarioTradeDto> Trades
    );

    public sealed record ScenarioTradeDto(
        string TradingSymbol,
        int Lots,
        int Quantity,

        double NPV,
        double Delta,
        double Gamma,

        double Vega,
        double Vanna,
        double Volga,
        double Correl,

        double Theta,
        double Rho
    );
}