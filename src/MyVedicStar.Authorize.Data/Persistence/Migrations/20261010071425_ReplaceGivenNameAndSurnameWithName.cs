using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Authorize.Data.Persistence.Migrations
{
    /// <summary>
    /// Stores the token's display name instead of the given name and surname formerly read from Microsoft Graph.
    /// Existing rows keep their name: it is built from the old columns before they are dropped.
    /// </summary>
    public partial class ReplaceGivenNameAndSurnameWithName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // concat_ws skips NULLs; NULLIF turns "no parts" into NULL. LEFT keeps within the new column's limit.
            migrationBuilder.Sql(
                """
                UPDATE "myvedicstar"."User"
                SET "Name" = LEFT(NULLIF(concat_ws(' ', NULLIF(btrim("GivenName"), ''), NULLIF(btrim("Surname"), '')), ''), 256)
                """);

            migrationBuilder.DropColumn(
                name: "GivenName",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "Surname",
                schema: "myvedicstar",
                table: "User");
        }

        /// <inheritdoc />
        /// <remarks>The name cannot be split back reliably, so it is restored into GivenName and Surname stays empty.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GivenName",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Surname",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql("""UPDATE "myvedicstar"."User" SET "GivenName" = "Name" """);

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "myvedicstar",
                table: "User");
        }
    }
}
