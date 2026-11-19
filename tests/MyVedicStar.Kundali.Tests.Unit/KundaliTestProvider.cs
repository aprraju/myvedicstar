using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Builds the Kundali facade's service provider for tests, so no test enumerates its constructor by hand.
/// </summary>
/// <remarks>
/// <para>
/// api#124. <c>KundaliFacade</c> takes a long constructor (thirteen parameters at the time of writing). Tests that
/// wrote <c>new KundaliFacade(…)</c> broke on every parameter addition, and each broke individually, so the cost grew
/// with the number of test files and the failure gave no single place to fix. Four files did that. They now go through
/// here, and the container resolves the constructor, so an added parameter with no registration fails once - at
/// provider build - instead of once per call site.
/// </para>
/// <para>
/// <b><see cref="ValidateOnBuild"/> is baked in and not optional.</b> Without it a missing dependency surfaces as a null
/// or an exception at first use, which is the shape of failure this exists to prevent. A test that had to remember the
/// flag would eventually forget it, and the forgetting would be invisible. Making it unconditional removes that choice.
/// </para>
/// <para>
/// <b>This does not make the problem impossible.</b> A future test can still write <c>new KundaliFacade(…)</c> and
/// reintroduce the same fragility; nothing here prevents that. What it does is make the safe form the convenient one,
/// so hand-enumeration is a deliberate choice rather than the path of least resistance. Claiming more than that would
/// be a guarantee this mechanism does not provide.
/// </para>
/// <para>
/// <b>Per-test overrides are <see cref="With{TService}(TService)"/> calls</b>, applied before <see cref="Build"/>. That
/// is deliberately the plainest possible mechanism - no fluent substitution syntax, no reflection over the constructor,
/// no inference of which dependency a test "probably" wants. A test that overrides a port says so explicitly, which is
/// auditable; a builder that guessed would hide exactly the kind of omission this file exists to surface.
/// </para>
/// </remarks>
internal sealed class KundaliTestProvider
{
    private readonly ServiceCollection services = new();

    private KundaliTestProvider() =>
        // Every test needs a clock that does not move, and the business module defaults to the system clock. Registered
        // before AddKundaliBusiness so the module's TryAddSingleton keeps this one rather than replacing it. A test that
        // wants a movable clock overrides TimeProvider explicitly.
        services.AddSingleton<TimeProvider>(new FixedClock());

    /// <summary>Starts a builder with the business module pending and a fixed clock in place.</summary>
    internal static KundaliTestProvider Create() => new();

    /// <summary>Registers a substitute or concrete instance for a port the facade depends on.</summary>
    /// <remarks>Last registration wins, matching the container's own resolution rule, so a later override replaces an
    /// earlier one rather than being silently ignored.</remarks>
    internal KundaliTestProvider With<TService>(TService implementation) where TService : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        services.AddSingleton(implementation);
        return this;
    }

    /// <summary>
    /// Builds the provider with <see cref="ValidateOnBuild"/> enforced.
    /// </summary>
    /// <remarks>
    /// <see cref="ServiceProviderOptions.ValidateScopes"/> is deliberately not enabled. The facade is scoped, so scope
    /// validation would reject any resolution from the root provider - a real container rule, but not the failure this
    /// helper exists to surface, and enforcing it would make every test assert scope discipline rather than the
    /// behaviour under test.
    /// </remarks>
    /// <exception cref="AggregateException">A registered service cannot be constructed from its dependencies. The
    /// message names the missing type, so the fix is one registration rather than a search.</exception>
    internal ServiceProvider Build()
    {
        services.AddKundaliBusiness();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>Builds the provider and resolves the facade from a scope, as a real request would.</summary>
    internal IKundaliFacade BuildFacade() =>
        Build().CreateScope().ServiceProvider.GetRequiredService<IKundaliFacade>();
}

