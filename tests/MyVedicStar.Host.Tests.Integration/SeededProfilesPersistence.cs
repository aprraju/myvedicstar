using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Business;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// In-memory stand-ins for the two ports the Vedic Profiles module needs, so a host test can hold an account id
/// without a database.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists (#93): the facade resolves the account BEFORE it validates, and that order is deliberate - validating
/// first would leak validation behaviour to callers who are not authenticated. The consequence is that on a host with no
/// registered account every write stops at account resolution, so no HTTP test could reach a single validation rule.
/// A boundary test that stopped there would assert nothing about the validator while looking like it did.
/// </para>
/// <para>
/// Only the account lookup and storage are replaced. The real facade, the real validator and the real
/// exception-to-400 mapping still run, which is the whole point: the question is whether an HTTP caller sees the
/// specific error, not whether a substitute can be made to return one.
/// </para>
/// </remarks>
internal sealed class SeededProfilesPersistence
{
    private readonly ConcurrentDictionary<Guid, VedicProfile> profiles = new();

    internal SeededProfilesPersistence(Guid accountId) => AccountId = accountId;

    /// <summary>The account id the host resolves for the test principal.</summary>
    internal Guid AccountId { get; }

    /// <summary>Profiles actually written. A validation test asserts this stays at zero.</summary>
    internal int StoredCount => profiles.Count;

    /// <summary>Seeds a profile directly, for tests that need an existing row to replace.</summary>
    internal VedicProfile Seed(VedicProfileDetails details, long revision = 1)
    {
        var profile = new VedicProfile(Guid.NewGuid(), details, revision);
        profiles[profile.Id] = profile;
        return profile;
    }

    /// <summary>Replaces the account reader and repository the host composed for the Vedic Profiles module.</summary>
    internal void Apply(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IVedicProfileAccountReader>();
        services.AddScoped<IVedicProfileAccountReader>(_ => new SeededAccountReader(AccountId));
        services.RemoveAll<IVedicProfileRepository>();
        services.AddScoped<IVedicProfileRepository>(_ => new SeededRepository(this));
    }

    private sealed class SeededAccountReader(Guid accountId) : IVedicProfileAccountReader
    {
        // No default name is offered: a name-less Self profile must surface the validator's 400 rather than have a value
        // invented for it, which matches the real adapter when the account has no usable name.
        public Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token) =>
            Task.FromResult(accountId);

        public Task<string?> GetDefaultNameAsync(ClaimsPrincipal user, CancellationToken token) =>
            Task.FromResult<string?>(null);
    }

    private sealed class SeededRepository(SeededProfilesPersistence seed) : IVedicProfileRepository
    {
        public Task<IReadOnlyList<VedicProfileSummary>> ListAsync(Guid accountId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<VedicProfileSummary>>(seed.profiles.Values
                .Select(profile => new VedicProfileSummary(profile.Id, profile.Details.Name ?? string.Empty,
                    profile.Details.Relationship, profile.Details.CustomRelationship, profile.Revision))
                .ToArray());

        public Task<VedicProfile?> GetAsync(Guid accountId, Guid id, CancellationToken token) =>
            Task.FromResult(seed.profiles.GetValueOrDefault(id));

        // No rule is re-implemented here. Anything that reaches Create/Replace has already passed the real validator, so
        // a second copy of a rule could only ever disagree with it.
        public Task<VedicProfile> CreateAsync(Guid accountId, VedicProfileDetails details, CancellationToken token)
        {
            var profile = new VedicProfile(Guid.NewGuid(), details, 1);
            seed.profiles[profile.Id] = profile;
            return Task.FromResult(profile);
        }

        public Task<VedicProfile> ReplaceAsync(Guid accountId, Guid id, VedicProfileDetails details, long revision,
            CancellationToken token)
        {
            if (!seed.profiles.TryGetValue(id, out var current))
                throw new ResourceNotFoundException(VedicProfileErrors.NotFound);
            if (current.Revision != revision) throw new VedicProfileConflictException(VedicProfileErrors.StaleRevision);
            var replaced = new VedicProfile(id, details, revision + 1);
            seed.profiles[id] = replaced;
            return Task.FromResult(replaced);
        }

        public Task<bool> DeleteAsync(Guid accountId, Guid id, CancellationToken token) =>
            Task.FromResult(seed.profiles.TryRemove(id, out _));
    }
}

