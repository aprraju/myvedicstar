namespace MyVedicDay.Panchang.Calculation.Traditions;

/// <summary>
/// Resolves the registered tradition strategy for a tradition key.
/// </summary>
internal sealed class PanchangTraditionStrategyProvider : IPanchangTraditionCatalog
{
    private readonly Dictionary<string, IPanchangTraditionStrategy> _strategies;

    public PanchangTraditionStrategyProvider(IEnumerable<IPanchangTraditionStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(strategy => strategy.Key, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSupported(string tradition) => _strategies.ContainsKey(tradition);

    public IPanchangTraditionStrategy GetStrategy(string tradition) =>
        _strategies.TryGetValue(tradition, out var strategy)
            ? strategy
            : throw new ArgumentOutOfRangeException(nameof(tradition), tradition, "Unsupported Panchang tradition.");
}
