using Microsoft.EntityFrameworkCore;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Data;

/// <summary>
/// Owner-scoped alert consent. Like every repository here, each query filters accountId AND profileId, so a foreign
/// profile reads as empty rather than merely being refused.
/// </summary>
/// <remarks>
/// <para><b>Grant is an upsert in one transaction</b>, not a read-then-write: two identical concurrent PUTs must both
/// succeed and leave exactly one row per event type. The unique index is the arbiter; this code reacts to it instead of
/// trying to win a race with it.</para>
/// <para><b>Revocation deletes rows.</b> A revoked consent that stayed behind with an <c>enabled=false</c> flag would be
/// easy to resurrect by accident and hard to tell apart from consent that was never given, which is the wrong default
/// for an email-sending feature.</para>
/// </remarks>
internal sealed class KundaliAlertRepository(KundaliDbContext context, TimeProvider clock) : IKundaliAlertRepository
{
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<KundaliAlertPreference>> GetAsync(Guid accountId, Guid profileId,
        CancellationToken token) =>
        (await context.AlertPreferences.AsNoTracking()
            .Where(x => x.UserId == accountId && x.ProfileId == profileId)
            .OrderBy(x => x.EventType)
            .ToListAsync(token))
        .Select(ToContract)
        .ToArray();

    public async Task<IReadOnlyList<KundaliAlertPreference>> GetForDeliveryAsync(Guid accountId, Guid profileId,
        CancellationToken token) => await GetAsync(accountId, profileId, token);

    public async Task<IReadOnlyList<KundaliAlertPreference>> GrantAsync(Guid accountId, Guid profileId,
        IReadOnlyCollection<string> eventTypes, string channel, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);

        // Validate before touching the database: an unknown event type or channel must not create a row that the
        // delivery path will later have to filter out.
        if (eventTypes.Count == 0 || eventTypes.Any(type => !KundaliAlertEventTypes.All.Contains(type, StringComparer.Ordinal)))
            throw new ValidationException(KundaliErrors.InvalidAlertSubscription);
        if (!KundaliAlertEventTypes.Channels.Contains(channel, StringComparer.Ordinal))
            throw new ValidationException(KundaliErrors.InvalidAlertSubscription);

        var now = clock.GetUtcNow();
        var existing = await context.AlertPreferences
            .Where(x => x.UserId == accountId && x.ProfileId == profileId)
            .ToDictionaryAsync(x => x.EventType, StringComparer.Ordinal, token);

        foreach (var eventType in eventTypes.Distinct(StringComparer.Ordinal))
        {
            if (existing.TryGetValue(eventType, out var row))
            {
                // Consent already granted: keep the original instant, refresh only the audit stamp. A second PUT is
                // not a new consent event and must not look like one.
                row.Channel = channel;
                row.UpdatedAtUtc = now;
                continue;
            }

            context.AlertPreferences.Add(new KundaliAlertPreferenceEntity
            {
                Id = Guid.NewGuid(), UserId = accountId, ProfileId = profileId, EventType = eventType,
                Channel = channel, ConsentedAtUtc = now, UpdatedAtUtc = now
            });
        }

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent identical grant created the row first. That is success, not conflict: the desired end state
            // (consent exists) is already true, so re-read and return it rather than reporting an error.
            context.ChangeTracker.Clear();
        }

        return await GetAsync(accountId, profileId, token);
    }

    public async Task<int> RevokeAllAsync(Guid accountId, Guid profileId, CancellationToken token) =>
        await context.AlertPreferences
            .Where(x => x.UserId == accountId && x.ProfileId == profileId)
            .ExecuteDeleteAsync(token);

    private static KundaliAlertPreference ToContract(KundaliAlertPreferenceEntity entity) =>
        new(entity.EventType, entity.Channel, entity.ConsentedAtUtc, entity.UpdatedAtUtc);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: UniqueViolation };
}
