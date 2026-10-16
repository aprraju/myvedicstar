using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Authorize.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DisplayNameSyncPending",
                schema: "myvedicstar",
                table: "User",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "DisplayNameSyncPending",
                schema: "myvedicstar",
                table: "User");
        }
    }
}
