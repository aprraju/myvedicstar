using Microsoft.Net.Http.Headers;

namespace MyVedicDay.Api.Infrastructure.Localization;

/// <summary>
/// HTTP content negotiation for the response language: an explicit <c>language</c> query value first, then
/// <c>Accept-Language</c> entries by descending quality. Choosing among them is the Business layer's job.
/// </summary>
internal static class LanguagePreferences
{
    public const string LanguageTagPattern = "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,3}$";
    private const int MaxAcceptLanguageEntries = 10;

    public static IReadOnlyList<string> FromRequest(HttpRequest request, string? explicitLanguage)
    {
        var languages = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            languages.Add(explicitLanguage.Trim());
        }

        languages.AddRange(request.GetTypedHeaders().AcceptLanguage
            .Where(value => value.Quality is null or > 0 && value.Value.Value is { Length: > 0 } tag && tag != "*")
            .OrderByDescending(value => value.Quality ?? 1)
            .Take(MaxAcceptLanguageEntries)
            .Select(value => value.Value.Value!));

        return languages;
    }

    /// <summary>Marks the response as language-dependent for clients and HTTP caches.</summary>
    public static void SetResponseLanguage(HttpResponse response, string language)
    {
        response.Headers.ContentLanguage = language;
        response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptLanguage);
    }
}
