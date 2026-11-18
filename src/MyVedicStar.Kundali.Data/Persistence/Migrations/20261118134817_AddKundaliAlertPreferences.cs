using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <summary>
    /// KUN-13: opt-in alert consent rows.
    /// </summary>
    /// <remarks>
    /// <para>Generated with <c>dotnet ef migrations add</c>, then reduced to the statements this change actually
    /// needs. The generator also emitted unrelated index and check-constraint churn, because the checked-in
    /// <c>KundaliDbContextModelSnapshot</c> on main is behind its own migrations (notably it never recorded
    /// <c>AshtakavargaJson</c> from #110). Replaying that churn here would drop and recreate constraints on
    /// tables this migration does not own, which is a schema risk with no benefit.</para>
    /// <para>The designer file next to this one IS the untouched generator output, so EF's target model is
    /// authoritative and this migration cannot be recorded as applied against a model it did not create.</para>
    /// </remarks>
    public partial class AddKundaliAlertPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KundaliAlertPreferences",
                schema: "myvedicstarkundali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsentedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundaliAlertPreferences", x => x.Id);
                    table.CheckConstraint("CK_KundaliAlertPreferences_Channel", "\"Channel\" IN ('email')");
                    table.ForeignKey(
                        name: "FK_KundaliAlertPreferences_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KundaliAlertPreferences_UserId",
                schema: "myvedicstarkundali",
                table: "KundaliAlertPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_KundaliAlertPreferences_UserProfileEvent",
                schema: "myvedicstarkundali",
                table: "KundaliAlertPreferences",
                columns: new[] { "UserId", "ProfileId", "EventType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KundaliAlertPreferences",
                schema: "myvedicstarkundali");
        }
    }
}
