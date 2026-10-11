using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicDay.Astronomy;
using MyVedicDay.Caching;
using MyVedicDay.Location;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;
using NodaTime;
using NodaTime.Text;

namespace MyVedicDay.Muhurta.Business.Daily;

/// <summary>The cached, language-neutral evaluation of one day for one activity in one tradition.</summary>
internal sealed record MuhurtaDayData(
    DateOnly Date,
    string DayStatus,
    IReadOnlyList<MuhurtaSegmentData> Segments,
    IReadOnlyList<int> GoodTimes,
    string? NoGoodTimeReason);

internal sealed record MuhurtaSegmentData(
    DateTimeOffset Start,
    DateTimeOffset End,
    string Status,
    int Score,
    bool IsGoodTime,
    IReadOnlyList<MuhurtaReasonData> Reasons,
    IReadOnlyList<MuhurtaConflictData> Conflicts);

internal sealed record MuhurtaReasonData(string Type, string Code, string? Subject);

internal sealed record MuhurtaConflictData(
    string Kind,
    string Code,
    string Severity,
    bool WithFavorable,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset OverlapStart,
    DateTimeOffset OverlapEnd);

/// <summary>Everything that identifies one evaluation apart from the date.</summary>
internal sealed record MuhurtaRequestContext(ResolvedLocation Location, string Tradition, ActivityRuleProfile Profile, string RulesVersion, Nakshatra? BirthNakshatra = null);

/// <summary>
/// Cache-aside evaluation of days, one entry per location, date, activity and tradition, expiring at the location's
/// next local midnight (ADR-0003). Concurrent misses for the same key are coalesced.
/// </summary>
internal sealed partial class MuhurtaDayService(
    IMuhurtaCalculator calculator,
    IDistributedObjectCache cache,
    InFlightRequestCoalescer<MuhurtaDayData> coalescer,
    IClock clock,
    IOptions<LocationOptions> locationOptions,
    IOptions<MuhurtaOptions> options,
    ILogger<MuhurtaDayService> logger)
{
    /// <summary>Version of the cached <see cref="MuhurtaDayData"/> shape. Bump when it changes.</summary>
    public const string DataSchemaVersion = "1";

    // Built once at construction time; the format string does not change during the lifetime of the process.
    private readonly string _coordinateFormat =
        "F" + locationOptions.Value.CoordinateDecimalPlaces.ToString(CultureInfo.InvariantCulture);

    public async Task<MuhurtaDayData> GetDayAsync(MuhurtaRequestContext context, LocalDate date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = CreateKey(context, date);
        if (await cache.GetAsync<MuhurtaDayData>(key, cancellationToken) is { } hit && hit.Date == date.ToDateOnly())
        {
            LogDayResolved(context.Profile.Activity, true);
            return hit;
        }

        LogDayResolved(context.Profile.Activity, false);
        return await coalescer.GetOrStartAsync(key, () => CalculateAndCacheAsync(context, date, key)).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// <c>muhurta:{latitude}:{longitude}:{timeZone}:{date}:{activity}:{tradition}:{rulesVersion}:{muhurtaVersion}:{panchangVersion}:d{schema}</c>.
    /// </summary>
    public string CreateKey(MuhurtaRequestContext context, LocalDate date)
    {
        var key = string.Join(
            ':',
            "muhurta",
            context.Location.Latitude.ToString(_coordinateFormat, CultureInfo.InvariantCulture),
            context.Location.Longitude.ToString(_coordinateFormat, CultureInfo.InvariantCulture),
            context.Location.TimeZone.Id,
            LocalDatePattern.Iso.Format(date),
            context.Profile.Activity,
            context.Tradition,
            context.RulesVersion,
            MuhurtaCalculationVersion.Current,
            PanchangCalculationVersion.Current,
            "d" + DataSchemaVersion);
        // Keep standard keys unchanged. Version the personal policy separately from the unchanged base engine.
        return context.BirthNakshatra is { } birth ? $"{key}:birth-nakshatra:{birth}:tara-v1" : key;
    }

    private async Task<MuhurtaDayData> CalculateAndCacheAsync(MuhurtaRequestContext context, LocalDate date, string key)
    {
        var location = context.Location;
        var started = Stopwatch.GetTimestamp();
        MuhurtaEvaluation evaluation;
        try
        {
            evaluation = calculator.Evaluate(new MuhurtaCalculationRequest(
                date, new GeoCoordinate(location.Latitude, location.Longitude), location.TimeZone, context.Profile, context.BirthNakshatra));
        }
        catch (SunEventNotFoundException exception)
        {
            throw new BusinessRuleException(
                "Good times cannot be calculated for this location and date because the sun does not rise or set.", exception);
        }

        LogDayCalculated(context.Profile.Activity, Stopwatch.GetElapsedTime(started).TotalMilliseconds, location.TimeZone.Id);

        var data = ToData(evaluation, location.TimeZone);
        var expiration = LocalDayCacheExpiration.Calculate(
            date, location.TimeZone, clock.GetCurrentInstant(), options.Value.PastDateCacheLifetime, options.Value.MaxCacheLifetime);

        // Shared work: not tied to any single caller's cancellation token.
        await cache.SetAsync(key, data, expiration, CancellationToken.None);
        return data;
    }

    private static MuhurtaDayData ToData(MuhurtaEvaluation evaluation, DateTimeZone zone) =>
        new(
            evaluation.Date.ToDateOnly(),
            evaluation.DayStatus.ToString(),
            evaluation.Segments.Select(segment => new MuhurtaSegmentData(
                ToLocal(segment.Start, zone),
                ToLocal(segment.End, zone),
                segment.Status.ToString(),
                segment.Score,
                segment.IsGoodTime,
                segment.Reasons.Select(reason => new MuhurtaReasonData(reason.Type.ToString().ToLowerInvariant(), reason.Code, reason.Subject)).ToArray(),
                segment.Conflicts.Select(conflict => new MuhurtaConflictData(
                    conflict.Kind.ToString(),
                    conflict.Code,
                    conflict.Severity.ToString(),
                    conflict.WithFavorable,
                    ToLocal(conflict.PeriodStart, zone),
                    ToLocal(conflict.PeriodEnd, zone),
                    ToLocal(conflict.OverlapStart, zone),
                    ToLocal(conflict.OverlapEnd, zone))).ToArray())).ToArray(),
            evaluation.GoodTimes,
            evaluation.NoGoodTimeReason);

    /// <summary>Rounds to the nearest second, like the Panchang times.</summary>
    private static DateTimeOffset ToLocal(Instant instant, DateTimeZone zone)
    {
        var rounded = Instant.FromUnixTimeSeconds((long)Math.Round(instant.ToUnixTimeTicks() / (double)NodaConstants.TicksPerSecond));
        return rounded.InZone(zone).ToDateTimeOffset();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evaluated {Activity} in {CalculationDurationMs:0.00} ms for {TimeZone}")]
    private partial void LogDayCalculated(string activity, double calculationDurationMs, string timeZone);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Muhurta day for {Activity}: cache hit {CacheHit}")]
    private partial void LogDayResolved(string activity, bool cacheHit);
}
