using System.Net;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;

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
        return _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var userId = await _userRepository.UpsertAsync(ToUserProfileData(user), token);
                await _userLoginRepository.AddAsync(new UserLoginData(userId, clientIpAddress), token);
                return userId;
            },
            cancellationToken);
    }

    private static UserProfileData ToUserProfileData(SignedInUser user) => new(user.ExternalUserId, user.Email, user.Name);
}
