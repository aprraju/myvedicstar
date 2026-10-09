using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace MyVedicStar.Authorize.Api.Authorization;

/// <summary>
/// Every controller action must be authorized. Fails startup if a controller or action opts out with
/// <c>[AllowAnonymous]</c>, which would otherwise override the policy applied to all controllers.
/// Anonymous endpoints (health probes, API documentation) are mapped outside MVC.
/// </summary>
internal sealed class DenyAnonymousControllersConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        var anonymous = application.Controllers
            .SelectMany(controller => controller.Actions
                .Where(action => IsAnonymous(controller.Attributes) || IsAnonymous(action.Attributes))
                .Select(action => $"{controller.ControllerType.Name}.{action.ActionMethod.Name}"))
            .ToList();

        if (anonymous.Count > 0)
        {
            throw new InvalidOperationException(
                $"Controllers must not allow anonymous access; remove [AllowAnonymous] from: {string.Join(", ", anonymous)}.");
        }
    }

    private static bool IsAnonymous(IReadOnlyList<object> attributes) => attributes.OfType<IAllowAnonymous>().Any();
}
