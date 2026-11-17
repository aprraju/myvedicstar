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
}
