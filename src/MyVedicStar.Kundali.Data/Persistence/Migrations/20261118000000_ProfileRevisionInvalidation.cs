using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVedicStar.Kundali.Data.Persistence.Migrations
{
    /// <summary>
    /// KUN-21 / KUN-22: records which profile revision each result was computed from, marks superseded revisions, and
    /// adds the transactional invalidation outbox. Idempotent by construction (ADD COLUMN IF NOT EXISTS / CREATE TABLE
    /// IF NOT EXISTS) because the module is rolled out DB-first by an authorized operator and must survive a retry.
    /// </summary>
    public partial class ProfileRevisionInvalidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows predate revision tracking. Zero means "unknown, and never equal to a real profile revision",
            // so every pre-existing result immediately reports as stale instead of being claimed as current; the client
            // regenerates and the correct revision is stamped from then on.
            migrationBuilder.Sql("""
                ALTER TABLE myvedicstarkundali."KundaliAggregates"
                    ADD COLUMN IF NOT EXISTS "ProfileRevision" bigint NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE myvedicstarkundali."KundaliResultRevisions"
                    ADD COLUMN IF NOT EXISTS "ProfileRevision" bigint NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE myvedicstarkundali."KundaliResultRevisions"
                    ADD COLUMN IF NOT EXISTS "SupersededByProfileRevision" bigint NULL;
                """);

            // Rows written before this migration carry their profile revision in the input snapshot they were generated
            // from; copying it back is exact, not inferred.
            migrationBuilder.Sql("""
                UPDATE myvedicstarkundali."KundaliResultRevisions" r
                SET "ProfileRevision" = s."ProfileRevision"
                FROM myvedicstarkundali."KundaliInputSnapshots" s
                WHERE s."UserId" = r."UserId" AND s."ProfileId" = r."ProfileId" AND s."RevisionId" = r."Id"
                  AND r."ProfileRevision" = 0;
                """);

            migrationBuilder.Sql("""
                UPDATE myvedicstarkundali."KundaliAggregates" a
                SET "ProfileRevision" = COALESCE((
                    SELECT MAX(r."ProfileRevision") FROM myvedicstarkundali."KundaliResultRevisions" r
                    WHERE r."UserId" = a."UserId" AND r."ProfileId" = a."ProfileId"), 0)
                WHERE a."ProfileRevision" = 0;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS myvedicstarkundali."KundaliProfileInvalidations" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "ProfileId" uuid NOT NULL,
                    "OldRevision" bigint NOT NULL,
                    "NewRevision" bigint NOT NULL,
                    "ChangeKind" character varying(32) NOT NULL,
                    "CancelledRevisionId" uuid NULL,
                    "PublishedAtUtc" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_KundaliProfileInvalidations" PRIMARY KEY ("Id"),
                    CONSTRAINT "CK_KundaliProfileInvalidations_Revision" CHECK ("NewRevision" > 0 AND "NewRevision" > "OldRevision"),
                    CONSTRAINT "CK_KundaliProfileInvalidations_ChangeKind" CHECK ("ChangeKind" IN ('display', 'birth-input', 'calculation-settings')),
                    CONSTRAINT "FK_KundaliProfileInvalidations_User_UserId" FOREIGN KEY ("UserId") REFERENCES myvedicstar."User" ("Id") ON DELETE RESTRICT
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "UX_KundaliProfileInvalidations_UserProfileRevision"
                    ON myvedicstarkundali."KundaliProfileInvalidations" ("UserId", "ProfileId", "NewRevision");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_KundaliProfileInvalidations_UserId"
                    ON myvedicstarkundali."KundaliProfileInvalidations" ("UserId");
                """);

            // Read path: history is ordered by owner, profile and revision; the active revision is looked up by id.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_KundaliResultRevisions_UserProfileProfileRevision"
                    ON myvedicstarkundali."KundaliResultRevisions" ("UserId", "ProfileId", "ProfileRevision");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS myvedicstarkundali."IX_KundaliResultRevisions_UserProfileProfileRevision";
                """);
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS myvedicstarkundali."KundaliProfileInvalidations";
                """);
            migrationBuilder.Sql("""
                ALTER TABLE myvedicstarkundali."KundaliResultRevisions" DROP COLUMN IF EXISTS "SupersededByProfileRevision";
                """);
            migrationBuilder.Sql("""
                ALTER TABLE myvedicstarkundali."KundaliResultRevisions" DROP COLUMN IF EXISTS "ProfileRevision";
                """);
            migrationBuilder.Sql("""
                ALTER TABLE myvedicstarkundali."KundaliAggregates" DROP COLUMN IF EXISTS "ProfileRevision";
                """);
        }
    }
}
