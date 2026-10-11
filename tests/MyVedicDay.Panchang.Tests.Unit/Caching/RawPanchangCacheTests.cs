using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Caching;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicStar.Caching;
using NodaTime;
namespace MyVedicDay.Panchang.Tests.Unit.Caching;
public sealed class RawPanchangCacheTests
{
    private sealed class Counting(IPanchangCalculator inner) : IPanchangCalculator
    {
        internal int Calls;
        public PanchangDay Calculate(PanchangCalculationRequest request) { Calls++; return inner.Calculate(request); }
    }
    [Fact]
    public async Task RawCache_RoundTripsExactInstantsAndAllPeriods_WithoutRecalculating()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Caching:Policies:calculation:MemoryLifetime"]="00:05:00" }).Build();
        var services = new ServiceCollection().AddLogging().AddMetrics().AddPanchangCalculation();
        var original = services.Single(s=>s.ServiceType==typeof(IPanchangCalculator));
        services.RemoveAll<IPanchangCalculator>();
        services.AddSingleton<IPanchangCalculator>(sp=>new Counting((IPanchangCalculator)ActivatorUtilities.CreateInstance(sp,original.ImplementationType!)));
        services.AddApplicationCaching(configuration).AddPanchangCalculationCache();
        await using var provider = services.BuildServiceProvider();
        var calculator = (Counting)provider.GetRequiredService<IPanchangCalculator>();
        var cache = provider.GetRequiredService<IPanchangCalculationCache>();
        var request = new PanchangCalculationRequest(new LocalDate(2026,10,11),new GeoCoordinate(17.385,78.4867),DateTimeZoneProviders.Tzdb["Asia/Kolkata"],PanchangTraditions.Drik);
        var expected = calculator.Calculate(request);
        var baselineCalls = calculator.Calls;
        var first = await cache.CalculateAsync(request,CancellationToken.None);
        var second = await cache.CalculateAsync(request,CancellationToken.None);
        Assert.Equal(baselineCalls + 1,calculator.Calls);
        Assert.Equal(expected.Sun,first.Sun); Assert.Equal(expected.Tithis,first.Tithis); Assert.Equal(expected.Nakshatras,first.Nakshatras);
        Assert.Equal(expected.Yogas,first.Yogas); Assert.Equal(expected.Karanas,first.Karanas); Assert.Equal(expected.Periods,first.Periods);
        Assert.Equal(first.Sun,second.Sun);Assert.Equal(first.Tithis,second.Tithis);Assert.Equal(first.Nakshatras,second.Nakshatras);
        Assert.Equal(first.Yogas,second.Yogas);Assert.Equal(first.Karanas,second.Karanas);Assert.Equal(first.Periods,second.Periods);
        Assert.Equal(first.TimeZone.Id,second.TimeZone.Id);Assert.NotSame(first,second);
        await cache.CalculateAsync(request with { Date=request.Date.PlusDays(1) },CancellationToken.None);
        Assert.Equal(baselineCalls + 2,calculator.Calls);
    }
}
