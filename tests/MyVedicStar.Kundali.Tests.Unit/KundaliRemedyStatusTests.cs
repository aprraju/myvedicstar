using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#128. The remedy status boundary must never promote an unrecognised value into the rendered set.
/// </summary>
/// <remarks>
/// These exist because C# enums accept any integer: <c>(KundaliRemedyFindingStatus)999</c> compiles, so a cast at the
/// wire boundary is how an unrecognised state becomes a renderable one. Parsing is the only sanctioned route in, and
/// these tests pin that anything unreadable lands on <c>UnknownCode</c> rather than on a default member.
/// </remarks>
public sealed class KundaliRemedyStatusTests
{
    [Theory]
    [InlineData("evaluated", KundaliRemedyFindingStatus.Evaluated)]
    [InlineData("not-evaluated", KundaliRemedyFindingStatus.NotEvaluated)]
    [InlineData("withheld", KundaliRemedyFindingStatus.Withheld)]
    [InlineData("unknown-code", KundaliRemedyFindingStatus.UnknownCode)]
    [InlineData("EVALUATED", KundaliRemedyFindingStatus.Evaluated)]
    [InlineData("  withheld  ", KundaliRemedyFindingStatus.Withheld)]
    public void Recognised_text_round_trips(string text, KundaliRemedyFindingStatus expected) =>
        Assert.Equal(expected, KundaliRemedyStatuses.Parse(text));

    [Theory]
    [InlineData("bogus")]
    [InlineData("999")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("evaluated-ish")]
    public void Unrecognised_text_lands_on_unknown_code_and_never_on_evaluated(string? text)
    {
        // UnknownCode is a refusal: it says the catalogue entry cannot be trusted. Evaluated would be an assertion
        // that the finding was assessed, which is the one thing an unreadable status cannot support.
        var parsed = KundaliRemedyStatuses.Parse(text);

        Assert.Equal(KundaliRemedyFindingStatus.UnknownCode, parsed);
        Assert.NotEqual(KundaliRemedyFindingStatus.Evaluated, parsed);
    }

    [Fact]
    public void Every_status_has_its_own_stable_wire_text()
    {
        var statuses = Enum.GetValues<KundaliRemedyFindingStatus>();
        var texts = statuses.Select(KundaliRemedyStatuses.ToText).ToArray();

        Assert.Equal(statuses.Length, texts.Distinct().Count());
        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        // Round-trip through the parser, so text and parse cannot drift apart.
        Assert.All(statuses, status =>
            Assert.Equal(status, KundaliRemedyStatuses.Parse(KundaliRemedyStatuses.ToText(status))));
    }

    [Fact]
    public void An_undefined_enum_value_has_no_wire_text_rather_than_borrowing_one()
    {
        // A cast value must not acquire wire text by falling through; it is refused like any other unhandled status.
        Assert.Throws<ArgumentOutOfRangeException>(() => KundaliRemedyStatuses.ToText((KundaliRemedyFindingStatus)999));
    }
}

