using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MyVedicDay.Api.Infrastructure.OpenApi;

/// <summary>
/// Adds security requirements to every operation that is not explicitly anonymous.
/// <para>
/// A single <c>BearerAuth</c> scheme is used in the OpenAPI document so Swagger UI shows one
/// "Authorize" dialog and reliably injects <c>Authorization: Bearer …</c> on every secured request.
/// The real multi-provider JWT validation (Google, Entra, Apple, Facebook) is enforced at runtime
/// by the authentication middleware — the OpenAPI scheme name is only a Swagger UI hint.
/// </para>
/// <para>
/// All MVC controllers have class-level <c>[Authorize(AuthPolicyNames.AnyProvider)]</c>. Detecting
/// <c>[Authorize]</c> is unreliable because ASP.NET Core stores <c>AuthorizeFilter</c> in
/// <c>FilterDescriptors</c>, which does not implement <c>IAuthorizeData</c>. Detecting
/// <c>[AllowAnonymous]</c> is reliable because <c>AllowAnonymousAttribute</c> implements
/// <c>IAllowAnonymous</c> directly.
/// </para>
/// </summary>
internal sealed class SecurityOperationTransformer(IConfiguration configuration) : IOpenApiOperationTransformer
{
    internal const string BearerSchemeId = "BearerAuth";

    internal static IEnumerable<string> EnabledProviders(IConfiguration configuration) =>
        new[] { "EntraExternalId", "Google", "Apple", "Facebook" }
            .Where(provider => configuration.GetValue<bool>($"Authentication:{provider}:Enabled"));

    private static readonly OpenApiResponse UnauthorizedResponse = new()
    {
        Description = "Unauthorized. A valid Bearer token from any supported identity provider is required.",
    };

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var descriptor = context.Description.ActionDescriptor;

        // [AllowAnonymous] reliably surfaces as IAllowAnonymous in both FilterDescriptors and EndpointMetadata.
        var isAnonymous = descriptor.FilterDescriptors.Any(fd => fd.Filter is IAllowAnonymous)
                       || descriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();

        if (!isAnonymous)
        {
            // OpenApiSecuritySchemeReference requires the document to resolve the $ref correctly at
            // serialization time — passing context.Document here is what makes "BearerAuth" appear as
            // the JSON key instead of an empty object {}.
            var requirement = new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = [],
            };

            operation.Security ??= [];
            operation.Security.Add(requirement);
            // Separate requirements are alternatives: one provider token is sufficient.
            foreach (var provider in EnabledProviders(configuration))
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(provider, context.Document)] = [],
                });
            }
            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("401", UnauthorizedResponse);
        }

        return Task.CompletedTask;
    }
}
