using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Location.Integration.GeoNames;

/// <summary>
/// Offline geocoding over the embedded GeoNames city dataset. No API key, no network calls and no per-request cost;
/// covers cities with a population of 5,000 or more. Replaceable by an online provider (Azure Maps, Google) behind
/// <see cref="IGeocodingProvider"/>.
/// </summary>
internal sealed partial class GeoNamesGeocodingProvider(ILogger<GeoNamesGeocodingProvider> logger) : IGeocodingProvider
{
    private const string ProviderName = "GeoNames";

    // Immutable reference data embedded in the assembly: loaded once per process and shared.
    private static readonly Lazy<GeoNamesDataset> Dataset = new(GeoNamesDataset.Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public ValueTask<IReadOnlyList<GeocodedPlace>> SearchAsync(PlaceSearch search, int maxResults, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var dataset = GetDataset();
        var started = Stopwatch.GetTimestamp();
        var results = dataset.Search(search, maxResults);
        LogOperation("search", results.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return ValueTask.FromResult(results);
    }

    public ValueTask<GeocodedPlace?> FindNearestAsync(double latitude, double longitude, double maxDistanceKm, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dataset = GetDataset();
        var started = Stopwatch.GetTimestamp();
        var result = dataset.FindNearest(latitude, longitude, maxDistanceKm);
        LogOperation("nearest", result is null ? 0 : 1, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return ValueTask.FromResult(result);
    }

    private GeoNamesDataset GetDataset()
    {
        try
        {
            return Dataset.Value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or FormatException or IOException)
        {
            LogDatasetLoadFailed(ProviderName, exception);
            throw new IntegrationException(ProviderName, "Place dataset could not be loaded.", exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "GeoNames {Operation} returned {ResultCount} result(s) in {DurationMs:0.00} ms")]
    private partial void LogOperation(string operation, int resultCount, double durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Provider} place dataset could not be loaded")]
    private partial void LogDatasetLoadFailed(string provider, Exception exception);
}
