using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Persistence.Entities;

namespace MyVedicStar.Authorize.Data.Users;

internal sealed class UserLoginRepository : IUserLoginRepository
{
    private readonly AuthorizeDbContext _dbContext;

    public UserLoginRepository(AuthorizeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(UserLoginData login, CancellationToken cancellationToken)
    {
        _dbContext.UserLogins.Add(new UserLoginEntity
        {
            UserId = login.UserId,
            IpAddress = login.IpAddress,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
