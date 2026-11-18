using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>Adapts the pure yoga catalogue to the business port. No logic of its own - every rule and every verdict
/// belongs in the calculation project, where it is testable without a host.</summary>
internal sealed class KundaliYogaRulesAdapter : IKundaliYogaRules
{
    public string RulesVersion => Yogas.RulesVersion;

    public IReadOnlyList<string> Statuses =>
        [Yogas.Statuses.Satisfied, Yogas.Statuses.NotSatisfied, Yogas.Statuses.Indeterminate];

    public IReadOnlyList<string> Catalogue => Yogas.CatalogueCodes;

    public KundaliYogaEvaluation Evaluate(IReadOnlyDictionary<string, int> signByGraha)
    {
        var result = Yogas.Evaluate(signByGraha);

        return new KundaliYogaEvaluation(
            result.RulesVersion,
            result.Assessments.Select(Map).ToArray(),
            result.Indeterminate.Select(Map).ToArray());
    }

    private static KundaliYogaResult Map(Yogas.Assessment assessment) => new(
        assessment.Code,
        assessment.Name,
        assessment.Scope,
        assessment.Status,
        assessment.Conditions
            .Select(condition => new KundaliYogaConditionResult(condition.Text, condition.Truth))
            .ToArray(),
        assessment.Supporting,
        assessment.Opposing,
        assessment.Exceptions,
        assessment.Cancellations,
        assessment.Complete,
        assessment.Reason,
        assessment.UntestedComponents);
}

public static class KundaliYogaRulesRegistration
{
    public static IServiceCollection AddKundaliYogaRules(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IKundaliYogaRules, KundaliYogaRulesAdapter>());
        return services;
    }
}
