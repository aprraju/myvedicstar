using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// Guards the general class behind #97: an abstraction that a host depends on, registered only by its concrete type.
/// </summary>
/// <remarks>
/// <para>
/// The defect this prevents: <c>LahiriAyanamsa</c> was registered concrete-only, so <c>IAyanamsa</c> was unresolvable in
/// every host. <c>GetService&lt;T&gt;()</c> returns <c>null</c> for an unregistered service instead of throwing, so a
/// missing registration looks exactly like an optional dependency and the build stays green.
/// </para>
/// <para>
/// Two complementary guards. The first is a fact about the host: each port a calculation module takes as a constructor
/// dependency resolves from the host's own provider. The second is a fact about the astronomy composition: every public
/// interface the astronomy package exposes must resolve as that interface, so the next concrete-only registration fails
/// here rather than at the first request that needs it.
/// </para>
/// <para>
/// If either fails, register the abstraction in the composition root. Do not add an optional dependency, lazy
/// resolution, or a default argument that hides the absence - all three are how this went unnoticed the first time.
/// </para>
/// </remarks>
public sealed partial class SharedHostTests
{
    /// <summary>
    /// Ports a calculation module takes by constructor injection. Listed by name so the set documents intent without a
    /// compile-time dependency on every consuming project; the type is still resolved for real before it is asserted.
    /// </summary>
    public static TheoryData<string> AstronomyPortsTheHostMustCompose() => new(ComposedPortNames());

    /// <summary>The single source of truth for "a port the host must compose", shared by both assertions.</summary>
    private static string[] ComposedPortNames() =>
    [
        "MyVedicDay.Astronomy.IEphemeris",
        "MyVedicDay.Astronomy.IAyanamsa",
        "MyVedicDay.Astronomy.IPlanetEphemeris",
        "MyVedicDay.Astronomy.ISunEventCalculator",
        "MyVedicDay.Astronomy.IMoonEventCalculator",
        "MyVedicDay.Astronomy.ILunisolarCalendar",
    ];

    [Theory]
    [MemberData(nameof(AstronomyPortsTheHostMustCompose))]
    public void Every_astronomy_port_a_module_depends_on_is_composed_by_the_host(string portName)
    {
        using var factory = new CombinedFactory();

        var port = typeof(IEphemeris).Assembly.GetType(portName)
            ?? throw new Xunit.Sdk.XunitException(
                $"'{portName}' is listed as a host-composed port but the astronomy package no longer declares it. "
                + "If it moved namespace, update the list; if it was removed, delete the entry.");

        // GetService returns null for an unregistered service and says nothing about which registrations exist, which is
        // why an absent port is silent. Asserting the instance as well as its presence keeps the failure honest: it
        // names the port rather than reporting an unrelated null.
        Assert.NotNull(factory.Services.GetService(port));
    }

    [Fact]
    public void No_astronomy_abstraction_the_host_depends_on_is_registered_only_by_its_concrete_type()
    {
        using var factory = new CombinedFactory();

        // The #97 defect in one sentence: a consumer depends on an interface, the composition registers only the class,
        // and GetService&lt;T&gt;() reports null so nothing fails until a request needs it.
        //
        // Scope matters. IEphemerisEngine is deliberately NOT in this set: it is the inner provider port that only the
        // spice and compare selections compose, and the analytical selection the host runs today has no engine to put
        // behind it. Asserting it here would demand a registration the analytical path must not have - a second
        // calculation path, which is the defect this file exists to prevent.
        var composedPorts = ComposedPortNames()
            .Select(name => typeof(IEphemeris).Assembly.GetType(name)
                ?? throw new Xunit.Sdk.XunitException($"'{name}' is listed as a composed port but the astronomy package no longer declares it."))
            .ToArray();

        Assert.NotEmpty(composedPorts);
        Assert.Contains(composedPorts, type => type.Name == nameof(IAyanamsa));

        // For every other interface the package exports, the host must either compose it or leave it entirely to the
        // provider selection. "Registered the class, not the interface" is neither: it is the silent state.
        var others = typeof(IEphemeris).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && !type.IsGenericTypeDefinition)
            .Where(type => !composedPorts.Contains(type))
            .Where(type => type.Name != nameof(IEphemerisEngine))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        var concreteOnly = others.Where(type => factory.Services.GetService(type) is null).ToArray();

        Assert.True(concreteOnly.Length == 0,
            "These astronomy interfaces resolve to null in the Star host: " + string.Join(", ", concreteOnly.Select(type => type.FullName))
            + ". Either compose the abstraction next to its implementation in AddAstronomy, or - if it is genuinely "
            + "provider-selection-only - record that decision here beside IEphemerisEngine with the reason.");
    }

    [Fact]
    public void The_composed_astronomy_port_matches_the_provider_the_host_selected()
    {
        using var factory = new CombinedFactory();

        // Composition is only correct if it is the composition the host configured: one selection, one port. A second
        // registration behind the same interface would leave the resolved instance unrelated to the key that provenance
        // and the parity gate report.
        var selection = factory.Services.GetRequiredService<SelectedAstronomyProvider>();
        Assert.Equal("analytical", selection.Key);
        Assert.NotNull(factory.Services.GetService<IEphemeris>());
    }
}

