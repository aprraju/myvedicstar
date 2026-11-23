using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Tests.Integration.Postgres;
using MyVedicStar.Kundali.Data;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Integration;

/// <summary>
/// Persistence tests for the KUN-14 interpretations section (api#54).
/// </summary>
/// <remarks>
/// <para>The three properties asserted here are the ones persistence can silently destroy. Each is stated as the
/// distinction it protects rather than as a status code, because a status code passes for the wrong reason.</para>
/// <para>Skipped unless <c>MYVEDICSTAR_TEST_POSTGRES</c> is set, matching this project's convention: a test that
/// silently passes without a database proves nothing about persistence.</para>
/// </remarks>
public sealed class KundaliInterpretationPersistenceTests(PostgresDatabaseFixture database)
    : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly string[] AllDomains =
        ["career", "education", "finance", "marriage", "wellbeing"];

    private async Task<KundaliDbContext> ContextAsync()
    {
        Skip.IfNot(database.IsAvailable, "Set MYVEDICSTAR_TEST_POSTGRES to enable disposable PostgreSQL tests.");
        var options = new DbContextOptionsBuilder<KundaliDbContext>()
            .UseNpgsql(database.TestConnectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", KundaliDbContext.Schema))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        var db = new KundaliDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private async Task<Guid> AccountAsync()
    {
        Skip.IfNot(database.IsAvailable, "Set MYVEDICSTAR_TEST_POSTGRES to enable disposable PostgreSQL tests.");
        await using var scope = database.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>()
            .UpsertAsync(new(Guid.NewGuid().ToString(), "ki@example.com", "First", "Last", "https://test.example"),
                default);
        return user.Id;
    }

    /// <summary>
    /// One entry per domain, deliberately mixing the three states: the two not-ready domains are the point, and they
    /// carry different reasons so a test that swapped them would still notice.
    /// </summary>
    private static KundaliInterpretationsSection Section(long sourceRevision) => new(
        sourceRevision,
        "prompt-1",
        "stub-model",
        "en",
        "traditional interpretation",
        [
            new KundaliInterpretationEntry("career", KundaliInterpretationStatuses.Ready, "A career text.",
                [new KundaliInterpretationFactor("graha:saturn:sign", "Saturn in Capricorn")], null, []),
            // Withheld: the chart exists, the product does not publish an interpretation for it.
            new KundaliInterpretationEntry("marriage", KundaliInterpretationStatuses.Withheld, null, [],
                KundaliInterpretationReasons.ContentRejected, []),
            // Not evaluated: nothing was assessed, which must stay distinguishable from withheld above.
            new KundaliInterpretationEntry("wellbeing", KundaliInterpretationStatuses.NotEvaluated, null, [],
                KundaliInterpretationReasons.AiDisabled, []),
            new KundaliInterpretationEntry("finance", KundaliInterpretationStatuses.Ready, "A finance text.", [],
                null, ["graha:invented:id"]),
            new KundaliInterpretationEntry("education", KundaliInterpretationStatuses.Ready, "An education text.", [],
                null, []),
        ],
        [new KundaliUnsupportedField("varshaphal", "engine-has-no-annual-chart")]);

    [SkippableFact]
    public async Task A_withheld_domain_round_trips_as_withheld_and_not_as_not_evaluated()
    {
        var account = await AccountAsync();
        var profile = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        const long sourceRevision = 7;

        await using (var write = await ContextAsync())
        {
            await new KundaliInterpretationStore(write)
                .SaveAsync(account, profile, revisionId, sourceRevision, "en", Section(sourceRevision),
                    DateTimeOffset.UtcNow);
        }

        await using var read = await ContextAsync();
        var stored = await new KundaliInterpretationStore(read)
            .ReadAsync(account, profile, sourceRevision, "en", "traditional interpretation", "prompt-1", "stub-model");

        Assert.NotNull(stored);
        var marriage = stored!.Sections.Single(s => s.Domain == "marriage");
        var wellbeing = stored.Sections.Single(s => s.Domain == "wellbeing");

        // Status AND reason, because a row that kept the status but lost the reason cannot explain itself to a reader.
        Assert.Equal(KundaliInterpretationStatuses.Withheld, marriage.Status);
        Assert.Equal(KundaliInterpretationReasons.ContentRejected, marriage.Reason);
        Assert.Null(marriage.Text);

        Assert.Equal(KundaliInterpretationStatuses.NotEvaluated, wellbeing.Status);
        Assert.Equal(KundaliInterpretationReasons.AiDisabled, wellbeing.Reason);

        // The distinction itself, asserted directly rather than inferred from the two checks above.
        Assert.NotEqual(marriage.Status, wellbeing.Status);
        Assert.NotEqual(marriage.Reason, wellbeing.Reason);
    }

    [SkippableFact]
    public async Task Every_domain_survives_the_round_trip_exactly_once()
    {
        var account = await AccountAsync();
        var profile = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        const long sourceRevision = 3;

        await using (var write = await ContextAsync())
        {
            await new KundaliInterpretationStore(write)
                .SaveAsync(account, profile, revisionId, sourceRevision, "en", Section(sourceRevision),
                    DateTimeOffset.UtcNow);
        }

        await using var read = await ContextAsync();
        var stored = await new KundaliInterpretationStore(read)
            .ReadAsync(account, profile, sourceRevision, "en", "traditional interpretation", "prompt-1", "stub-model");

        Assert.NotNull(stored);
        var domains = stored!.Sections.Select(s => s.Domain).ToArray();

        // None dropped, none duplicated, none invented - the list-level form of losing "withheld".
        Assert.Equal(AllDomains.Length, domains.Length);
        Assert.Equal(AllDomains.OrderBy(d => d, StringComparer.Ordinal), domains.OrderBy(d => d, StringComparer.Ordinal));
        Assert.Equal(domains.Length, domains.Distinct(StringComparer.Ordinal).Count());

        // Sorted, so an enumerate-and-zip client cannot silently pair the wrong reason with a domain.
        Assert.Equal(domains.OrderBy(d => d, StringComparer.Ordinal), domains);
    }

    [SkippableFact]
    public async Task A_profile_belonging_to_another_principal_is_indistinguishable_from_a_missing_one()
    {
        var owner = await AccountAsync();
        var stranger = await AccountAsync();
        var profile = Guid.NewGuid();
        var revisionId = Guid.NewGuid();

        await using (var write = await ContextAsync())
        {
            await new KundaliInterpretationStore(write)
                .SaveAsync(owner, profile, revisionId, 5, "en", Section(5), DateTimeOffset.UtcNow);
        }

        await using var read = await ContextAsync();
        var store = new KundaliInterpretationStore(read);

        // A real profile that belongs to somebody else.
        var foreign = await store.ReadAsync(stranger, profile, 5, "en", "traditional interpretation", "prompt-1",
            "stub-model");

        // A profile id that has never existed.
        var missing = await store.ReadAsync(stranger, Guid.NewGuid(), 5, "en", "traditional interpretation",
            "prompt-1", "stub-model");

        // Indistinguishable, not merely both non-2xx: the caller must not be able to tell these apart at all.
        Assert.Null(foreign);
        Assert.Null(missing);
        Assert.Equal(foreign is null, missing is null);
    }
}
