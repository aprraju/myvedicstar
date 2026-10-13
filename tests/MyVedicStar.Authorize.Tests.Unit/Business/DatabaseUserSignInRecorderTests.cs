using System.Net;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class DatabaseUserSignInRecorderTests
{
    private static readonly Guid InternalUserId = Guid.Parse("0199a3b4-0000-7000-8000-000000000001");
    private static readonly IPAddress ClientIp = IPAddress.Parse("203.0.113.7");

    private static readonly SignedInUser User = new("object-id", "John", "Smith", "john.smith@example.com", "https://accounts.google.com", "session-key");

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUserLoginRepository _userLoginRepository = Substitute.For<IUserLoginRepository>();
    private readonly InlineUnitOfWork _unitOfWork = new();
    private readonly DatabaseUserSignInRecorder _recorder;

    public DatabaseUserSignInRecorderTests()
    {
        _userRepository.UpsertAsync(Arg.Any<UserProfileData>(), Arg.Any<CancellationToken>()).Returns(InternalUserId);
        _recorder = new DatabaseUserSignInRecorder(_userRepository, _userLoginRepository, _unitOfWork);
    }

    [Fact]
    public async Task RecordSignInAsync_UpsertsTokenProfileKeyedByObjectId()
    {
        await _recorder.RecordSignInAsync(User, ClientIp, CancellationToken.None);

        await _userRepository.Received(1).UpsertAsync(
            new UserProfileData("object-id", "john.smith@example.com", "John", "Smith", User.Issuer),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignInAsync_RecordsLoginForInternalUserIdWithClientIp()
    {
        await _recorder.RecordSignInAsync(User, ClientIp, CancellationToken.None);

        await _userLoginRepository.Received(1).AddAsync(new UserLoginData(InternalUserId, ClientIp, User.SessionKey), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignInAsync_UpsertAndLoginRunInOneTransaction()
    {
        await _recorder.RecordSignInAsync(User, ClientIp, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.TransactionCount);
    }

    [Fact]
    public async Task RecordSignInAsync_UpsertFails_DoesNotRecordLogin()
    {
        _userRepository.UpsertAsync(Arg.Any<UserProfileData>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DataAccessException("could not save"));

        await Assert.ThrowsAsync<DataAccessException>(
            () => _recorder.RecordSignInAsync(User, ClientIp, CancellationToken.None));

        await _userLoginRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task RecordSignInAsync_NameLongerThanColumn_StoresShortenedName()
    {
        var user = User with { GivenName = new string('a', UserProfileData.MaxNameLength + 10), FamilyName = new string('b', UserProfileData.MaxNameLength + 10) };

        await _recorder.RecordSignInAsync(user, ClientIp, CancellationToken.None);

        await _userRepository.Received(1).UpsertAsync(
            Arg.Is<UserProfileData>(profile => profile.GivenName == new string('a', UserProfileData.MaxNameLength) && profile.FamilyName == new string('b', UserProfileData.MaxNameLength)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignInAsync_NameCutInsideSurrogatePair_DropsTheWholeCharacter()
    {
        // "𝔸" is one character made of two UTF-16 code units; the limit falls between them.
        var user = User with { GivenName = new string('a', UserProfileData.MaxNameLength - 1) + "𝔸" };

        await _recorder.RecordSignInAsync(user, ClientIp, CancellationToken.None);

        await _userRepository.Received(1).UpsertAsync(
            Arg.Is<UserProfileData>(profile => profile.GivenName == new string('a', UserProfileData.MaxNameLength - 1)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignInAsync_EmailLongerThanColumn_StoresNoEmail()
    {
        var user = User with { Email = new string('a', UserProfileData.MaxEmailLength) + "@example.com" };

        await _recorder.RecordSignInAsync(user, ClientIp, CancellationToken.None);

        await _userRepository.Received(1).UpsertAsync(
            Arg.Is<UserProfileData>(profile => profile.Email == null && profile.GivenName == User.GivenName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignInAsync_UserIdLongerThanColumn_ThrowsAuthenticationFailedAndStoresNothing()
    {
        var user = User with { ExternalUserId = new string('a', UserProfileData.MaxExternalIdLength + 1) };

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _recorder.RecordSignInAsync(user, ClientIp, CancellationToken.None));

        Assert.Equal(0, _unitOfWork.TransactionCount);
        await _userRepository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task Logout_UsesRegisteredIdentityAndAppendsType99WithoutRegistration()
    {
        _userRepository.FindIdAsync(User.Issuer, User.ExternalUserId, Arg.Any<CancellationToken>()).Returns(InternalUserId);
        await _recorder.RecordSignOutAsync(User, ClientIp, default);
        await _userLoginRepository.Received(1).AddAsync(new UserLoginData(InternalUserId, ClientIp, User.SessionKey, 99), Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
        Assert.Equal(1, _unitOfWork.TransactionCount);
    }

    [Fact]
    public async Task Logout_UnknownAccount_DoesNotCreateAudit()
    {
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _recorder.RecordSignOutAsync(User, null, default));
        await _userLoginRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    /// <summary>Runs the operation directly and counts how many transactions were requested.</summary>
    private sealed class InlineUnitOfWork : IAuthorizeUnitOfWork
    {
        public int TransactionCount { get; private set; }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
        {
            TransactionCount++;
            return operation(cancellationToken);
        }
    }
}
