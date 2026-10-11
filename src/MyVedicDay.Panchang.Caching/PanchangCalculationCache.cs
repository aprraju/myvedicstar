using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicStar.Caching;
using NodaTime;
using NodaTime.Text;
namespace MyVedicDay.Panchang.Caching;
public interface IPanchangCalculationCache
{
    Task<PanchangDay> CalculateAsync(PanchangCalculationRequest request, CancellationToken cancellationToken);
}
public sealed class PanchangCalculationCache(IPanchangCalculator calculator, IApplicationCache cache, TimeProvider time) : IPanchangCalculationCache
{
    private sealed record Payload(string Json);
    private static readonly JsonSerializerOptions Serialization = CreateOptions();
    public async Task<PanchangDay> CalculateAsync(PanchangCalculationRequest request, CancellationToken cancellationToken)
    {
        var key = CacheKeyBuilder.Create("calculation", new Dictionary<string,string?>
        {
            ["date"] = LocalDatePattern.Iso.Format(request.Date),
            ["latitude"] = request.Location.Latitude.ToString("R", CultureInfo.InvariantCulture),
            ["longitude"] = request.Location.Longitude.ToString("R", CultureInfo.InvariantCulture),
            ["timezone"] = request.TimeZone.Id,
            ["tzdb"] = DateTimeZoneProviders.Tzdb.VersionId,
            ["method"] = request.Tradition,
            ["engine"] = PanchangCalculationVersion.Current,
            ["schema"] = "1",
        });
        var payload = await cache.GetOrCreateAsync(key, token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new Payload(JsonSerializer.Serialize(calculator.Calculate(request), Serialization)));
        }, time.GetUtcNow().AddDays(30), cancellationToken);
        try { return JsonSerializer.Deserialize<PanchangDay>(payload.Json, Serialization) ?? throw new JsonException("Invalid Panchang payload."); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            await cache.RemoveAsync<Payload>(key, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return calculator.Calculate(request);
        }
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new InstantConverter());
        options.Converters.Add(new LocalDateConverter());
        options.Converters.Add(new ZoneConverter());
        return options;
    }
    private sealed class InstantConverter : JsonConverter<Instant>
    {
        public override Instant Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => InstantPattern.ExtendedIso.Parse(reader.GetString()!).Value;
        public override void Write(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options) => writer.WriteStringValue(InstantPattern.ExtendedIso.Format(value));
    }
    private sealed class LocalDateConverter : JsonConverter<LocalDate>
    {
        public override LocalDate Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => LocalDatePattern.Iso.Parse(reader.GetString()!).Value;
        public override void Write(Utf8JsonWriter writer, LocalDate value, JsonSerializerOptions options) => writer.WriteStringValue(LocalDatePattern.Iso.Format(value));
    }
    private sealed class ZoneConverter : JsonConverter<DateTimeZone>
    {
        public override DateTimeZone Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => DateTimeZoneProviders.Tzdb[reader.GetString()!];
        public override void Write(Utf8JsonWriter writer, DateTimeZone value, JsonSerializerOptions options) => writer.WriteStringValue(value.Id);
    }
}
public static class PanchangCalculationCacheExtensions
{
    public static IServiceCollection AddPanchangCalculationCache(this IServiceCollection services)
    {
        services.TryAddSingleton<IPanchangCalculationCache, PanchangCalculationCache>();
        return services;
    }
}
