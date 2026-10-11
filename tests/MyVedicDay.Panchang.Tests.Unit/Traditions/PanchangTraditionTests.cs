using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Panchang.Data;
using MyVedicDay.Panchang.Data.Content;
using MyVedicDay.Panchang.Tests.Unit.TestDoubles;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Unit.Traditions;

/// <summary>
/// Location drives the calculation; the tradition drives which timings are shown, their order and regional names.
/// </summary>
public sealed class PanchangTraditionTests
{
    private static readonly DateOnly Tuesday = new(2026, 9, 29);

    [Fact]
    public async Task DefaultTradition_IsAll_AndShowsEveryTimingIncludingChoghadiya()
    {
        var result = await GetDayAsync(tradition: null);

        Assert.Equal("all", result.Tradition.Key);
        Assert.Equal("drik", result.CalculationMethod);
        var kinds = Kinds(result);
        Assert.Contains("RahuKalam", kinds);
        Assert.Contains("Varjyam", kinds);
        Assert.Contains("BrahmaMuhurta", kinds);
        Assert.Equal(16, result.Days[0].Periods.Count(period => period.Group == "Choghadiya"));
    }

    [Fact]
    public async Task Telugu_ShowsItsTimingsWithRegionalNames_AndNoChoghadiya()
    {
        var result = await GetDayAsync("telugu");

        Assert.Equal(["Varjyam", "DurMuhurtam", "RahuKalam", "Yamagandam", "GulikaKalam", "AmritKalam", "AbhijitMuhurta", "BrahmaMuhurta"], result.Tradition.Display);
        Assert.All(Kinds(result), kind => Assert.Contains(kind, result.Tradition.Display));
        Assert.DoesNotContain(result.Days[0].Periods, period => period.Group == "Choghadiya");
        Assert.Equal("Durmuhurtham", result.Days[0].Periods.First(period => period.Kind == "DurMuhurtam").Name);
        Assert.Equal("Durmuhurtham", result.Glossary.Sections["periods"]["DurMuhurtam"].Name);
        Assert.Equal("Brahma Muhurtham", result.Days[0].Periods.Single(period => period.Kind == "BrahmaMuhurta").Name);
        Assert.DoesNotContain("ChoghadiyaAmrit", result.Glossary.Sections["periods"].Keys);
        Assert.DoesNotContain("choghadiya", result.Glossary.Sections["terms"].Keys);
    }

    [Fact]
    public async Task Tamil_ShowsRahuYamagandamKuligai_AndItsAuspiciousTimes()
    {
        var result = await GetDayAsync("tamil");

        Assert.Equal(["AbhijitMuhurta", "AmritKalam", "BrahmaMuhurta", "GulikaKalam", "RahuKalam", "Yamagandam"], Kinds(result).Order());
        Assert.Equal("Kuligai", result.Days[0].Periods.Single(period => period.Kind == "GulikaKalam").Name);
        Assert.Equal("Amirtha Kalam", result.Days[0].Periods.Single(period => period.Kind == "AmritKalam").Name);
    }

    /// <summary>
    /// Every regional Panchangam publishes auspicious times as well as the periods to avoid; a profile without them
    /// would leave the auspicious periods and the favorable, spiritual and mixed guidance windows always empty.
    /// </summary>
    [Theory]
    [InlineData("all")]
    [InlineData("telugu")]
    [InlineData("tamil")]
    [InlineData("kannada")]
    [InlineData("malayalam")]
    [InlineData("north-indian")]
    [InlineData("gujarati")]
    [InlineData("marathi")]
    public async Task EveryTradition_ShowsAuspiciousAndSpiritualTimes(string tradition)
    {
        var result = await GetDayAsync(tradition);

        var day = result.Days[0];
        Assert.Contains(day.Periods, period => period.Group == "Muhurta" && period.IsAuspicious);
        Assert.NotEmpty(day.Guidance!.FavorableWindows);
        Assert.Contains(day.Guidance.SpiritualWindows, window => window.Kind == "BrahmaMuhurta");
    }

    [Theory]
    [InlineData("north-indian")]
    [InlineData("gujarati")]
    public async Task NorthAndWest_ShowChoghadiyaRahuKaalAndAbhijitMuhurat(string tradition)
    {
        var result = await GetDayAsync(tradition, new DateOnly(2026, 10, 1)); // Thursday: Abhijit applies

        var day = result.Days[0];
        Assert.Equal(16, day.Periods.Count(period => period.Group == "Choghadiya"));
        Assert.Equal("Rahu Kaal", day.Periods.Single(period => period.Kind == "RahuKalam").Name);
        Assert.Equal("Abhijit Muhurat", day.Periods.Single(period => period.Kind == "AbhijitMuhurta").Name);
        Assert.DoesNotContain(day.Periods, period => period.Kind is "Varjyam" or "DurMuhurtam");
        Assert.Contains("choghadiya", result.Glossary.Sections["terms"].Keys);
        Assert.Contains("ChoghadiyaAmrit", result.Glossary.Sections["periods"].Keys);
    }

    [Fact]
    public async Task Gujarati_EmphasizesChoghadiyaFirst()
    {
        var result = await GetDayAsync("gujarati");

        Assert.Equal("Choghadiya", result.Tradition.Display[0]);
    }

    [Fact]
    public async Task TraditionsShareOneCalculation()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        foreach (var tradition in new[] { "all", "telugu", "tamil", "north-indian", "gujarati" })
        {
            await host.Facade.GetDateAsync(new PanchangQuery(17.385, 78.4867, tradition), Tuesday, CancellationToken.None);
        }

        Assert.Equal(1, host.Calculator.Calls);
    }

    [Fact]
    public async Task TraditionKeys_AreCaseInsensitive()
    {
        var result = await GetDayAsync("Telugu");

        Assert.Equal("telugu", result.Tradition.Key);
    }

    [Fact]
    public async Task TraditionsList_ContainsAllBuiltInTraditionsWithNames()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        var traditions = await host.GetRequiredService<ITraditionFacade>().GetTraditionsAsync(null, CancellationToken.None);

        Assert.Equal(
            ["all", "telugu", "tamil", "kannada", "malayalam", "north-indian", "gujarati", "marathi"],
            traditions.Traditions.Select(tradition => tradition.Key));
        Assert.All(traditions.Traditions, tradition => Assert.False(string.IsNullOrWhiteSpace(tradition.Description)));
        Assert.Equal("Telugu", traditions.Traditions.Single(tradition => tradition.Key == "telugu").Name);
    }

    [Fact]
    public async Task BuiltInProfiles_UseSupportedMethodsAndKnownTimings()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddPanchangCalculation()
            .AddPanchangData(new ConfigurationBuilder().Build())
            .AddTraditionsBusiness(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var methods = provider.GetRequiredService<IPanchangTraditionCatalog>();
        var profiles = await provider.GetRequiredService<ITraditionProfileRepository>().GetProfilesAsync(CancellationToken.None);
        var registeredTraditions = await provider.GetRequiredService<ITraditionRegistry>().GetKeysAsync(CancellationToken.None);

        // Every Panchang profile belongs to a tradition in the shared list.
        Assert.All(profiles, profile => Assert.Contains(profile.Key, registeredTraditions));

        var knownDisplayItems = Enum.GetNames<PanchangPeriodKind>().Where(kind => !kind.StartsWith("Choghadiya", StringComparison.Ordinal)).Append("Choghadiya");
        foreach (var profile in profiles)
        {
            Assert.True(methods.IsSupported(profile.CalculationMethod), profile.Key);
            Assert.NotEmpty(profile.Display);
            Assert.All(profile.Display, item => Assert.Contains(item, knownDisplayItems));
        }
    }

    [Fact]
    public async Task ProfilesAreConfigurable_ByOverrideFile()
    {
        var directory = Directory.CreateTempSubdirectory("mvd-traditions-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "panchang-traditions.json"), """
                { "traditions": [ { "key": "telugu", "calculationMethod": "drik", "display": [ "RahuKalam", "Choghadiya" ] } ] }
                """);
            using var host = new BusinessTestHost(
                Instant.FromUtc(2026, 9, 28, 6, 0), settings: new Dictionary<string, string?> { ["Content:Directory"] = directory });

            var result = await host.Facade.GetDateAsync(new PanchangQuery(17.385, 78.4867, "telugu"), Tuesday, CancellationToken.None);

            Assert.Equal(["RahuKalam", "Choghadiya"], result.Tradition.Display);
            Assert.DoesNotContain(result.Days[0].Periods, period => period.Kind == "Varjyam");
            Assert.Equal(16, result.Days[0].Periods.Count(period => period.Group == "Choghadiya"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<PanchangResult> GetDayAsync(string? tradition, DateOnly? date = null)
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));
        return await host.Facade.GetDateAsync(new PanchangQuery(17.385, 78.4867, tradition), date ?? Tuesday, CancellationToken.None);
    }

    private static string[] Kinds(PanchangResult result) =>
        result.Days[0].Periods.Where(period => period.Group == "Muhurta").Select(period => period.Kind).Distinct().ToArray();
}
