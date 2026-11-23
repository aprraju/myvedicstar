using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKundaliInterpretations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KundaliInterpretations",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    InterpretationDomain = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    UnknownFactorReferencesJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliInterpretations", x => x.Id);
                    table.CheckConstraint("CK_KundaliInterpretations_Domain", "\"InterpretationDomain\" IN ('career', 'marriage', 'wellbeing', 'finance', 'education')");
                    table.CheckConstraint("CK_KundaliInterpretations_Reason", "((\"Status\" = 'ready' AND \"Reason\" IS NULL) OR (\"Status\" <> 'ready' AND \"Reason\" IS NOT NULL))");
                    table.CheckConstraint("CK_KundaliInterpretations_SourceRevision", "\"SourceRevision\" > 0");
                    table.CheckConstraint("CK_KundaliInterpretations_Status", "\"Status\" IN ('ready', 'withheld', 'not-evaluated')");
                    table.CheckConstraint("CK_KundaliInterpretations_Text", "((\"Status\" = 'ready' AND \"Text\" IS NOT NULL) OR (\"Status\" <> 'ready' AND \"Text\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_KundaliInterpretations_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KundaliInterpretations_ProfileRevisionLocale",
                schema: "myvedicstarkundali",
                table: "KundaliInterpretations",
                columns: new[] { "UserId", "ProfileId", "RevisionId", "Locale" });

            migrationBuilder.CreateIndex(
                name: "UX_KundaliInterpretations_ProfileRevisionLocaleDomain",
                schema: "myvedicstarkundali",
                table: "KundaliInterpretations",
                columns: new[] { "UserId", "ProfileId", "SourceRevision", "Locale", "InterpretationDomain" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KundaliInterpretations",
                schema: "myvedicstarkundali");
        }
    }
}
