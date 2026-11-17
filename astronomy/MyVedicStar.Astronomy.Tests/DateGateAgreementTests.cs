using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Jpl.Provider;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>
/// Issue #83: two date gates that could disagree. These tests pin the boundaries the review named and assert the
/// gates cannot drift apart again, because both now delegate to one decision.
/// </summary>
public sealed class DateGateAgreementTests
{
    private static readonly DateOnly BeforeFloor = new(1799, 12, 31);
    private static readonly DateOnly AtFloor = new(1800, 1, 1);
    private static readonly DateOnly AtCeiling = new(2200, 12, 31);
    private static readonly DateOnly AfterCeiling = new(2201, 1, 1);

    [Theory]
    [InlineData(1799, 12, 31, false)]
    [InlineData(1800, 1, 1, true)]
    [InlineData(2200, 12, 31, true)]
    [InlineData(2201, 1, 1, false)]
    public void The_engine_gate_accepts_exactly_the_advertised_span(int year, int month, int day, bool serviceable)
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        var date = new DateOnly(year, month, day);

        if (serviceable) engine.EnsureDateIsCovered(date);
        else Assert.Throws<AstronomyNotSupportedException>(() => engine.EnsureDateIsCovered(date));
    }

    [Theory]
    [InlineData(1799, 12, 31)]
    [InlineData(1800, 1, 1)]
    [InlineData(2200, 12, 31)]
    [InlineData(2201, 1, 1)]
    [InlineData(5, 1, 1)]
    public void The_two_gates_cannot_disagree(int year, int month, int day)
    {
        // The exact defect #83 reported: a kernel declared Min..Max answered "yes" for year 5 while the capability
        // floor said no. Serviceability must now equal the capability answer for every date a kernel covers.
        var date = new DateOnly(year, month, day);
        Assert.Equal(AstronomyCapabilities.IsSupportedDate(date), SpiceConfiguration.Default.IsServiceable(date));
    }

    [Fact]
    public void Year_five_is_refused_even_though_a_kernel_declares_it()
    {
        var date = new DateOnly(5, 1, 1);

        // The kernel declaration alone still says yes - that is honest about the kernel, not about serviceability.
        Assert.True(SpiceConfiguration.Default.CoversDateByKernel(date));

        // ...but the engine refuses it, so no caller can obtain a position for a date the product does not support.
        Assert.False(SpiceConfiguration.Default.IsServiceable(date));
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        var failure = Assert.Throws<AstronomyNotSupportedException>(() => engine.EnsureDateIsCovered(date));
        Assert.Contains("product", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Kernel_coverage_is_at_least_as_wide_as_the_promise()
    {
        // The declaration must not be narrower than the promise, or the promise would be unprovable.
        var kernels = SpiceConfiguration.Default.KernelDateRange();
        Assert.NotNull(kernels);
        Assert.True(kernels!.Value.From <= AstronomyCapabilities.MinimumSupportedDate);
        Assert.True(kernels.Value.To >= AstronomyCapabilities.MaximumSupportedDate);
    }

    [Fact]
    public void A_narrow_kernel_is_reported_as_a_kernel_gap_not_a_policy_refusal()
    {
        // With a kernel that starts in 1900, 1850 is inside the product promise but outside kernel coverage. The
        // message must name the kernel, not the policy, so an operator can tell which one to fix.
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default with
        {
            Kernels = [new SpiceKernel("/var/spice/de440.bsp", new DateOnly(1900, 1, 1), new DateOnly(2100, 1, 1))]
        });

        var failure = Assert.Throws<AstronomyNotSupportedException>(
            () => engine.EnsureDateIsCovered(new DateOnly(1850, 1, 1)));
        Assert.Contains("kernel", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside the product", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Supported_range_is_the_intersection_of_kernel_coverage_and_the_promise()
    {
        var range = SpiceConfiguration.Default.SupportedDateRange;
        Assert.NotNull(range);
        Assert.Equal(AstronomyCapabilities.MinimumSupportedDate, range!.Value.From);
        Assert.Equal(AstronomyCapabilities.MaximumSupportedDate, range.Value.To);
    }
}
