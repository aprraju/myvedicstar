using System.Net;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Integration.Identity;

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

    public async Task<string> RecordSignInAsync(
        string externalUserId,
        ExternalUserProfile profile,
        IPAddress? clientIpAddress,
        CancellationToken cancellationToken)
    {
        var userId = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _userRepository.UpsertAsync(ToUserProfileData(externalUserId, profile), token);
                await _userLoginRepository.AddAsync(new UserLoginData(id, clientIpAddress), token);
                return id;
            },
            cancellationToken);

        return userId.ToString();
    }

    // The validated token's object ID is the key, not the value echoed back by the provider.
    private static UserProfileData ToUserProfileData(string externalUserId, ExternalUserProfile profile) => new(
        externalUserId,
        profile.Email,
        profile.GivenName,
        profile.Surname);
}
