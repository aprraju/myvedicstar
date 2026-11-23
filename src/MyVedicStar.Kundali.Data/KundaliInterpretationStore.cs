using Microsoft.EntityFrameworkCore;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Data;

/// <summary>
/// Persistence for the KUN-14 interpretations section (api#54): one row per domain, per profile, per source
/// revision and locale.
/// </summary>
/// <remarks>
/// <para><b>Scoped by principal, and scoped in the query.</b> Every read filters on <c>UserId</c> as well as
/// <c>ProfileId</c>, so a profile id belonging to somebody else returns exactly what a profile id that does not exist
/// returns. The alternative - read the row, then compare the owner - leaves a code path where a later refactor can
/// return the row before the comparison, and it makes the two cases distinguishable by timing as well as by code.
/// </para>
///
/// <para><b>Withheld survives.</b> The status and reason are columns, written from the contract verbatim and read
/// back verbatim. A stored <c>withheld</c> row cannot be reconstructed as <c>ready</c> or as absent, because turning it
/// into either would require choosing a value the row does not contain. That is the property persistence is most
/// likely to lose, so it is the one the schema constraints and the round-trip test both defend.</para>
///
/// <para><b>Writes are idempotent per identity.</b> The identity is (profile, source revision, locale, domain). A
/// second write for the same identity replaces the row in place: a re-run of the same generation is a correction of
/// the same claim, not a second claim. A different source revision is a different identity, so history is kept.
/// </para>
/// </remarks>
internal sealed class KundaliInterpretationStore(KundaliDbContext context)
{
    private readonly KundaliDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Replaces the stored interpretations for one (profile, revision, locale) with <paramref name="section"/>.
    /// </summary>
    /// <remarks>
    /// The whole set is replaced rather than merged. Merging would let a domain that is <c>not-evaluated</c> in this
    /// run keep a <c>ready</c> row from an earlier run, which is a stored response that never existed.
    /// </remarks>
    public async Task SaveAsync(Guid accountId, Guid profileId, Guid revisionId, long sourceRevision, string locale,
        KundaliInterpretationsSection section, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (sourceRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision), sourceRevision,
                "A stored interpretation must name a positive source revision; zero would make it unattributable.");

        var existing = await _context.Interpretations
            .Where(x => x.UserId == accountId && x.ProfileId == profileId
                && x.SourceRevision == sourceRevision && x.Locale == locale)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _context.Interpretations.RemoveRange(existing);

        foreach (var entry in section.Sections)
        {
            _context.Interpretations.Add(new KundaliInterpretationEntity
            {
                Id = Guid.NewGuid(),
                UserId = accountId,
                ProfileId = profileId,
                RevisionId = revisionId,
                SourceRevision = sourceRevision,
                Domain = entry.Domain,
                Locale = locale,
                // Verbatim. Not mapped to an enum, not defaulted: see the entity remarks.
                Status = entry.Status,
                Reason = entry.Reason,
                Text = entry.Text,
                FactorsJson = KundaliInterpretationJson.FactorsToJson(entry.Factors),
                UnknownFactorReferencesJson = KundaliInterpretationJson.StringsToJson(entry.UnknownFactorReferences),
                CreatedAtUtc = now,
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the stored interpretations for one (profile, revision, locale), scoped to the caller.
    /// </summary>
    /// <returns>
    /// The reconstructed section, or null when nothing is stored - which is also what a profile belonging to another
    /// principal returns. Callers must treat null as "not found" and must not distinguish the two cases.
    /// </returns>
    public async Task<KundaliInterpretationsSection?> ReadAsync(Guid accountId, Guid profileId, long sourceRevision,
        string locale, string interpretationLabel, string promptVersion, string modelProvenance,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Interpretations
            .AsNoTracking()
            .Where(x => x.UserId == accountId && x.ProfileId == profileId
                && x.SourceRevision == sourceRevision && x.Locale == locale)
            .OrderBy(x => x.Domain)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return null;

        var entries = rows.Select(row => new KundaliInterpretationEntry(
            row.Domain,
            row.Status,
            row.Text,
            KundaliInterpretationJson.FactorsFromJson(row.FactorsJson),
            row.Reason,
            KundaliInterpretationJson.StringsFromJson(row.UnknownFactorReferencesJson))).ToArray();

        return new KundaliInterpretationsSection(sourceRevision, promptVersion, modelProvenance, locale,
            interpretationLabel, entries, []);
    }
}
