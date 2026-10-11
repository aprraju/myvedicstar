namespace MyVedicDay.Panchang.Calculation;

/// <summary>
/// Lists the Panchang traditions the calculation engine supports.
/// </summary>
public interface IPanchangTraditionCatalog
{
    bool IsSupported(string tradition);
}
