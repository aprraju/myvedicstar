using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Tests.Unit;

public sealed class KundaliValidatorTests
{
    private static KundaliValidator Validator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddKundaliBusiness();
        return services.BuildServiceProvider().GetRequiredService<KundaliValidator>();
    }

    [Fact]
    public void A_section_payload_larger_than_the_bound_is_refused()
    {
        var payload = new string('x', KundaliLimits.SectionPayloadBytes + 1);
        Assert.Throws<ValidationException>(() => Validator().ValidateSectionPayload(KundaliSections.Overview, payload));
    }

    [Fact]
    public void An_unknown_section_name_is_refused()
    {
        Assert.Throws<ValidationException>(() => Validator().ValidateSectionPayload("nonsense", "{}"));
    }

    [Theory]
    [InlineData(KundaliSections.Overview)]
    [InlineData(KundaliSections.Bala)]
    [InlineData(KundaliSections.Interpretations)]
    public void A_known_section_within_the_bound_is_accepted(string section)
    {
        Validator().ValidateSectionPayload(section, "{}");
    }

    [Fact]
    public void A_future_birth_date_is_refused_rather_than_producing_a_chart_of_nothing()
    {
        // FixedClock is 2026-11-17, so this date is in the future relative to the injected clock - which is the point:
        // the rule must read the clock it was given, not the system clock.
        var tomorrow = new DateOnly(2026, 11, 18);
        Assert.Throws<ValidationException>(() => Validator().ValidateSnapshot(Snapshot(tomorrow)));
    }

    [Fact]
    public void A_birth_date_today_is_accepted()
    {
        // Boundary: today is not in the future. A rule that refused "today" would break every same-day birth.
        Validator().ValidateSnapshot(Snapshot(new DateOnly(2026, 11, 17)));
    }

    [Fact]
    public void A_historically_supported_birth_date_is_accepted()
    {
        Validator().ValidateSnapshot(Snapshot(new DateOnly(1990, 4, 12)));
    }

    private static KundaliInputSnapshot Snapshot(DateOnly dateOfBirth) => new(
        dateOfBirth, MyVedicStar.VedicProfiles.Shared.BirthTimeMode.Known, new TimeOnly(6, 45), null, null,
        13.0827m, 80.2707m, "Asia/Kolkata", 1);

}
