using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Adapts the pure Ashtakoota rules to the business port (KUN-15). Translation only - no scoring logic lives here, so
/// the tables cannot be re-derived on the integration side and disagree with the calculation project's.
/// </summary>
internal sealed class KundaliAshtakootaRulesAdapter : IKundaliAshtakootaRules
{
    public string RulesVersion => Ashtakoota.RulesVersion;

    public AshtakootaAssessment Assess(AshtakootaSide sideA, AshtakootaSide sideB)
    {
        ArgumentNullException.ThrowIfNull(sideA);
        ArgumentNullException.ThrowIfNull(sideB);

        var result = Ashtakoota.Assess(ToCalculation(sideA), ToCalculation(sideB));

        var categories = result.Categories
            .Select(category => new AshtakootaCategoryResult(category.Code, category.Maximum, category.Score,
                category.Reason, category.Side, category.Note))
            .ToArray();

        return new AshtakootaAssessment(result.RulesVersion, categories, result.ScoredTotal, result.Certainty);
    }

    private static Ashtakoota.Side ToCalculation(AshtakootaSide side) => side.Available
        ? Ashtakoota.Side.Of(side.SignIndex, side.NakshatraIndex)
        : Ashtakoota.Side.Unavailable(side.UnavailableReason ?? AshtakootaReasons.MoonPositionUnavailable);
}

public static class KundaliAshtakootaRulesRegistration
{
    public static IServiceCollection AddKundaliAshtakootaRules(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IKundaliAshtakootaRules, KundaliAshtakootaRulesAdapter>());
        return services;
    }
}
