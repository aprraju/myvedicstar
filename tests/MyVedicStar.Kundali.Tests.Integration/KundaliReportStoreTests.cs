using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Tests.Integration.Postgres;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Data;
using MyVedicStar.Kundali.Shared;
using Xunit;

namespace MyVedicStar.Kundali.Tests.Integration;

/// <summary>
/// KUN-17 (api#57) report persistence against real PostgreSQL: owner isolation and the not-owned/not-found
/// indistinguishability the store's contract requires.
/// </summary>
/// <remarks>
/// These properties cannot be proven in memory. An in-memory provider would happily return a row for a wrong account and
/// leave the predicate untested, which is exactly the defect (a foreign id reading as forbidden rather than missing) the
/// requirement exists to prevent.
/// </remarks>
public sealed class KundaliReportStoreTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
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
            .UpsertAsync(new(Guid.NewGuid().ToString(), $"r{Guid.NewGuid():N}@example.com", "First", "Last",
                "https://test.example"), default);
        return user.Id;
    }

    /// <summary>The short variant's sections, from the closed set this branch has. Deterministic and stored, so two
    /// enqueues of the same revision are the same record.</summary>
    private static KundaliReportRequest Request() =>
        new(KundaliReportVariants.Short,
            [KundaliSections.Overview, KundaliSections.Planets, KundaliSections.Dashas]);

    [SkippableFact]
    public async Task The_store_cannot_return_another_accounts_report()
    {
        var owner = await AccountAsync();
        var stranger = await AccountAsync();
        var profile = Guid.NewGuid();

        Guid reportId;
        await using (var context = await ContextAsync())
        {
            var store = new KundaliReportStore(context, TimeProvider.System);
            reportId = (await store.EnqueueAsync(owner, profile, Request(), 7, TimeProvider.System, default)).ReportId;
        }

        await using (var context = await ContextAsync())
        {
            var store = new KundaliReportStore(context, TimeProvider.System);

            // The owner sees it; that half matters, or a store that returned null for everyone would pass the rest.
            Assert.NotNull(await store.FindAsync(owner, profile, reportId, default));

            // The stranger does not - and this is asserted as a null, not as a thrown forbidden, because a throw would
            // tell the caller the row exists.
            Assert.Null(await store.FindAsync(stranger, profile, reportId, default));

            // Listing is scoped too: a foreign profile lists empty rather than erroring.
            Assert.Empty(await store.ListAsync(stranger, profile, default));
        }
    }

    [SkippableFact]
    public async Task A_foreign_report_id_and_a_missing_one_are_indistinguishable()
    {
        var owner = await AccountAsync();
        var stranger = await AccountAsync();
        var profile = Guid.NewGuid();

        Guid foreignReportId;
        await using (var context = await ContextAsync())
        {
            var store = new KundaliReportStore(context, TimeProvider.System);
            foreignReportId = (await store.EnqueueAsync(owner, profile, Request(), 7, TimeProvider.System, default))
                .ReportId;
        }

        var missingReportId = Guid.NewGuid();

        await using (var context = await ContextAsync())
        {
            var store = new KundaliReportStore(context, TimeProvider.System);

            // THE assertion of this test: the two calls must be the same observation, not merely both "not found".
            // Comparing the results to each other catches an implementation that returns, say, null for one and an
            // empty record for the other while the HTTP layer maps both to 404.
            var foreign = await store.FindAsync(stranger, profile, foreignReportId, default);
            var missing = await store.FindAsync(stranger, profile, missingReportId, default);

            Assert.Null(foreign);
            Assert.Null(missing);
            Assert.Equal(foreign, missing);
        }
    }

    [SkippableFact]
    public async Task A_repeat_request_for_the_same_revision_returns_one_report()
    {
        var account = await AccountAsync();
        var profile = Guid.NewGuid();

        await using var context = await ContextAsync();
        var store = new KundaliReportStore(context, TimeProvider.System);
        var first = await store.EnqueueAsync(account, profile, Request(), 7, TimeProvider.System, default);
        var second = await store.EnqueueAsync(account, profile, Request(), 7, TimeProvider.System, default);

        // Same report, not a second one: one revision cannot have two reports a caller could tell apart.
        Assert.Equal(first.ReportId, second.ReportId);
        Assert.Single(await store.ListAsync(account, profile, default));
    }

    [SkippableFact]
    public async Task The_database_rejects_a_status_outside_the_closed_set()
    {
        var account = await AccountAsync();
        var profile = Guid.NewGuid();

        await using var context = await ContextAsync();
        var store = new KundaliReportStore(context, TimeProvider.System);
        var report = await store.EnqueueAsync(account, profile, Request(), 7, TimeProvider.System, default);

        // The CHECK constraint is the last line of defence. A future writer that skips the business layer still cannot
        // store a status the contract does not define.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => store.SettleAsync(account, profile, report.ReportId,
            "partially-ready", null, null, [], TimeProvider.System, default));
    }
}
