using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShortenParticipantName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres refuses to narrow the column while any row is longer, so oversized names are cut
            // first. Unlike tournament names these are unique per tournament, and cutting two long
            // names can land them on the same 30 characters - which would then break the unique index
            // the ALTER has to preserve. So collisions get a "~2", "~3" discriminator, and the ordering
            // hands the plain spelling to whoever needed no truncation at all. LOWER() in the partition
            // keeps the domain's case-insensitive rule satisfied too, not just the case-sensitive index.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT
                        "Id",
                        "Name",
                        LEFT("Name", 30) AS truncated,
                        ROW_NUMBER() OVER (
                            PARTITION BY "TournamentId", LOWER(LEFT("Name", 30))
                            ORDER BY (LENGTH("Name") > 30), "Position", "Id"
                        ) AS rn
                    FROM participants
                ),
                resolved AS (
                    SELECT
                        "Id",
                        CASE
                            WHEN rn = 1 THEN truncated
                            ELSE LEFT(truncated, 29 - LENGTH(rn::text)) || '~' || rn::text
                        END AS final_name
                    FROM ranked
                )
                UPDATE participants AS p
                SET "Name" = r.final_name
                FROM resolved AS r
                WHERE p."Id" = r."Id" AND p."Name" <> r.final_name;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "participants",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "participants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);
        }
    }
}
