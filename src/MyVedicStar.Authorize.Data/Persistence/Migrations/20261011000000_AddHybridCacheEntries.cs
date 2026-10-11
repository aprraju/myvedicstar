using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MyVedicStar.Authorize.Data.Persistence.Migrations;

/// <summary>Provider-compatible logged storage. Cache entries are infrastructure, not Authorize entities.</summary>
[DbContext(typeof(AuthorizeDbContext))]
[Migration("20261011000000_AddHybridCacheEntries")]
public sealed class AddHybridCacheEntries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("myvedicstar");
        migrationBuilder.Sql("""
            CREATE TABLE myvedicstar.cache_entries (
                id VARCHAR(449) COLLATE "C" PRIMARY KEY,
                value BYTEA NOT NULL,
                expiresattime TIMESTAMPTZ NOT NULL,
                slidingexpirationinseconds BIGINT NULL,
                absoluteexpiration TIMESTAMPTZ NULL
            );
            CREATE INDEX ix_cache_entries_expiresattime ON myvedicstar.cache_entries (expiresattime);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("cache_entries", "myvedicstar");
}
