using Microsoft.Extensions.Options;
using MyVedicDay.Caching;
using MyVedicDay.Panchang.Business.Configuration;
using NodaTime;

namespace MyVedicDay.Panchang.Business.Caching;

/// <summary>
/// Decides when a daily Panchang cache entry expires: at the location's next local midnight for today, with
/// Panchang's configured lifetimes for past and far-future dates (see <see cref="LocalDayCacheExpiration"/>).
/// </summary>
internal sealed class PanchangCacheExpirationPolicy(IClock clock, IOptions<PanchangOptions> options)
{
    public DateTimeOffset GetAbsoluteExpiration(LocalDate date, DateTimeZone timeZone) =>
        LocalDayCacheExpiration.Calculate(
            date, timeZone, clock.GetCurrentInstant(), options.Value.PastDateCacheLifetime, options.Value.MaxCacheLifetime);
}
