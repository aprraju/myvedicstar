using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <summary>
    /// KUN-17 (api#57): the report table and its two indexes, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>Generated with <c>dotnet ef migrations add</c> and then reduced to the statements this change actually
    /// needs, following the convention <c>AddKundaliAlertPreferences</c> already records. The generator also emitted
    /// 42 unrelated statements because the checked-in <c>KundaliDbContextModelSnapshot</c> on main is behind its own
    /// migrations - it never recorded <c>AshtakavargaJson</c> from #110, carried default <c>IX_..._UserId</c> index
    /// names that <c>InitialKundali</c> had already replaced, and held hand-written check-constraint bodies whose
    /// quoting does not match the model.</para>
    /// <para>Replaying that churn here would have been actively unsafe rather than untidy: it included
    /// <c>DropColumn("AshtakavargaJson")</c> on <c>KundaliResultRevisions</c>, which would discard every stored
    /// Ashtakavarga payload on a database where <c>InitialKundali</c> created the column - data loss disguised as a
    /// snapshot sync. The other 41 statements were drop-then-re-add pairs on five tables this migration does not own,
    /// several of which re-added a constraint with DOUBLED inner quotes, i.e. a different predicate, so a constraint
    /// would have silently stopped enforcing what it appears to enforce.</para>
    /// <para>The designer file next to this one IS the untouched generator output, so EF's target model remains
    /// authoritative and this migration cannot be recorded as applied against a model it did not create. The stale
    /// snapshot on main is therefore still stale, deliberately and visibly, and remains a separate defect.</para>
    /// </remarks>
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
