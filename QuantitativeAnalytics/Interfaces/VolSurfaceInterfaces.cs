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
        VolSkewDTO ToDTO();

        IParametricModelSkew FromDTO(VolSkewDTO dto);

        /// <summary>
        /// Returns a list of bumpable parameter names
        /// </summary>
        /// <returns></returns>
        IEnumerable<string> GetBumpParamNames();
    }



    /// <summary>
    /// Unified interface for parameterised volatility surfaces (2D: time × moneyness).
    /// Supports retrieving implied vols, bumping, and serialization.
    /// </summary>
    public interface IParametricModelSurface
    {
        /// <summary>
        /// Return implied volatility at given (timeToExpiry, moneyness).
        /// </summary>
        double GetVol(double timeToExpiry, double moneyness);

        /// <summary>
        /// Return 2D parameter set:
        /// (parameterName, expiryString, value)
        /// </summary>
        IEnumerable<(string parameterName, string expiryString, double value)> GetParameters();

        /// <summary>
        /// Parallel absolute bump across all skews and expiries.
        /// </summary>
        IParametricModelSurface Bump(double bumpAmount);

        /// <summary>
        /// Pointwise bump by parameter name (applies across all expiries).
        /// </summary>
        IParametricModelSurface Bump(string parameterName, double bumpAmount);

        /// <summary>
        /// Apply multiple bumps sequentially.
        /// </summary>
        IParametricModelSurface Bump(IEnumerable<(string parameterName, double bumpAmount)> bumps);

        /// <summary>
        /// Convert the entire surface to a serializable DTO.
        /// </summary>
        VolSurfaceDTO ToDTO();

        /// <summary>
        /// Reconstructs the surface from its DTO representation.
        /// </summary>
        IParametricModelSurface FromDTO(VolSurfaceDTO dto);

        /// <summary>
        /// Returns all bumpable parameter names supported by this surface.
        /// </summary>
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

    /// <summary>
    /// Popular params for volatility models.
    /// </summary>
    public enum VolatilityParam
    {
        Vega,
        Vanna,
        Volga,
        Correl
    }

}