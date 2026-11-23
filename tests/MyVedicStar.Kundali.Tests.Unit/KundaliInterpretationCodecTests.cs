using MyVedicStar.Kundali.Data;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// The payload round-trip itself (KUN-14, api#54), with no database.
/// </summary>
/// <remarks>
/// <para>These tests exist separately from the PostgreSQL ones on purpose. The database tests need a server and skip
/// without one; the properties below are pure serialisation and must hold on every machine. If only the database
/// tests covered them, a contributor without Postgres would see green having proven nothing - the same shape of gap
/// that made day-CI green mean nothing before #173.</para>
/// </remarks>
public sealed class KundaliInterpretationCodecTests
{
    private static KundaliInterpretationsSection Section() => new(
        4, "prompt-1", "stub-model", "en", "traditional interpretation",
        [
            new KundaliInterpretationEntry("career", KundaliInterpretationStatuses.Ready, "Text.", [], null, []),
            new KundaliInterpretationEntry("marriage", KundaliInterpretationStatuses.Withheld, null, [],
                KundaliInterpretationReasons.ContentRejected, []),
            new KundaliInterpretationEntry("wellbeing", KundaliInterpretationStatuses.NotEvaluated, null, [],
                KundaliInterpretationReasons.AiDisabled, []),
            new KundaliInterpretationEntry("finance", KundaliInterpretationStatuses.Ready, "Text.", [], null,
                ["graha:invented:id"]),
            new KundaliInterpretationEntry("education", KundaliInterpretationStatuses.Ready, "Text.", [], null, []),
        ],
        [new KundaliUnsupportedField("varshaphal", "engine-has-no-annual-chart")]);

    [Fact]
    public void Withheld_and_not_evaluated_stay_distinct_through_a_round_trip()
    {
        var json = KundaliInterpretationCodec.ToJson(Section());
        Assert.True(KundaliInterpretationCodec.TryRead(json, out var read, out var failure), failure);

        var withheld = read!.Sections.Single(s => s.Domain == "marriage");
        var notEvaluated = read.Sections.Single(s => s.Domain == "wellbeing");

        Assert.Equal(KundaliInterpretationStatuses.Withheld, withheld.Status);
        Assert.Equal(KundaliInterpretationReasons.ContentRejected, withheld.Reason);
        Assert.Equal(KundaliInterpretationStatuses.NotEvaluated, notEvaluated.Status);
        Assert.Equal(KundaliInterpretationReasons.AiDisabled, notEvaluated.Reason);
        Assert.NotEqual(withheld.Status, notEvaluated.Status);
    }

    [Fact]
    public void Every_domain_survives_exactly_once_and_in_order()
    {
        var original = Section();
        var json = KundaliInterpretationCodec.ToJson(original);
        Assert.True(KundaliInterpretationCodec.TryRead(json, out var read, out var failure), failure);

        Assert.Equal(original.Sections.Count, read!.Sections.Count);
        Assert.Equal(original.Sections.Select(s => s.Domain), read.Sections.Select(s => s.Domain));
        Assert.Distinct(read.Sections.Select(s => s.Domain));
    }

    [Fact]
    public void An_unknown_status_is_preserved_rather_than_promoted_to_ready()
    {
        // A stored value the contract does not know must not become a claim. Coercion here would be the same defect as
        // casting an enum, and it would be invisible to every client.
        var json = KundaliInterpretationCodec.ToJson(Section()).Replace(
            "\"withheld\"", "\"some-future-state\"", StringComparison.Ordinal);

        Assert.True(KundaliInterpretationCodec.TryRead(json, out var read, out var failure), failure);
        Assert.Equal("some-future-state", read!.Sections.Single(s => s.Domain == "marriage").Status);
    }

    [Fact]
    public void An_unparseable_payload_reports_a_reason_instead_of_throwing()
    {
        Assert.False(KundaliInterpretationCodec.TryRead("{not json", out var section, out var failure));
        Assert.Null(section);
        Assert.False(string.IsNullOrWhiteSpace(failure));

        Assert.False(KundaliInterpretationCodec.TryRead(null, out _, out var emptyFailure));
        Assert.False(string.IsNullOrWhiteSpace(emptyFailure));
    }
}
