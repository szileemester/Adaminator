using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the single scoreboard row up front. The write endpoint is anonymous and the page gets
    /// passed round at a party, so two people saving the very first result at once is an ordinary event,
    /// not a rare one - and both would have found no row, both created one with the same fixed id, and
    /// the slower one would have hit a primary key violation surfacing as a 500. With the row always
    /// present, a concurrent save is just a save.
    /// </summary>
    public partial class SeedUnmatchedScoreboardRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                INSERT INTO unmatched_scoreboard ("Id", "FiukWins", "LanyokWins", "LastVictor", "UpdatedAt")
                VALUES ('5c0eb0a4-0000-4000-8000-000000000001', 0, 0, NULL, NOW())
                ON CONFLICT ("Id") DO NOTHING;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            // Only the untouched zero state is removed; a scoreboard someone has since written is left alone.
            migrationBuilder.Sql("""
                DELETE FROM unmatched_scoreboard
                WHERE "Id" = '5c0eb0a4-0000-4000-8000-000000000001'
                  AND "FiukWins" = 0 AND "LanyokWins" = 0 AND "LastVictor" IS NULL;
                """);
    }
}
