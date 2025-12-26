namespace Server
{
    public sealed record ScenarioViewerSnapshotDto(
        IReadOnlyList<ScenarioDto> Scenarios
    );

    public sealed record ScenarioDto(
        string ScenarioName,
        IReadOnlyList<ScenarioTradeDto> Trades,
        IReadOnlyList<ScenarioTradePnLDto> TradePnL,
        IReadOnlyList<ScenarioValuePointDto> ScenarioValues
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

    public sealed record ScenarioTimeSliceDto(
        double TimeShiftYears,
        IReadOnlyList<double> ForwardShiftsPct,
        IReadOnlyList<double> VolShiftsAbs,
        IReadOnlyList<IReadOnlyList<double>> NpvGrid
    );

    public sealed record ScenarioValuePointDto(
        double TimeShiftYears,
        double ForwardShiftPct,
        double VolShiftAbs,
        double TotalNPV,
        double TotalPnL
    );

    public sealed record ScenarioTradePnLDto
    (
        string TradingSymbol,

        double ActualPnL,

        double ThetaPnL,
        double RfrPnL,

        double DeltaPnL,
        double GammaPnL,

        double VegaPnL,
        double VolgaPnL,
        double VannaPnL,

        double FwdResidualPnL,
        double VolResidualPnL,
        double CrossResidualPnL,

        double ExplainedPnL,
        double UnexplainedPnL
    );
}