using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Registers the account if needed and appends its session event in one database transaction.
/// </summary>
internal sealed partial class DatabaseUserSignInRecorder : IUserSignInRecorder
{
    private readonly IUserRepository _userRepository;
    private readonly IUserLoginRepository _userLoginRepository;
    private readonly IAuthorizeUnitOfWork _unitOfWork;
    private readonly IEnumerable<IAccountSignInHandler> _handlers;
    private readonly ILogger<DatabaseUserSignInRecorder> _logger;
    private readonly AccountInitializationOptions _initialization;

    public DatabaseUserSignInRecorder(
        IUserRepository userRepository,
        IUserLoginRepository userLoginRepository,
        IAuthorizeUnitOfWork unitOfWork,
        IEnumerable<IAccountSignInHandler> handlers,
        ILogger<DatabaseUserSignInRecorder> logger,
        IOptions<AccountInitializationOptions> initialization)
    {
        _userRepository = userRepository;
        _userLoginRepository = userLoginRepository;
        _unitOfWork = unitOfWork;
        _handlers = handlers;
        _logger = logger;
        _initialization = initialization.Value;
    }

    public async Task<SignedInUserNames> RecordSignInAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
    {
        var profile = ToUserProfileData(user);

        var registered = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var stored = await _userRepository.UpsertAsync(profile, token);
                await _userLoginRepository.AddAsync(new UserLoginData(stored.Id, clientIpAddress, user.SessionKey), token);
                return stored;
            },
            cancellationToken);
        // The identity transaction has committed. Settings failures must not undo successful sign-in.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_initialization.TimeoutSeconds));
        foreach (var handler in _handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await handler.EnsureAccountDefaultsAsync(registered.Id, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // Deliberately omit exception contents and identity/profile values from this boundary log.
                LogAccountInitializationFailure(_logger, exception.GetType().Name);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new SignedInUserNames(registered.DisplayName, registered.GivenName, registered.FamilyName);
    }

    public Task RecordSignOutAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken) =>
        _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var userId = await _userRepository.FindIdAsync(user.Issuer, user.ExternalUserId, token)
                ?? throw new ResourceNotFoundException("The registered user was not found.");
            await _userLoginRepository.AddAsync(new UserLoginData(userId, clientIpAddress, user.SessionKey, 99), token);
            return userId;
        }, cancellationToken);

    /// <summary>
    /// Fits the token's values to what can be stored, so an unusually long claim never blocks sign-in: each
    /// name is shortened, and an email address too long to be valid is not stored. The user ID is never shortened,
    /// because a shortened ID could match another user.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">The user ID is too long to store.</exception>
    private static UserProfileData ToUserProfileData(SignedInUser user)
    {
        if (user.ExternalUserId.Length > UserProfileData.MaxExternalIdLength || user.Issuer.Length > UserProfileData.MaxIssuerLength)
        {
            throw new AuthenticationFailedException(
                "The token does not identify a user.",
                $"The user ID claim is {user.ExternalUserId.Length} characters; at most {UserProfileData.MaxExternalIdLength} can be stored.");
        }

        var email = user.Email?.Length <= UserProfileData.MaxEmailLength ? user.Email : null;
        return new UserProfileData(user.ExternalUserId, email, Truncate(user.GivenName, UserProfileData.MaxNameLength),
            Truncate(user.FamilyName, UserProfileData.MaxNameLength), user.Issuer, Truncate(user.DisplayName, UserProfileData.MaxNameLength));
    }

    /// <summary>Shortens <paramref name="value"/> to at most <paramref name="maxLength"/> characters without splitting a surrogate pair.</summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..length].TrimEnd();
    }
}
