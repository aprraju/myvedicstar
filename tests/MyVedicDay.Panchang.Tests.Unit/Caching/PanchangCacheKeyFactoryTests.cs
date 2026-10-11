using Microsoft.Extensions.Options;
using MyVedicDay.Location;
using MyVedicDay.Panchang.Business.Caching;
using MyVedicDay.Panchang.Business.Daily;
using MyVedicDay.Panchang.Calculation;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Unit.Caching;

public sealed class PanchangCacheKeyFactoryTests
{
    private readonly PanchangCacheKeyFactory _factory = new(Options.Create(new LocationOptions()));

    [Fact]
    public void Create_CombinesLocationTimeZoneLocalDateTraditionAndVersion()
    {
        var context = new PanchangRequestContext(
            new ResolvedLocation(17.39, 78.49, DateTimeZoneProviders.Tzdb["Asia/Kolkata"]), "drik");

        var key = _factory.Create(context, new LocalDate(2026, 9, 28));

        Assert.Equal($"panchang:17.39:78.49:Asia/Kolkata:2026-09-28:drik:{PanchangCalculationVersion.Current}:d{PanchangCacheKeyFactory.DataSchemaVersion}", key);
    }

    [Fact]
    public void Create_FormatsNegativeCoordinatesWithFixedPrecision()
    {
        var context = new PanchangRequestContext(
            new ResolvedLocation(30.3, -97.74, DateTimeZoneProviders.Tzdb["America/Chicago"]), "drik");

        var key = _factory.Create(context, new LocalDate(2026, 1, 5));

        Assert.StartsWith("panchang:30.30:-97.74:America/Chicago:2026-01-05:", key, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_DiffersPerLocalDate()
    {
        var context = new PanchangRequestContext(
            new ResolvedLocation(17.39, 78.49, DateTimeZoneProviders.Tzdb["Asia/Kolkata"]), "drik");

        Assert.NotEqual(_factory.Create(context, new LocalDate(2026, 9, 28)), _factory.Create(context, new LocalDate(2026, 9, 29)));
    }
}
