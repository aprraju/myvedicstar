namespace MyVedicStar.Authorize.Api.Security;

/// <summary>Where HTTPS is enforced.</summary>
public sealed class TransportSecurityOptions
{
    public const string SectionName = "TransportSecurity";

    /// <summary>
    /// Redirect plain-HTTP requests to HTTPS inside the application. Turn off where a proxy that terminates TLS already
    /// redirects (Azure Container Apps ingress with <c>allowInsecure: false</c>); the container itself only listens on HTTP.
    /// </summary>
    public bool RedirectHttpToHttps { get; set; } = true;
}
