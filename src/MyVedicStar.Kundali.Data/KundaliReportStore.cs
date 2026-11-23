using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Data;

/// <summary>
/// KUN-17 (api#57) report persistence. Every query filters on the owning account, so another account's row is not
/// refused after being read - it is never read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not-owned and not-found are the same query.</b> This matters more than it looks: a "find by id, then compare the
/// owner" implementation does strictly more work for someone else's row than for a nonexistent one, and the endpoint
/// built on it becomes an existence oracle under timing. Filtering on <c>UserId</c> in the WHERE clause makes the two
/// cases identical down to the query plan, which is why the requirement can be asserted rather than merely promised.
/// </para>
/// <para>
/// <b>A re-request for the same revision returns the existing row.</b> The unique index on
/// (UserId, ProfileId, SourceRevision) is the arbiter, and a unique violation is answered by reading back rather than
/// by retrying - two concurrent clicks converge on one report instead of racing to create two.
/// </para>
/// </remarks>
internal sealed class KundaliReportStore(KundaliDbContext context, TimeProvider clock) : IKundaliReportStore
{
    private const string UniqueViolation = "23505";

    public async Task<KundaliReport> EnqueueAsync(Guid accountId, Guid profileId, KundaliReportRequest request,
        long sourceRevision, TimeProvider unusedClock, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same revision asked for twice is the same report. Returning the existing row is not an optimisation: a
        // second render would produce a second document for one revision, and the caller cannot tell them apart.
        var existing = await context.Reports.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId
                && x.SourceRevision == sourceRevision, token);
        if (existing is not null) return ToContract(existing);

        var now = clock.GetUtcNow();
        var entity = new KundaliReportEntity
        {
            Id = Guid.NewGuid(),
            UserId = accountId,
            ProfileId = profileId,
            Variant = request.Variant,
            Sections = string.Join(',', request.Sections),
            Status = KundaliReportStatus.Queued,
            SourceRevision = sourceRevision,
            RequestedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.Reports.Add(entity);
        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Lost the race to a concurrent request for the same revision. Detach the rejected row and return the
            // winner rather than surfacing a conflict the caller cannot act on.
            context.Entry(entity).State = EntityState.Detached;
            var winner = await context.Reports.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId
                    && x.SourceRevision == sourceRevision, token)
                ?? throw new InvalidOperationException(
                    "A unique violation was raised but the existing report could not be read back.", exception);
            return ToContract(winner);
        }

        return ToContract(entity);
    }

    public async Task<KundaliReport?> FindAsync(Guid accountId, Guid profileId, Guid reportId,
        CancellationToken token)
    {
        // The owner is part of the predicate, not a check after the fact: a foreign report id and a nonexistent one
        // produce the same query, the same scan and the same answer.
        var entity = await context.Reports.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == reportId && x.UserId == accountId && x.ProfileId == profileId, token);

        return entity is null ? null : ToContract(entity);
    }

    public async Task<IReadOnlyList<KundaliReport>> ListAsync(Guid accountId, Guid profileId,
        CancellationToken token) =>
        (await context.Reports.AsNoTracking()
            .Where(x => x.UserId == accountId && x.ProfileId == profileId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(token))
        .Select(ToContract)
        .ToArray();

    public async Task<KundaliReport> SettleAsync(Guid accountId, Guid profileId, Guid reportId, string status,
        string? downloadUrl, DateTimeOffset? downloadExpiresAtUtc, IReadOnlyList<KundaliReportFailure> failures,
        TimeProvider unusedClock, CancellationToken token)
    {
        var entity = await context.Reports
            .FirstOrDefaultAsync(x => x.Id == reportId && x.UserId == accountId && x.ProfileId == profileId, token)
            ?? throw new InvalidOperationException("Report disappeared between enqueue and settle.");

        var now = clock.GetUtcNow();
        entity.Status = status;
        entity.DocumentReference = downloadUrl;
        entity.FailuresJson = failures.Count == 0 ? null : JsonSerializer.Serialize(failures);
        entity.CompletedAtUtc = status is KundaliReportStatus.Ready or KundaliReportStatus.Failed ? now : null;
        entity.UpdatedAtUtc = now;

        await context.SaveChangesAsync(token);
        return ToContract(entity);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: UniqueViolation };

    private static KundaliReport ToContract(KundaliReportEntity entity) => new(
        entity.Id,
        entity.ProfileId,
        entity.Variant,
        entity.Sections.Length == 0
            ? []
            : entity.Sections.Split(',', StringSplitOptions.RemoveEmptyEntries),
        entity.Status,
        entity.SourceRevision,
        entity.RequestedAtUtc,
        entity.CompletedAtUtc,
        entity.DocumentReference,
        null,
        entity.FailuresJson is null
            ? []
            : JsonSerializer.Deserialize<KundaliReportFailure[]>(entity.FailuresJson) ?? []);
}
