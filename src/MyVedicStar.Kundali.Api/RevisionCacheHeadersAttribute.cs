using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace MyVedicStar.API.Controllers;

/// <summary>
/// HTTP cache discipline for private Kundali payloads (KUN-21). The body is never stored by any shared cache:
/// <c>Cache-Control: private, no-store</c> is kept because these payloads carry birth details. What the client gains is a
/// validator: an <c>ETag</c> derived from the payload's freshness envelope, so a repeat read can be answered with a cheap
/// <c>304</c> while a revision change always produces a new tag.
/// </summary>
/// <remarks>
/// The tag is computed from the envelope only - profile, input revision, calculation settings and version tuple - so it
/// changes exactly when the answer can change. It is a hash, so it carries no personal input.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RevisionCacheHeadersAttribute : ActionFilterAttribute
{
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is not ObjectResult { Value: not null } result) return;

        var revisionKey = RevisionKeyOf(result.Value);
        if (revisionKey is null) return;

        var etag = new EntityTagHeaderValue('"' + Tag(revisionKey) + '"');
        var headers = context.HttpContext.Response.Headers;
        headers[HeaderNames.ETag] = etag.ToString();
        headers[HeaderNames.CacheControl] = "private, no-store";

        var supplied = context.HttpContext.Request.Headers.IfNoneMatch;
        if (supplied.Count > 0 && EntityTagHeaderValue.TryParseList(supplied, out var candidates)
            && candidates.Any(candidate => candidate.Compare(etag, useStrongComparison: true)))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        base.OnActionExecuted(context);
    }

    private static string? RevisionKeyOf(object value) => value switch
    {
        Contracts.KundaliOverviewResponse overview => overview.Freshness?.RevisionKey,
        Contracts.KundaliViewResponse view => view.RevisionKey,
        _ => null
    };

    private static string Tag(string revisionKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionKey)))[..32].ToLowerInvariant();
}
