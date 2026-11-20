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

        // Distinct is the whole point: a duplicate wire text is a duplicate merge key.
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

    [Fact]
    public void An_undefined_reason_has_no_wire_text_rather_than_borrowing_someone_elses()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Reasons.ToWire((RemedyReason)999));
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

