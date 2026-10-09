using System.Net;

namespace MyVedicStar.Authorize.Api.Networking;

internal static class HttpContextClientIpExtensions
{
    /// <summary>
    /// The client's IP address. Forwarded headers have already been applied by the forwarded-headers middleware,
    /// which only honours them from configured trusted proxies.
    /// </summary>
    public static IPAddress? GetClientIpAddress(this HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress;
        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }
}
