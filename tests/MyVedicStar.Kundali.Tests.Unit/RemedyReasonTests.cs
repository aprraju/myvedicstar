using MyVedicStar.Kundali.Business;

namespace MyVedicStar.Kundali.Tests.Unit;
/// <summary>
/// api#146. The closed reason set: every cause has its own wire text, and an undefined value cannot borrow one.
/// </summary>
/// <remarks>
/// The merge in <see cref="RemedyEmission.MergeUnsupported"/> keys on (path, reason), so two causes sharing wire text
/// would make one entry vanish while both callers believed they had written theirs. These tests pin the property that
/// makes the key safe.
/// </remarks>
public sealed class RemedyReasonTests
{
    [Fact]
    public void Every_reason_has_a_distinct_wire_text()
    {
        var reasons = Enum.GetValues<RemedyReason>();
        var wires = reasons.Select(Reasons.ToWire).ToArray();

        // What THIS test pins: the DECLARED members have distinct wire text today.
        //
        // It cannot pin the collision itself - a test can only see the members that were declared, so a future member
        // that collides is invisible here until someone adds it to this run. What catches that is the compiler:
        // mapping a new member to an existing wire text fails with CS8510 "The pattern is unreachable". Credit the
        // build for the collision rule; this test covers the members that exist.
        Assert.Equal(reasons.Length, wires.Distinct(StringComparer.Ordinal).Count());
        Assert.All(wires, wire => Assert.False(string.IsNullOrWhiteSpace(wire)));
    }

    [Fact]
    public void Wire_text_is_lower_kebab_so_it_reads_as_a_code_not_prose()
    {
        // Clients switch on these; a space or an underscore would make them fragile in URLs, logs and switch arms.
        Assert.All(Enum.GetValues<RemedyReason>(), reason =>
        {
            var wire = Reasons.ToWire(reason);
            Assert.Equal(wire.ToLowerInvariant(), wire);
            Assert.DoesNotContain(' ', wire);
            Assert.DoesNotContain('_', wire);
        });
    }

    [Theory]
    [InlineData(999)]
    [InlineData(-1)]
    public void An_undefined_reason_has_no_wire_text_rather_than_borrowing_someone_elses(int raw)
    {
        // Negative as well as positive: a serializer or a parse that produces -1 must not fall through to whoever owns
        // that arm, and ReasonWire throwing from a property getter is a poor place to discover it - so the refusal is
        // pinned at the map, which is where the value would be turned into text.
        Assert.Throws<ArgumentOutOfRangeException>(() => Reasons.ToWire((RemedyReason)raw));
    }

    [Fact]
    public void A_serializer_meeting_the_boundary_type_emits_wire_text_not_the_member_name()
    {
        // The point of keeping RemedyReason internal: there is no public surface for a serializer to meet the enum
        // through, so the wire value cannot be decided by reflection. This asserts what a client actually receives.
        var resolution = RemedyResolver.Resolve("absent", new Dictionary<string, RemedyFindingState>());

        var json = System.Text.Json.JsonSerializer.Serialize(resolution);

        Assert.Contains("catalogue-stale", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CatalogueStale", json, StringComparison.Ordinal);
        Assert.Contains("reasonWire", json, StringComparison.OrdinalIgnoreCase);
        // And the enum itself contributes no property, because it is internal and has no public getter.
        Assert.DoesNotContain("\"reason\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_wire_texts_the_contract_promises_are_stable()
    {
        // Pinned by literal on purpose: these strings are the promise to clients. If one changes, that is a contract
        // change and this test is where it has to be acknowledged rather than discovered.
        Assert.Equal("catalogue-stale", Reasons.ToWire(RemedyReason.CatalogueStale));
        Assert.Equal("stored-status-unreadable", Reasons.ToWire(RemedyReason.StoredStatusUnreadable));
        Assert.Equal("input-missing", Reasons.ToWire(RemedyReason.InputMissing));
        Assert.Equal("capability-gap", Reasons.ToWire(RemedyReason.CapabilityGap));
    }
}

