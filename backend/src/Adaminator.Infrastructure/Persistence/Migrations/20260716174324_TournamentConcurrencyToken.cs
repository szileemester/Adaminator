using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records that Tournament now uses a shadow "Version" property mapped to Postgres's built-in
    /// xmin system column as an optimistic concurrency token. xmin already exists on every row, so
    /// there is no actual column to add/drop here - only EF's migration history needs to know.
    /// </summary>
    public partial class TournamentConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
