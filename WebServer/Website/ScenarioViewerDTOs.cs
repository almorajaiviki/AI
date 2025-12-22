namespace Server
{
    public sealed record ScenarioViewerSnapshotDto(
        IReadOnlyList<ScenarioDto> Scenarios
    );

        public sealed record ScenarioDto(
            string ScenarioName,
            IReadOnlyList<ScenarioTradeDto> Trades,
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
        double TotalNPV
    );
}