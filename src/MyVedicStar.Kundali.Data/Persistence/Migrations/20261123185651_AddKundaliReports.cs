using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKundaliReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KundaliReports",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Variant = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Sections = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DocumentReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FailuresJson = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliReports", x => x.Id);
                    table.CheckConstraint("CK_KundaliReports_CompletedAt", "(\"Status\" IN ('ready', 'failed') AND \"CompletedAtUtc\" IS NOT NULL) OR (\"Status\" NOT IN ('ready', 'failed') AND \"CompletedAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_KundaliReports_SourceRevision", "\"SourceRevision\" > 0");
                    table.CheckConstraint("CK_KundaliReports_Status", "\"Status\" IN ('queued', 'running', 'ready', 'failed')");
                    table.CheckConstraint("CK_KundaliReports_Variant", "\"Variant\" IN ('short', 'detailed')");
                    table.ForeignKey(
                        name: "FK_KundaliReports_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KundaliReports_UserProfileRequested",
                schema: "myvedicstarkundali",
                table: "KundaliReports",
                columns: new[] { "UserId", "ProfileId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_KundaliReports_UserProfileRevision",
                schema: "myvedicstarkundali",
                table: "KundaliReports",
                columns: new[] { "UserId", "ProfileId", "SourceRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KundaliReports",
                schema: "myvedicstarkundali");
        }
    }
}
