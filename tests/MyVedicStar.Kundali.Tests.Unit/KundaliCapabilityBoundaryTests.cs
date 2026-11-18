using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// ADR-0016 section 3 is a contract, not prose: these tests fail if an unsupported capability is silently dropped or
/// reclassified as supported. Removing an entry requires an ADR change, so the failure here is the intended guard.
/// </summary>
public sealed class KundaliCapabilityBoundaryTests
{
    [Fact]
    public void D60_is_never_a_supported_divisional()
    {
        Assert.Contains(60, KundaliCapabilities.ProvisionalDivisionalCharts);
        Assert.DoesNotContain(60, KundaliCapabilities.SupportedDivisionalCharts);
        Assert.False(KundaliCapabilities.IsSupportedDivisional(60));
        Assert.True(KundaliCapabilities.IsProvisionalDivisional(60));
    }

    [Fact]
    public void D9_is_supported_and_D60_is_never_definitive()
    {
        Assert.Contains(9, KundaliCapabilities.SupportedDivisionalCharts);

        // D60 is the division the ADR refuses: it must never appear among the definitive set, and must be listed as
        // provisional so a caller can see why rather than inferring "unsupported" from an absence.
        Assert.DoesNotContain(60, KundaliCapabilities.SupportedDivisionalCharts);
        Assert.Contains(60, KundaliCapabilities.ProvisionalDivisionalCharts);

        // The supported and provisional sets must not overlap: a division is either definitive or provisional.
        Assert.Empty(KundaliCapabilities.SupportedDivisionalCharts
            .Intersect(KundaliCapabilities.ProvisionalDivisionalCharts));
    }

    [Fact]
    public void Shadbala_reports_chesta_as_unsupported_and_never_as_supported()
    {
        Assert.Contains("Chesta", KundaliCapabilities.UnsupportedShadbalaComponents);
        Assert.DoesNotContain("Chesta", KundaliCapabilities.SupportedShadbalaComponents);

        // The other five classical balas stay supported, so Shadbala is partial rather than absent.
        Assert.Equal(5, KundaliCapabilities.SupportedShadbalaComponents.Count);
        Assert.Contains("Sthana", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Dig", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Kala", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Naisargika", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Drik", KundaliCapabilities.SupportedShadbalaComponents);
    }

    [Fact]
    public void Lunar_node_convention_is_mean_not_true()
    {
        Assert.Equal("MeanNode", KundaliCapabilities.LunarNodeConvention);
    }

    [Theory]
    [InlineData(1799, 12, 31)]
    [InlineData(2201, 1, 1)]
    [InlineData(1500, 6, 1)]
    public void Birth_dates_outside_the_supported_range_are_refused(int year, int month, int day)
    {
        Assert.False(KundaliCapabilities.IsSupportedBirthDate(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(1800, 1, 1)]
    [InlineData(2000, 6, 15)]
    [InlineData(2200, 12, 31)]
    public void Birth_dates_inside_the_supported_range_are_accepted(int year, int month, int day)
    {
        Assert.True(KundaliCapabilities.IsSupportedBirthDate(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Published_report_names_the_unsupported_capabilities_so_nothing_degrades_silently()
    {
        var report = KundaliCapabilities.Report();

        Assert.Contains(60, report.ProvisionalDivisionalCharts);
        Assert.Contains("Chesta", report.UnsupportedShadbalaComponents);
        Assert.Equal("MeanNode", report.LunarNodeConvention);

        // At least one human-readable entry must name each mandated item: D60, Chesta, and pada boundaries.
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("D60", StringComparison.Ordinal));
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("Chesta", StringComparison.Ordinal));
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("pada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_report_section_set_is_published_and_matches_the_module_vocabulary()
    {
        // A client must bind to these ids rather than assert the server's structure, so the set is published AND
        // pinned be identical to the persistence vocabulary. Deriving it from KundaliSections.All means the two cannot
        // drift; this test is what fails if that derivation is ever replaced with a hand-written list.
        var report = KundaliCapabilities.Report();

        Assert.Equal(KundaliSections.All, report.ReportSections);
        Assert.Contains(KundaliSections.Yogas, report.ReportSections);
    }

    [Fact]
    public void The_report_section_set_is_closed_and_carries_no_duplicates()
    {
        // "Closed" is the property that lets a request naming an unknown section be rejected rather than silently
        // ignored. A duplicate id would make the published list ambiguous about how many sections exist.
        var report = KundaliCapabilities.Report();

        Assert.Equal(report.ReportSections.Count, report.ReportSections.Distinct(StringComparer.Ordinal).Count());
        Assert.All(report.ReportSections, section => Assert.False(string.IsNullOrWhiteSpace(section)));

        // Every published id must be one the module actually defines - the closed set is the module's, not a copy.
        Assert.All(report.ReportSections, section => Assert.Contains(section, KundaliSections.All));
    }
}
