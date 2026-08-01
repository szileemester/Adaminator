using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelaxParticipantNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_participants_TournamentId_Name",
                table: "participants");

            migrationBuilder.CreateIndex(
                name: "IX_participants_TournamentId_Name",
                table: "participants",
                columns: new[] { "TournamentId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_participants_TournamentId_Name",
                table: "participants");

            migrationBuilder.CreateIndex(
                name: "IX_participants_TournamentId_Name",
                table: "participants",
                columns: new[] { "TournamentId", "Name" },
                unique: true);
        }
    }
}
