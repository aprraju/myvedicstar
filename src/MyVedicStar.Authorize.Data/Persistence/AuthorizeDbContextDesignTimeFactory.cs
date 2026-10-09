using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API.
/// Applying migrations is done by the application (see <see cref="DatabaseMigrationHostedService"/>).
/// </summary>
internal sealed class AuthorizeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthorizeDbContext>
{
    public AuthorizeDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AuthorizeDbContext>();
        AuthorizeDbContextOptionsConfigurator.Configure(builder, options: null);
        return new AuthorizeDbContext(builder.Options);
    }
}
