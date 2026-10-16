using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Integration;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Business;

public sealed record ProfileUpdateRequest(long Revision, Dictionary<string, string?> Changes);

internal interface IProfileService
{
    Task<ManagedProfile> ReadAsync(ClaimsPrincipal principal, CancellationToken token);
    Task<ManagedProfile> UpdateAsync(ClaimsPrincipal principal, ProfileUpdateRequest request, CancellationToken token);
    Task<ManagedProfile> RetrySyncAsync(ClaimsPrincipal principal, CancellationToken token);
}

internal sealed partial class ProfileService(IProfileRepository repository, SignedInUserReader reader, IGraphProfileClient graph,
    ILogger<ProfileService> logger) : IProfileService
{
    private IProfileRepository Repository => repository;
    private SignedInUser Account(ClaimsPrincipal principal) => reader.GetSignedInUser(principal, Guid.Empty.ToString());

    public async Task<ManagedProfile> ReadAsync(ClaimsPrincipal principal, CancellationToken token)
    {
        var account = Account(principal);
        return (await Repository.ReadAsync(account.Issuer, account.ExternalUserId, token)).Profile;
    }

    public async Task<ManagedProfile> UpdateAsync(ClaimsPrincipal principal, ProfileUpdateRequest request, CancellationToken token)
    {
        if (request.Changes is null || request.Revision < 0) throw new ValidationException("Provide profile changes and a valid revision.");
        var account = Account(principal);
        var changes = Validate(request.Changes);
        var original = await Repository.ReadAsync(account.Issuer, account.ExternalUserId, token);
        ValidateAddress(changes.GetValueOrDefault("country", original.Profile.Country), changes.GetValueOrDefault("postalCode", original.Profile.PostalCode));
        var work = await Repository.UpdateAsync(account.Issuer, account.ExternalUserId, request.Revision, changes, token);
        return await SynchronizeAsync(principal, account, work, token);
    }

    public async Task<ManagedProfile> RetrySyncAsync(ClaimsPrincipal principal, CancellationToken token)
    {
        var account = Account(principal);
        return await SynchronizeAsync(principal, account, await Repository.ReadAsync(account.Issuer, account.ExternalUserId, token), token);
    }

    private async Task<ManagedProfile> SynchronizeAsync(ClaimsPrincipal principal, SignedInUser account, ProfileSyncWork work, CancellationToken token)
    {
        if (!work.Profile.NameSyncPending) return work.Profile;
        try
        {
            var oid = principal.FindFirst("oid")?.Value ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            if (oid is null || oid != account.ExternalUserId) throw new AuthenticationFailedException("The token does not identify an Entra object.");
            return await Repository.SynchronizeAsync(account.Issuer, account.ExternalUserId, async (latest, cancellation) =>
            {
                var changes = new Dictionary<string, string?>();
                if (latest.FirstNamePending) changes["givenName"] = latest.Profile.FirstName;
                if (latest.LastNamePending) changes["surname"] = latest.Profile.LastName;
                // Entra requires a display name, so it is only ever replaced, never cleared.
                if (latest.DisplayNamePending && latest.Profile.DisplayName is not null) changes["displayName"] = latest.Profile.DisplayName;
                await graph.UpdateNamesAsync(account.Issuer, oid, changes, cancellation);
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Local changes and pending flags were committed first. A retry only resends pending name attributes.
            LogSyncPending(logger, error.GetType().Name);
            return work.Profile;
        }
    }

    // Partial-failure strategy: the database is the source of truth. UpdateAsync commits the edited values with
    // per-attribute pending flags before Graph is called; SynchronizeAsync sends only pending attributes under a row
    // lock and clears the flags after Graph succeeds. A failure leaves the flags set, the save still succeeds with
    // NameSyncPending = true, sign-in never overwrites the edited names, and POST profile/sync (or the next save)
    // resends the same idempotent PATCH.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Profile name synchronization remains pending ({ErrorType})")]
    private static partial void LogSyncPending(ILogger logger, string errorType);

    internal static Dictionary<string, string?> Validate(IReadOnlyDictionary<string, string?> input)
    {
        var limits = new Dictionary<string, int>
        {
            ["firstName"] = 256,
            ["lastName"] = 256,
            ["displayName"] = 256,
            ["streetAddress"] = 256,
            ["streetAddress2"] = 256,
            ["country"] = 256,
            ["state"] = 256,
            ["city"] = 256,
            ["postalCode"] = 20,
            ["email"] = 320,
            ["phoneNumber"] = 40
        };
        var changes = new Dictionary<string, string?>();
        foreach (var (field, raw) in input)
        {
            if (!limits.TryGetValue(field, out var max)) throw new ValidationException("Unknown profile field.");
            var value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            if (value?.Length > max || value?.Any(char.IsControl) == true) throw new ValidationException($"{field} contains invalid or excessive text.");
            if (field == "displayName" && value is null) throw new ValidationException("Enter a display name.");
            if (field == "email" && value is not null && (!MailAddress.TryCreate(value, out var address) || address.Address != value || !value.Contains('@')))
                throw new ValidationException("Enter a valid email address.");
            if (field == "phoneNumber" && value is not null && (!Regex.IsMatch(value, @"^\+?[0-9 ()\-.]+$", RegexOptions.None, TimeSpan.FromMilliseconds(100)) || value.Count(char.IsDigit) is < 7 or > 15))
                throw new ValidationException("Enter a phone number with 7–15 digits.");
            changes[field] = value;
        }
        return changes;
    }

    private static void ValidateAddress(string? country, string? postal)
    {
        if (postal is null) return;
        var pattern = country switch { "US" => @"^\d{5}(-\d{4})?$", "CA" => @"^[ABCEGHJ-NPRSTVXY]\d[ABCEGHJ-NPRSTV-Z] ?\d[ABCEGHJ-NPRSTV-Z]\d$", "IN" => @"^[1-9]\d{5}$", _ => @"^[\p{L}\p{N} -]{1,20}$" };
        if (!Regex.IsMatch(postal, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) throw new ValidationException("Enter a valid ZIP / Postal Code for your country.");
    }
}
