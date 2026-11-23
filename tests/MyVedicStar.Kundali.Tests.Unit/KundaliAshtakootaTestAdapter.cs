using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// The production rules behind the business port, for tests that need real numbers (api#55).
/// </summary>
/// <remarks>
/// A translation-only copy of <c>KundaliAshtakootaRulesAdapter</c>, which lives in the Integration project this test
/// project does not reference. The copy carries no scoring logic - the arithmetic stays in
/// <see cref="Ashtakoota.Assess"/>, so a mistake here cannot make a wrong table look right, and a table change still
/// fails these tests rather than silently agreeing with a duplicate.
/// </remarks>
internal sealed class KundaliAshtakootaTestAdapter : IKundaliAshtakootaRules
{
    public string RulesVersion => Ashtakoota.RulesVersion;

    public AshtakootaAssessment Assess(AshtakootaSide sideA, AshtakootaSide sideB)
    {
        ArgumentNullException.ThrowIfNull(sideA);
        ArgumentNullException.ThrowIfNull(sideB);

        var result = Ashtakoota.Assess(
            sideA.Available ? Ashtakoota.Side.Of(sideA.SignIndex, sideA.NakshatraIndex)
                : Ashtakoota.Side.Unavailable(sideA.UnavailableReason ?? AshtakootaReasons.MoonPositionUnavailable),
            sideB.Available ? Ashtakoota.Side.Of(sideB.SignIndex, sideB.NakshatraIndex)
                : Ashtakoota.Side.Unavailable(sideB.UnavailableReason ?? AshtakootaReasons.MoonPositionUnavailable));

        return new AshtakootaAssessment(result.RulesVersion,
            result.Categories.Select(category => new AshtakootaCategoryResult(category.Code, category.Maximum,
                category.Score, category.Reason, category.Side, category.Note)).ToArray(),
            result.ScoredTotal, result.Certainty);
    }
}
