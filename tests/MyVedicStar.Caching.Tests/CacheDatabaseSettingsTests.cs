using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MyVedicStar.Caching.Tests;

public sealed class CacheDatabaseSettingsTests
{
    private static IConfiguration Database(Dictionary<string, string?> values, string? azureClientId = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(item => "Database:" + item.Key, item => item.Value)
            .Append(new("AZURE_CLIENT_ID", azureClientId))).Build();

    [Fact]
    public void Uses_the_connection_string_without_managed_identity()
    {
        var settings = CacheServiceExtensions.CacheDatabaseSettings.From(Database(new() { ["ConnectionString"] = "Host=localhost;Database=db;Username=u;Password=p" }));
        Assert.False(settings.UseManagedIdentity);
        Assert.Equal("Host=localhost;Database=db;Username=u;Password=p", settings.ConnectionString);
    }

    [Fact]
    public void Requires_a_connection_string_without_managed_identity()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CacheServiceExtensions.CacheDatabaseSettings.From(Database(new())));
        Assert.Contains("Database:ConnectionString", error.Message);
    }

    [Fact]
    public void Builds_a_passwordless_TLS_connection_for_managed_identity_like_the_business_database()
    {
        var settings = CacheServiceExtensions.CacheDatabaseSettings.From(Database(new()
        {
            ["UseManagedIdentity"] = "true", ["Host"] = "example.postgres.database.azure.com", ["Name"] = "myvedicstar",
            ["Username"] = "id-myvedicstar-api-prod", ["SslMode"] = "VerifyFull",
        }));
        var connection = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
        Assert.True(settings.UseManagedIdentity);
        Assert.Null(settings.ManagedIdentityClientId);
        Assert.Equal("example.postgres.database.azure.com", connection.Host);
        Assert.Equal("myvedicstar", connection.Database);
        Assert.Equal("id-myvedicstar-api-prod", connection.Username);
        Assert.Equal(5432, connection.Port);
        Assert.Equal(SslMode.VerifyFull, connection.SslMode);
        Assert.Null(connection.Password);
    }

    [Fact]
    public void Uses_the_containers_user_assigned_identity_from_AZURE_CLIENT_ID_unless_configured()
    {
        var values = new Dictionary<string, string?> { ["UseManagedIdentity"] = "true", ["Host"] = "h", ["Name"] = "n", ["Username"] = "u" };
        Assert.Equal("6f8a921d-c99b-45be-b877-8b8cace0bf33",
            CacheServiceExtensions.CacheDatabaseSettings.From(Database(values, "6f8a921d-c99b-45be-b877-8b8cace0bf33")).ManagedIdentityClientId);
        values["ManagedIdentityClientId"] = "11111111-1111-1111-1111-111111111111";
        Assert.Equal("11111111-1111-1111-1111-111111111111",
            CacheServiceExtensions.CacheDatabaseSettings.From(Database(values, "6f8a921d-c99b-45be-b877-8b8cace0bf33")).ManagedIdentityClientId);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Name")]
    [InlineData("Username")]
    public void Managed_identity_requires_host_name_and_username(string missing)
    {
        var values = new Dictionary<string, string?> { ["UseManagedIdentity"] = "true", ["Host"] = "h", ["Name"] = "n", ["Username"] = "u" };
        values.Remove(missing);
        Assert.Throws<InvalidOperationException>(() => CacheServiceExtensions.CacheDatabaseSettings.From(Database(values)));
    }

    [Fact]
    public void Managed_identity_requires_TLS()
    {
        Assert.Throws<InvalidOperationException>(() => CacheServiceExtensions.CacheDatabaseSettings.From(Database(new()
        {
            ["UseManagedIdentity"] = "true", ["Host"] = "h", ["Name"] = "n", ["Username"] = "u", ["SslMode"] = "Disable",
        })));
    }
}
