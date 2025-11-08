namespace QuantitativeAnalytics
{
    /// <summary>
    /// Unified interface for parameterised volatility surfaces.
    /// Supports retrieving implied vols and bumping parameters.
    /// </summary>
    public interface IParametricModelSkew
    {
        /// <summary>
        /// Return implied vol at a given moneyness (K/F or delta proxy).
        /// </summary>
        double GetVol(double moneyness);

        /// <summary>Returns a parameter dictionary (name -> value).</summary>
        IReadOnlyDictionary<string, double> GetParameters();

        /// <summary>Parallel bump of the surface (absolute vol add).</summary>
        IParametricModelSkew Bump(double bumpAmount);

        /// <summary>Pointwise bump by parameter name.</summary>
        IParametricModelSkew Bump(string parameterName, double bumpAmount);

        /// <summary>
        /// Pointwise bump multiple parameters.
        /// The bumps are applied sequentially, one after another.
        /// </summary>
        IParametricModelSkew Bump(IEnumerable<(string parameterName, double bumpAmount)> bumps);

        /// <summary> convert surface data to DTO for serialization </summary>
        VolSurfaceDTO ToDTO();

        IParametricModelSkew FromDTO(VolSurfaceDTO dto);

        /// <summary>
        /// Returns a list of bumpable parameter names
        /// </summary>
        /// <returns></returns>
        IEnumerable<string> GetBumpParamNames();
    }

    /// <summary>
    /// Supported option pricing volatility models.
    /// </summary>
    public enum VolatilityModel
    {
        Black76,
        Heston
    }
}