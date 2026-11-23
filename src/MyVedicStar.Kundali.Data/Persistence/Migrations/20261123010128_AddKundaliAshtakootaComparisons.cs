using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <summary>
    /// KUN-15: adds the Ashtakoota comparison table (api#55).
    /// </summary>
    /// <remarks>
    /// <para><b>This migration is deliberately table-only.</b> A generated migration also proposed dropping and
    /// re-adding several unrelated check constraints and adding an <c>AshtakavargaJson</c> column that the committed
    /// snapshot has never contained. That drift predates this change: the model snapshot in
    /// <c>KundaliDbContextModelSnapshot.cs</c> was already behind the migrations applied to main, so scaffolding against
    /// it diffs unrelated history against the live model.</para>
    /// <para>Those operations were removed rather than shipped. Re-creating an existing column or constraint on a real
    /// database would fail the migration, and silently re-declaring constraints this issue does not own would make a
    /// KUN-15 change the place where a KUN-11 schema decision is re-litigated. The drift is reported on the PR as a
    /// separate finding with its own owner.</para>
    /// </remarks>
    public partial class AddKundaliAshtakootaComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KundaliAshtakootaComparisons",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PairKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProfileAId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileBId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionA = table.Column<long>(type: "bigint", nullable: false),
                    RevisionB = table.Column<long>(type: "bigint", nullable: false),
                    RulesVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliAshtakootaComparisons", x => x.Id);
                    table.CheckConstraint("CK_KundaliAshtakootaComparisons_DistinctProfiles", "\"ProfileAId\" <> \"ProfileBId\"");
                    table.CheckConstraint("CK_KundaliAshtakootaComparisons_Revisions", "\"RevisionA\" >= 0 AND \"RevisionB\" >= 0");
                    table.CheckConstraint("CK_KundaliAshtakootaComparisons_SortedPair", "\"ProfileAId\" < \"ProfileBId\"");
                    table.ForeignKey(
                        name: "FK_KundaliAshtakootaComparisons_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_KundaliAshtakootaComparisons_Identity",
                schema: "myvedicstarkundali",
                table: "KundaliAshtakootaComparisons",
                columns: new[] { "UserId", "PairKey", "RevisionA", "RevisionB", "RulesVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KundaliAshtakootaComparisons",
                schema: "myvedicstarkundali");
        }
    }
}
