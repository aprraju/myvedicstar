using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.VedicProfiles.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialVedicProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "myvedicstarvedicprofiles");

            migrationBuilder.CreateTable(
                name: "VedicProfiles",
                schema: "myvedicstarvedicprofiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    BirthTimeMode = table.Column<int>(type: "integer", nullable: false),
                    BirthTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    BirthTimeFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    BirthTimeTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    TimeAccuracyNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LocationName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    TimezoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Gender = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Occupation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Relationship = table.Column<int>(type: "integer", nullable: false),
                    CustomRelationship = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VedicProfiles", x => x.Id);
                    table.CheckConstraint("CK_VedicProfiles_BirthTimeMode", "\"BirthTimeMode\" IN (0,1,2)");
                    table.CheckConstraint("CK_VedicProfiles_Coordinates", "\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_VedicProfiles_CustomRelationship", "((\"Relationship\" = 7 AND \"CustomRelationship\" IS NOT NULL AND \"CustomRelationship\" <> '') OR (\"Relationship\" <> 7 AND \"CustomRelationship\" IS NULL))");
                    table.CheckConstraint("CK_VedicProfiles_Gender", "\"Gender\" <> ''");
                    table.CheckConstraint("CK_VedicProfiles_Relationship", "\"Relationship\" BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_VedicProfiles_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_VedicProfiles_Times", "((\"BirthTimeMode\" = 0 AND \"BirthTime\" IS NOT NULL AND \"BirthTimeFrom\" = \"BirthTime\" AND \"BirthTimeTo\" = \"BirthTime\") OR (\"BirthTimeMode\" = 1 AND \"BirthTime\" IS NULL AND \"BirthTimeFrom\" IS NOT NULL AND \"BirthTimeTo\" IS NOT NULL AND \"BirthTimeTo\" >= \"BirthTimeFrom\") OR (\"BirthTimeMode\" = 2 AND \"BirthTime\" IS NULL AND \"BirthTimeFrom\" IS NULL AND \"BirthTimeTo\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_VedicProfiles_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "myvedicstar",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_VedicProfiles_Self",
                schema: "myvedicstarvedicprofiles",
                table: "VedicProfiles",
                column: "UserId",
                unique: true,
                filter: "\"Relationship\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VedicProfiles",
                schema: "myvedicstarvedicprofiles");
        }
    }
}
