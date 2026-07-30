using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "participants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing rosters have no recorded order, so seed it from the order they are displayed in
            // today (alphabetical). Without this every row keeps Position 0 and the roster comes back in
            // whatever order the database happens to return.
            migrationBuilder.Sql(
                """
                UPDATE participants AS p
                SET "Position" = ordered.rn
                FROM (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "TournamentId" ORDER BY "Name") AS rn
                    FROM participants
                ) AS ordered
                WHERE p."Id" = ordered."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Position",
                table: "participants");
        }
    }
}
