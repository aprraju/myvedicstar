using Microsoft.Extensions.Options;
using MyVedicDay.Panchang.Business.Caching;
using MyVedicDay.Panchang.Business.Configuration;
using NodaTime;
using NodaTime.Testing;

namespace MyVedicDay.Panchang.Tests.Unit.Caching;

public sealed class PanchangCacheExpirationPolicyTests
{
    private static readonly DateTimeZone Kolkata = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private static readonly DateTimeZone Chicago = DateTimeZoneProviders.Tzdb["America/Chicago"];

    [Fact]
    public void Today_InKolkata_ExpiresAtNextLocalMidnight()
    {
        var policy = CreatePolicy(Instant.FromUtc(2026, 9, 28, 10, 0));

        var expiresAt = policy.GetAbsoluteExpiration(new LocalDate(2026, 9, 28), Kolkata);

        // 2026-09-29 00:00 IST (+05:30)
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 18, 30, 0, TimeSpan.Zero), expiresAt);
    }

    [Fact]
    public void Today_InChicago_ExpiresAtNextLocalMidnightInDaylightTime()
    {
        var policy = CreatePolicy(Instant.FromUtc(2026, 9, 28, 15, 0));

        var expiresAt = policy.GetAbsoluteExpiration(new LocalDate(2026, 9, 28), Chicago);

        // 2026-09-29 00:00 CDT (−05:00)
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 5, 0, 0, TimeSpan.Zero), expiresAt);
    }

    [Fact]
    public void DaylightSavingStartDay_UsesTheNewOffsetForMidnight()
    {
        // DST starts in Chicago at 02:00 on 2026-03-08; the following midnight is CDT, not CST.
        var policy = CreatePolicy(Instant.FromUtc(2026, 3, 8, 12, 0));

        var expiresAt = policy.GetAbsoluteExpiration(new LocalDate(2026, 3, 8), Chicago);

        Assert.Equal(new DateTimeOffset(2026, 3, 9, 5, 0, 0, TimeSpan.Zero), expiresAt);
    }

    [Fact]
    public void SameInstant_DifferentLocations_ExpireAtDifferentUtcTimes()
    {
        var policy = CreatePolicy(Instant.FromUtc(2026, 9, 28, 15, 0));

        var kolkata = policy.GetAbsoluteExpiration(new LocalDate(2026, 9, 28), Kolkata);
        var chicago = policy.GetAbsoluteExpiration(new LocalDate(2026, 9, 28), Chicago);

        Assert.NotEqual(kolkata, chicago);
    }

    [Fact]
    public void PastDate_UsesShortLifetime()
    {
        var now = Instant.FromUtc(2026, 9, 28, 10, 0);
        var policy = CreatePolicy(now);

        var expiresAt = policy.GetAbsoluteExpiration(new LocalDate(2026, 9, 1), Kolkata);

        Assert.Equal((now + Duration.FromHours(1)).ToDateTimeOffset(), expiresAt);
    }

    [Fact]
    public void FarFutureDate_IsCappedByMaxLifetime()
    {
        var now = Instant.FromUtc(2026, 9, 28, 10, 0);
        var policy = CreatePolicy(now);

        var expiresAt = policy.GetAbsoluteExpiration(new LocalDate(2027, 9, 28), Kolkata);

        Assert.Equal((now + Duration.FromDays(35)).ToDateTimeOffset(), expiresAt);
    }

    private static PanchangCacheExpirationPolicy CreatePolicy(Instant now) =>
        new(new FakeClock(now), Options.Create(new PanchangOptions()));
}
