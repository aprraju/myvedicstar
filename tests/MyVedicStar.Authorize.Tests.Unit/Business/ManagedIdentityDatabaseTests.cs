using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Data.DependencyInjection;
using MyVedicStar.Authorize.Data.Options;
using MyVedicStar.Authorize.Data.Persistence;
using Npgsql;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class ManagedIdentityDatabaseTests
{
    [Fact]
    public void ProductionSettings_RegisterSharedPoolWithoutConnectionStringOrEagerTokenRequest()
    {
        const string clientId = "11111111-2222-3333-4444-555555555555";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:UseManagedIdentity"] = "true", ["Database:Host"] = "example.postgres.database.azure.com",
            ["Database:Name"] = "myvedicstar", ["Database:Username"] = "api-identity", ["Database:SslMode"] = "Require",
            ["Database:CommandTimeout"] = "00:00:30", ["Database:MaxRetryDelay"] = "00:00:05", ["AZURE_CLIENT_ID"] = clientId,
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddAuthorizeData(configuration).BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        Assert.Equal(clientId, options.ManagedIdentityClientId);
        var pool = services.GetRequiredService<AuthorizeDatabaseDataSource>();
        using var first = services.CreateScope();
        using var second = services.CreateScope();
        Assert.Same(pool, first.ServiceProvider.GetRequiredService<AuthorizeDatabaseDataSource>());
        Assert.Same(pool, second.ServiceProvider.GetRequiredService<AuthorizeDatabaseDataSource>());
        var connection = new NpgsqlConnectionStringBuilder(first.ServiceProvider.GetRequiredService<AuthorizeDbContext>().Database.GetConnectionString());
        Assert.Equal("myvedicstar", connection.Database);
        Assert.Equal("api-identity", connection.Username);
        Assert.Equal(SslMode.Require, connection.SslMode);
        Assert.False(connection.ContainsKey("Password"));
    }

    [Theory]
    [InlineData(SslMode.Disable)]
    [InlineData(SslMode.Allow)]
    [InlineData(SslMode.Prefer)]
    public void ManagedIdentity_RejectsUnencryptedConnections(SslMode sslMode)
    {
        var options = ValidOptions();
        options.SslMode = sslMode;
        Assert.False(IsValid(options));
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Name")]
    [InlineData("Username")]
    public void ManagedIdentity_RejectsMissingDatabaseSettings(string setting)
    {
        var options = ValidOptions();
        if (setting == "Host") options.Host = null;
        if (setting == "Name") options.Name = null;
        if (setting == "Username") options.Username = null;
        Assert.False(IsValid(options));
    }

    [Fact]
    public void PasswordAuthentication_StillRequiresConnectionString()
    {
        var options = ValidOptions();
        options.UseManagedIdentity = false;
        Assert.False(IsValid(options));
        options.ConnectionString = "Host=localhost;Database=test;Username=test;Password=test";
        Assert.True(IsValid(options));
    }

    private static DatabaseOptions ValidOptions() => new()
    {
        UseManagedIdentity = true, Host = "example.postgres.database.azure.com", Name = "test", Username = "api-identity",
        CommandTimeout = TimeSpan.FromSeconds(30), MaxRetryDelay = TimeSpan.FromSeconds(5),
    };
    private static bool IsValid(DatabaseOptions options) => Validator.TryValidateObject(options, new ValidationContext(options), [], true);
}
