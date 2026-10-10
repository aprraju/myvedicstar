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

    private static readonly SignedInUser User = new("object-id", "John Smith", "john.smith@example.com");

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
            new UserProfileData("object-id", "john.smith@example.com", "John Smith"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSignInAsync_RecordsLoginForInternalUserIdWithClientIp()
    {
        await _recorder.RecordSignInAsync(User, ClientIp, CancellationToken.None);

        await _userLoginRepository.Received(1).AddAsync(new UserLoginData(InternalUserId, ClientIp), Arg.Any<CancellationToken>());
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
