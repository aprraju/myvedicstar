using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Authorize.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuerAndLoginLogoutAuditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserLogin_User_UserId",
                schema: "myvedicstar",
                table: "UserLogin");

            migrationBuilder.DropIndex(
                name: "UX_User_ExternalId",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserLogin",
                schema: "myvedicstar",
                table: "UserLogin");

            migrationBuilder.RenameTable(
                name: "UserLogin",
                schema: "myvedicstar",
                newName: "loginaudit",
                newSchema: "myvedicstar");

            migrationBuilder.RenameColumn(
                name: "LoginTimeUtc",
                schema: "myvedicstar",
                table: "loginaudit",
                newName: "Time");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "myvedicstar",
                table: "loginaudit",
                newName: "AuditId");

            migrationBuilder.RenameIndex(
                name: "IX_UserLogin_UserId_LoginTimeUtc",
                schema: "myvedicstar",
                table: "loginaudit",
                newName: "IX_loginaudit_UserId_Time");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "myvedicstar",
                table: "User",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Issuer",
                schema: "myvedicstar",
                table: "User",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            // Existing accounts were Entra-only; backfill the deployed tenant without changing account IDs.
            migrationBuilder.Sql("""
                UPDATE myvedicstar."User"
                SET "Issuer" = 'https://cbece845-e124-4de7-89d3-96a71abb7b83.ciamlogin.com/cbece845-e124-4de7-89d3-96a71abb7b83/v2.0'
                WHERE "Issuer" = '';
                """);

            migrationBuilder.AddColumn<string>(
                name: "SessionKey",
                schema: "myvedicstar",
                table: "loginaudit",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                schema: "myvedicstar",
                table: "loginaudit",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddPrimaryKey(
                name: "PK_loginaudit",
                schema: "myvedicstar",
                table: "loginaudit",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "UX_User_Issuer_ExternalId",
                schema: "myvedicstar",
                table: "User",
                columns: new[] { "Issuer", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_loginaudit_User_Session_Type",
                schema: "myvedicstar",
                table: "loginaudit",
                columns: new[] { "UserId", "SessionKey", "Type" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_loginaudit_Type",
                schema: "myvedicstar",
                table: "loginaudit",
                sql: "\"Type\" IN (1, 99)");

            migrationBuilder.AddForeignKey(
                name: "FK_loginaudit_User_UserId",
                schema: "myvedicstar",
                table: "loginaudit",
                column: "UserId",
                principalSchema: "myvedicstar",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_loginaudit_User_UserId",
                schema: "myvedicstar",
                table: "loginaudit");

            migrationBuilder.DropIndex(
                name: "UX_User_Issuer_ExternalId",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropPrimaryKey(
                name: "PK_loginaudit",
                schema: "myvedicstar",
                table: "loginaudit");

            migrationBuilder.DropIndex(
                name: "UX_loginaudit_User_Session_Type",
                schema: "myvedicstar",
                table: "loginaudit");

            migrationBuilder.DropCheckConstraint(
                name: "CK_loginaudit_Type",
                schema: "myvedicstar",
                table: "loginaudit");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "Issuer",
                schema: "myvedicstar",
                table: "User");

            migrationBuilder.DropColumn(
                name: "SessionKey",
                schema: "myvedicstar",
                table: "loginaudit");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "myvedicstar",
                table: "loginaudit");

            migrationBuilder.RenameTable(
                name: "loginaudit",
                schema: "myvedicstar",
                newName: "UserLogin",
                newSchema: "myvedicstar");

            migrationBuilder.RenameColumn(
                name: "Time",
                schema: "myvedicstar",
                table: "UserLogin",
                newName: "LoginTimeUtc");

            migrationBuilder.RenameColumn(
                name: "AuditId",
                schema: "myvedicstar",
                table: "UserLogin",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "IX_loginaudit_UserId_Time",
                schema: "myvedicstar",
                table: "UserLogin",
                newName: "IX_UserLogin_UserId_LoginTimeUtc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserLogin",
                schema: "myvedicstar",
                table: "UserLogin",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "UX_User_ExternalId",
                schema: "myvedicstar",
                table: "User",
                column: "ExternalId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserLogin_User_UserId",
                schema: "myvedicstar",
                table: "UserLogin",
                column: "UserId",
                principalSchema: "myvedicstar",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
