using MyVedicDay.Panchang.Business.Localization;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Business.Traditions;

namespace MyVedicDay.Panchang.Business;

internal sealed class GlossaryFacade(PanchangLocalizer localizer, PanchangTraditionResolver traditions)
    : IGlossaryFacade, IPanchangTraditionFacade
{
    public async Task<GlossaryResult> GetGlossaryAsync(
        IReadOnlyList<string>? preferredLanguages, string? tradition, CancellationToken cancellationToken)
    {
        var profile = await traditions.ResolveAsync(tradition, cancellationToken);
        var content = await localizer.GetContentAsync(preferredLanguages, profile, cancellationToken);
        return localizer.BuildGlossary(content, profile);
    }

    public async Task<PanchangTraditionsResult> GetTraditionsAsync(IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken)
    {
        var content = await localizer.GetContentAsync(preferredLanguages, cancellationToken);
        var profiles = await traditions.GetAllAsync(cancellationToken);

        return new PanchangTraditionsResult(content.Language, profiles.Select(profile => localizer.DescribeTradition(profile, content)).ToArray());
    }
}
