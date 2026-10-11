using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Astronomy;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;

namespace MyVedicDay.Muhurta.Calculation;

/// <summary>
/// Evaluates a day for an activity. Pure and CPU-bound: no I/O, caching or persistence.
/// </summary>
public interface IMuhurtaCalculator
{
    /// <exception cref="SunEventNotFoundException">The sun does not rise or set on the date at the location.</exception>
    /// <exception cref="MyVedicDay.Shared.Exceptions.CalculationException">A numerical search failed to converge.</exception>
    MuhurtaEvaluation Evaluate(MuhurtaCalculationRequest request);

    /// <summary>Evaluates an already calculated day without changing the evaluation rules.</summary>
    MuhurtaEvaluation Evaluate(MuhurtaCalculationRequest request, PanchangDay day) => Evaluate(request);
}

internal sealed class MuhurtaCalculator(IPanchangCalculator panchang, ILunisolarCalendar calendar) : IMuhurtaCalculator
{
    public MuhurtaEvaluation Evaluate(MuhurtaCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Periods are computed for every kind; the activity profile decides which ones count.
        var day = panchang.Calculate(new PanchangCalculationRequest(request.Date, request.Location, request.TimeZone, PanchangTraditions.Drik));

        return Evaluate(request, day);
    }

    public MuhurtaEvaluation Evaluate(MuhurtaCalculationRequest request, PanchangDay day)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(day);
        var profile = request.Profile;
        var needsMonth = profile.ExcludeAdhikaMonth || profile.ExcludedLunarMonths.Count > 0;
        return MuhurtaDayEvaluator.Evaluate(day, profile, needsMonth ? calendar.GetLunarMonth(day.Sun.Sunrise) : null, request.BirthNakshatra);
    }
}

/// <summary>Version of the Muhurta evaluation algorithm; part of every Muhurta cache key.</summary>
public static class MuhurtaCalculationVersion
{
    public const string Current = "1.0.0";
}

/// <summary>Registers the Muhurta calculation engine.</summary>
public static class MuhurtaCalculationServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IMuhurtaCalculator"/> and the calculation contracts it builds on. Safe to call from several modules.</summary>
    public static IServiceCollection AddMuhurtaCalculation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAstronomy();
        services.AddPanchangCalculation();
        services.TryAddSingleton<IMuhurtaCalculator, MuhurtaCalculator>();

        return services;
    }
}
