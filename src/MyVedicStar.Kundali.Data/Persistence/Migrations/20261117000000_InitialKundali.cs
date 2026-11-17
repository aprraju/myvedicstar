using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialKundali : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "myvedicstarkundali");

            migrationBuilder.CreateTable(
                name: "KundaliAggregates",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliAggregates", x => x.Id);
                    table.CheckConstraint("CK_KundaliAggregates_Revision", "\"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_KundaliAggregates_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KundaliPreferences",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChartStyle = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliPreferences", x => x.Id);
                    table.CheckConstraint("CK_KundaliPreferences_Revision", "\"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_KundaliPreferences_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KundaliResultRevisions",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EngineVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EphemerisVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleSetVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TimezoneDataVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ayanamsa = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsPartial = table.Column<bool>(type: "boolean", nullable: false),
                    OverviewJson = table.Column<string>(type: "text", nullable: true),
                    ChartsJson = table.Column<string>(type: "text", nullable: true),
                    PlanetsJson = table.Column<string>(type: "text", nullable: true),
                    DashasJson = table.Column<string>(type: "text", nullable: true),
                    YogasJson = table.Column<string>(type: "text", nullable: true),
                    AshtakavargaJson = table.Column<string>(type: "text", nullable: true),
                    BalaJson = table.Column<string>(type: "text", nullable: true),
                    TransitsJson = table.Column<string>(type: "text", nullable: true),
                    InterpretationsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliResultRevisions", x => x.Id);
                    table.CheckConstraint("CK_KundaliRevisions_FailureReason", "((\"Status\" = 3 AND \"FailureReasonCode\" IS NOT NULL) OR (\"Status\" <> 3 AND \"FailureReasonCode\" IS NULL))");
                    table.CheckConstraint("CK_KundaliRevisions_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_KundaliRevisions_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_KundaliResultRevisions_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KundaliInputSnapshots",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    BirthTimeMode = table.Column<int>(type: "integer", nullable: false),
                    BirthTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    BirthTimeFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    BirthTimeTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    TimezoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProfileRevision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliInputSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KundaliInputSnapshots_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KundaliAggregates_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_KundaliAggregates_UserProfile",
                schema: "myvedicstarkundali",
                table: "KundaliAggregates",
                columns: new[] { "UserId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KundaliInputSnapshots_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliInputSnapshots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_KundaliInputSnapshots_UserProfileHash",
                schema: "myvedicstarkundali",
                table: "KundaliInputSnapshots",
                columns: new[] { "UserId", "ProfileId", "InputSnapshotHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KundaliPreferences_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_KundaliPreferences_UserProfile",
                schema: "myvedicstarkundali",
                table: "KundaliPreferences",
                columns: new[] { "UserId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KundaliResultRevisions_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_KundaliRevisions_UserProfileRevision",
                schema: "myvedicstarkundali",
                table: "KundaliResultRevisions",
                columns: new[] { "UserId", "ProfileId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KundaliAggregates",
                schema: "myvedicstarkundali");

            migrationBuilder.DropTable(
                name: "KundaliInputSnapshots",
                schema: "myvedicstarkundali");

            migrationBuilder.DropTable(
                name: "KundaliPreferences",
                schema: "myvedicstarkundali");

            migrationBuilder.DropTable(
                name: "KundaliResultRevisions",
                schema: "myvedicstarkundali");
        }
    }
}
