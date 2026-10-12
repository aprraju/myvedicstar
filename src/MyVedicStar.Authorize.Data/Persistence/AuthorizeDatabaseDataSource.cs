using Azure.Core;
using Azure.Identity;
using MyVedicStar.Authorize.Data.Options;
using Npgsql;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>One shared EF connection pool, with SDK-cached Entra tokens for managed identity.</summary>
internal sealed class AuthorizeDatabaseDataSource : IDisposable
{
    private static readonly TokenRequestContext PostgreSqlTokenRequest = new(["https://ossrdbms-aad.database.windows.net/.default"]);
    internal NpgsqlDataSource Source { get; }

    public AuthorizeDatabaseDataSource(DatabaseOptions options, TokenCredential? credential = null)
    {
        var builder = new NpgsqlDataSourceBuilder(options.BuildConnectionString()) { Name = "myvedicstar-business" };
        if (options.UseManagedIdentity)
        {
            credential ??= options.ManagedIdentityClientId is { } clientId
                ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
                : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
            var identity = credential;
            builder.UsePasswordProvider(
                _ => identity.GetToken(PostgreSqlTokenRequest, CancellationToken.None).Token,
                async (_, cancellationToken) => (await identity.GetTokenAsync(PostgreSqlTokenRequest, cancellationToken)).Token);
        }
        Source = builder.Build();
    }

    public void Dispose() => Source.Dispose();
}
