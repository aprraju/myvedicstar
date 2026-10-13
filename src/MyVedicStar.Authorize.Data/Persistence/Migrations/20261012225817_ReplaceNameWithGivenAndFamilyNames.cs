using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Authorize.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceNameWithGivenAndFamilyNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve legacy names intact; the next sign-in refreshes both fields from token claims.
            migrationBuilder.RenameColumn(
                name: "Name",
                schema: "myvedicstar",
                table: "User",
                newName: "given_name");

            migrationBuilder.AddColumn<string>(
                name: "family_name",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "myvedicstar"."User"
                SET "given_name" = LEFT(NULLIF(concat_ws(',', "given_name", "family_name"), ''), 256);
                """);

            migrationBuilder.DropColumn(
                name: "family_name",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.RenameColumn(
                name: "given_name",
                schema: "myvedicstar",
                table: "User",
                newName: "Name");
        }
    }
}
