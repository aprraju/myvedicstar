using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Settings.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChartPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ayanamsa",
                schema: "myvedicstarsettings",
                table: "UserSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "lahiri");

            migrationBuilder.AddColumn<string>(
                name: "ChartStyle",
                schema: "myvedicstarsettings",
                table: "UserSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "south-indian");

            // Legacy accounts are provisioned outside this migration historically; give every account
            // a stored settings row so rollout has durable defaults before sign-in (H2/AC5).
            // Additive, duplicate-safe, and never overwrites existing settings.
            migrationBuilder.Sql("""
                INSERT INTO myvedicstarsettings."UserSettings"
                    ("UserId", "DefaultTradition", "DefaultLocationId", "Revision", "ChartStyle", "Ayanamsa")
                SELECT u."Id", NULL, NULL, 0, 'south-indian', 'lahiri'
                FROM myvedicstar."User" AS u
                ON CONFLICT ("UserId") DO NOTHING;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_Ayanamsa",
                schema: "myvedicstarsettings",
                table: "UserSettings",
                sql: "\"Ayanamsa\" IN ('lahiri','raman','krishnamurti','true-chitra')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_ChartStyle",
                schema: "myvedicstarsettings",
                table: "UserSettings",
                sql: "\"ChartStyle\" IN ('north-indian','south-indian','east-indian')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_Ayanamsa",
                schema: "myvedicstarsettings",
                table: "UserSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_ChartStyle",
                schema: "myvedicstarsettings",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "Ayanamsa",
                schema: "myvedicstarsettings",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "ChartStyle",
                schema: "myvedicstarsettings",
                table: "UserSettings");
        }
    }
}
