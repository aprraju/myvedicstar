using System.Net;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Upserts the user and inserts the login audit record in one database transaction.
/// </summary>
internal sealed class DatabaseUserSignInRecorder : IUserSignInRecorder
{
    private readonly IUserRepository _userRepository;
    private readonly IUserLoginRepository _userLoginRepository;
    private readonly IAuthorizeUnitOfWork _unitOfWork;

    public DatabaseUserSignInRecorder(
        IUserRepository userRepository,
        IUserLoginRepository userLoginRepository,
        IAuthorizeUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _userLoginRepository = userLoginRepository;
        _unitOfWork = unitOfWork;
    }

    public Task RecordSignInAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
    {
        var profile = ToUserProfileData(user);

        return _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var userId = await _userRepository.UpsertAsync(profile, token);
                await _userLoginRepository.AddAsync(new UserLoginData(userId, clientIpAddress), token);
                return userId;
            },
            cancellationToken);
    }

    /// <summary>
    /// Fits the token's values to what can be stored, so an unusually long claim never blocks sign-in: the display
    /// name is shortened, and an email address too long to be valid is not stored. The user ID is never shortened,
    /// because a shortened ID could match another user.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">The user ID is too long to store.</exception>
    private static UserProfileData ToUserProfileData(SignedInUser user)
    {
        if (user.ExternalUserId.Length > UserProfileData.MaxExternalIdLength)
        {
            throw new AuthenticationFailedException(
                "The token does not identify a user.",
                $"The user ID claim is {user.ExternalUserId.Length} characters; at most {UserProfileData.MaxExternalIdLength} can be stored.");
        }

        var email = user.Email?.Length <= UserProfileData.MaxEmailLength ? user.Email : null;
        return new UserProfileData(user.ExternalUserId, email, Truncate(user.Name, UserProfileData.MaxNameLength));
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
