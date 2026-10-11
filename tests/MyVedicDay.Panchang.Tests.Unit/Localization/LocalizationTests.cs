using Microsoft.Extensions.Options;
using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business;
using MyVedicDay.Panchang.Business.Configuration;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Tests.Unit.TestDoubles;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Unit.Localization;

/// <summary>
/// Language negotiation, per-field fallback, content overrides and configurable classifications.
/// </summary>
public sealed class LocalizationTests : IDisposable
{
    private static readonly PanchangQuery Hyderabad = new(17.385, 78.4867);
    private readonly string _contentDirectory = Directory.CreateTempSubdirectory("mvd-content-").FullName;

    public LocalizationTests()
    {
        // A partial Telugu catalog: only some names, no meanings.
        File.WriteAllText(Path.Combine(_contentDirectory, "panchang-content.te.json"), """
            {
              "language": "te",
              "entries": {
                "periods.RahuKalam": { "name": "రాహు కాలం" },
                "weekday.Tuesday": { "name": "మంగళవారం" }
              }
            }
            """);

        // An English override: replaces one message, keeps the rest of the entry.
        File.WriteAllText(Path.Combine(_contentDirectory, "panchang-content.en.json"), """
            {
              "language": "en",
              "entries": { "periods.GulikaKalam": { "message": "Be careful starting new things" } }
            }
            """);
    }

    [Fact]
    public async Task DefaultRequest_IsEnglishWithGlossary()
    {
        using var host = CreateHost();

        var result = await host.Facade.GetTodayAsync(Hyderabad, CancellationToken.None);

        Assert.Equal("en", result.Glossary.Language);
        var rahu = result.Glossary.Sections["periods"]["RahuKalam"];
        Assert.Equal("Rahu Kalam", rahu.Name);
        Assert.Equal("Avoid important new beginnings", rahu.Message);
        Assert.StartsWith("A daily period associated with Rahu", rahu.Meaning, StringComparison.Ordinal);
        Assert.Equal("🔴", result.Glossary.Sections["classifications"]["Avoid"].Icon);
        Assert.Contains("tithi", result.Glossary.Sections["elements"].Keys);
    }

    [Fact]
    public async Task RegionalTag_ResolvesToBaseLanguage_WithPerFieldFallback()
    {
        using var host = CreateHost();

        var result = await host.Facade.GetDateAsync(
            Hyderabad with { PreferredLanguages = ["te-IN", "en"] }, new DateOnly(2026, 9, 29), CancellationToken.None);

        Assert.Equal("te", result.Glossary.Language);
        var rahu = result.Glossary.Sections["periods"]["RahuKalam"];
        Assert.Equal("రాహు కాలం", rahu.Name);
        Assert.StartsWith("A daily period associated with Rahu", rahu.Meaning, StringComparison.Ordinal); // fallback
        Assert.Equal("మంగళవారం", result.Days[0].WeekdayName);
        Assert.Equal("రాహు కాలం", result.Days[0].Periods.Single(period => period.Kind == "RahuKalam").Name);
        Assert.Equal("Yamagandam", result.Days[0].Periods.Single(period => period.Kind == "Yamagandam").Name); // fallback
    }

    [Fact]
    public async Task UnsupportedLanguage_FallsBackToDefault()
    {
        using var host = CreateHost();

        var glossary = await host.GetRequiredService<IGlossaryFacade>().GetGlossaryAsync(["fr-FR", "de"], null, CancellationToken.None);

        Assert.Equal("en", glossary.Language);
    }

    [Fact]
    public async Task ContentDirectory_OverridesIndividualFields()
    {
        using var host = CreateHost();

        var glossary = await host.GetRequiredService<IGlossaryFacade>().GetGlossaryAsync(null, null, CancellationToken.None);

        var gulika = glossary.Sections["periods"]["GulikaKalam"];
        Assert.Equal("Be careful starting new things", gulika.Message);
        Assert.Equal("Gulika Kalam", gulika.Name);
    }

    [Fact]
    public async Task OneCacheEntry_ServesEveryLanguage()
    {
        using var host = CreateHost();

        await host.Facade.GetTodayAsync(Hyderabad with { PreferredLanguages = ["en"] }, CancellationToken.None);
        await host.Facade.GetTodayAsync(Hyderabad with { PreferredLanguages = ["te"] }, CancellationToken.None);

        Assert.Equal(1, host.Calculator.Calls);
    }

    [Fact]
    public async Task PeriodClassifications_UseDefaultsFromTheSpecification()
    {
        using var host = CreateHost();

        var day = (await host.Facade.GetDateAsync(Hyderabad, new DateOnly(2026, 9, 29), CancellationToken.None)).Days[0];

        Assert.Equal(PeriodClassification.Avoid, Classification(day, "RahuKalam"));
        Assert.Equal(PeriodClassification.Caution, Classification(day, "GulikaKalam"));
        Assert.Equal(PeriodClassification.Favorable, Classification(day, "BrahmaMuhurta"));
        Assert.Equal(PeriodClassification.HighlyFavorable, Classification(day, "AmritKalam"));
    }

    [Fact]
    public async Task PeriodClassifications_AreConfigurable()
    {
        using var host = CreateHost(new Dictionary<string, string?> { ["Panchang:PeriodClassifications:GulikaKalam"] = "Avoid" });

        var day = (await host.Facade.GetDateAsync(Hyderabad, new DateOnly(2026, 9, 29), CancellationToken.None)).Days[0];

        Assert.Equal(PeriodClassification.Avoid, Classification(day, "GulikaKalam"));
        Assert.Equal(PeriodClassification.Avoid, Classification(day, "RahuKalam"));
    }

    [Fact]
    public void UnknownPeriodKindInConfiguration_FailsValidation()
    {
        using var host = CreateHost(new Dictionary<string, string?> { ["Panchang:PeriodClassifications:Choghadiya"] = "Favorable" });

        var exception = Assert.Throws<OptionsValidationException>(() => host.GetRequiredService<IOptions<PanchangOptions>>().Value);

        Assert.Contains("Choghadiya", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultLanguageNotSupported_FailsValidation()
    {
        using var host = CreateHost(new Dictionary<string, string?> { ["Localization:SupportedLanguages:0"] = "te" });

        Assert.Throws<OptionsValidationException>(() => host.GetRequiredService<IOptions<LocalizationOptions>>().Value);
    }

    public void Dispose() => Directory.Delete(_contentDirectory, recursive: true);

    private static PeriodClassification Classification(DailyPanchang day, string kind) =>
        day.Periods.First(period => period.Kind == kind).Classification;

    private BusinessTestHost CreateHost(Dictionary<string, string?>? extraSettings = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Content:Directory"] = _contentDirectory,
            ["Localization:DefaultLanguage"] = "en",
            ["Localization:SupportedLanguages:0"] = "en",
            ["Localization:SupportedLanguages:1"] = "te",
        };

        foreach (var (key, value) in extraSettings ?? [])
        {
            settings[key] = value;
        }

        if (extraSettings?.ContainsKey("Localization:SupportedLanguages:0") == true)
        {
            settings.Remove("Localization:SupportedLanguages:1");
        }

        return new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0), settings: settings);
    }
}
