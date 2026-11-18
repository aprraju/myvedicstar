using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// Regression guard for host composition.
/// </summary>
/// <remarks>
/// The defect this prevents: <c>IEphemeris</c> was registered only by the Day host's Panchang composition, so the
/// Star host had no provider at all. <c>Kundali.Integration</c> still compiled against astronomy, so every build and
/// every unit test passed while the real host would have thrown on the first request needing a position. CI cannot
/// see a missing registration unless a test resolves it from the real container - which is what this does.
/// <para>
/// If this fails, do not add an optional dependency or a fallback: the port must be composed by the host.
/// </para>
/// </remarks>
public sealed partial class SharedHostTests
{
    [Fact]
    public void The_star_host_composes_the_astronomy_port_the_kundali_engine_depends_on()
    {
        using var factory = new CombinedFactory();

        // Resolved from the host's own service provider, not a hand-built container.
        var ephemeris = factory.Services.GetService<IEphemeris>();

        Assert.True(ephemeris is not null,
            "The Star host must compose IEphemeris. Kundali's engine depends on this port; without it the first "
            + "request needing a planetary position fails at runtime while CI stays green. Register it in "
            + "AddMyVedicStarModule (the host's composition root), not inside a module registration.");
    }

    [Fact]
    public void The_astronomy_port_is_resolvable_with_the_ayanamsa_it_is_used_with()
    {
        using var factory = new CombinedFactory();

        // A provider alone is not enough: the engine converts tropical to sidereal, so the ayanamsa must also come
        // from the host rather than be assumed. Both are asserted because either being absent is the same class of
        // composition bug.
        Assert.NotNull(factory.Services.GetService<IEphemeris>());
        Assert.NotNull(factory.Services.GetService<IAyanamsa>());
    }
}

