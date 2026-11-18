using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKundaliAlertPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KundaliResultRevisions_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliRevisions_FailureReason",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliRevisions_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliRevisions_Status",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.DropIndex(
                name: "IX_KundaliProfileInvalidations_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliProfileInvalidations_ChangeKind",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliProfileInvalidations_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations");

            migrationBuilder.DropIndex(
                name: "IX_KundaliPreferences_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliPreferences_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences");

            migrationBuilder.DropIndex(
                name: "IX_KundaliInputSnapshots_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliInputSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_KundaliAlertPreferences_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliAlertPreferences");

            migrationBuilder.DropIndex(
                name: "IX_KundaliAggregates_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliAggregates_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates");

            migrationBuilder.AddColumn<string>(
                name: "AshtakavargaJson",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliRevisions_FailureReason",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                sql: "((\"\"Status\"\" = 3 AND \"\"FailureReasonCode\"\" IS NOT NULL) OR (\"\"Status\"\" <> 3 AND \"\"FailureReasonCode\"\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliRevisions_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                sql: "\"\"Revision\"\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliRevisions_Status",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                sql: "\"\"Status\"\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliProfileInvalidations_ChangeKind",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations",
                sql: "\"ChangeKind\" IN ('display', 'birth-input', 'calculation-settings')\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliProfileInvalidations_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations",
                sql: "\"Revision\" > 0 AND \"NewRevision\" > \"OldRevision\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliPreferences_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences",
                sql: "\"\"Revision\"\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliAggregates_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates",
                sql: "\"\"Revision\"\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliRevisions_FailureReason",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliRevisions_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliRevisions_Status",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliProfileInvalidations_ChangeKind",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliProfileInvalidations_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliPreferences_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_KundaliAggregates_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates");

            migrationBuilder.DropColumn(
                name: "AshtakavargaJson",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions");

            migrationBuilder.CreateIndex(
                name: "IX_KundaliResultRevisions_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliRevisions_FailureReason",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                sql: "((\"Status\" = 3 AND \"FailureReasonCode\" IS NOT NULL) OR (\"Status\" <> 3 AND \"FailureReasonCode\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliRevisions_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                sql: "\"Revision\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliRevisions_Status",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "IX_KundaliProfileInvalidations_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliProfileInvalidations_ChangeKind",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations",
                sql: "\"ChangeKind\" IN ('display', 'birth-input', 'calculation-settings')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliProfileInvalidations_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliProfileInvalidations",
                sql: "\"NewRevision\" > 0 AND \"NewRevision\" > \"OldRevision\"");

            migrationBuilder.CreateIndex(
                name: "IX_KundaliPreferences_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliPreferences_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences",
                sql: "\"Revision\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_KundaliInputSnapshots_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliInputSnapshots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KundaliAlertPreferences_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliAlertPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KundaliAggregates_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_KundaliAggregates_Revision",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates",
                sql: "\"Revision\" >= 0");
        }
    }
}
