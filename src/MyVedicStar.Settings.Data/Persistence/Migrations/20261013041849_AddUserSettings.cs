using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Settings.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "myvedicstarsettings");

            migrationBuilder.CreateTable(
                name: "UserSavedLocations",
                schema: "myvedicstarsettings",
                columns: table => new
                {
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    State = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Country = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identity = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSavedLocations", x => x.LocationId);
                    table.UniqueConstraint("AK_UserSavedLocations_UserId_LocationId", x => new { x.UserId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_UserSavedLocations_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                schema: "myvedicstarsettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultTradition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DefaultLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserSettings_UserSavedLocations_UserId_DefaultLocationId",
                        columns: x => new { x.UserId, x.DefaultLocationId },
                        principalSchema: "myvedicstarsettings",
                        principalTable: "UserSavedLocations",
                        principalColumns: new[] { "UserId", "LocationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserSettings_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSavedLocations_UserId_Identity",
                schema: "myvedicstarsettings",
                table: "UserSavedLocations",
                columns: new[] { "UserId", "Identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId_DefaultLocationId",
                schema: "myvedicstarsettings",
                table: "UserSettings",
                columns: new[] { "UserId", "DefaultLocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSettings",
                schema: "myvedicstarsettings");

            migrationBuilder.DropTable(
                name: "UserSavedLocations",
                schema: "myvedicstarsettings");
        }
    }
}
