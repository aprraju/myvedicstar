using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Authorize.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FirstNameSyncPending",
                schema: "myvedicstar",
                table: "User",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LastNameSyncPending",
                schema: "myvedicstar",
                table: "User",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileManaged",
                schema: "myvedicstar",
                table: "User",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "ProfileRevision",
                schema: "myvedicstar",
                table: "User",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "State",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetAddress",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetAddress2",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "Country",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "FirstNameSyncPending",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "LastNameSyncPending",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "ProfileManaged",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "ProfileRevision",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "State",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "StreetAddress",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "StreetAddress2",
                schema: "myvedicstar",
                table: "User");
        }
    }
}
