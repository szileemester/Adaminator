using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchResultEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CompletionSequence",
                table: "matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreType",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "score_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    ScoreA = table.Column<int>(type: "integer", nullable: true),
                    ScoreB = table.Column<int>(type: "integer", nullable: true),
                    ParticipantAWon = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_score_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_score_entries_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_score_entries_MatchId_SequenceNumber",
                table: "score_entries",
                columns: new[] { "MatchId", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "score_entries");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "CompletionSequence",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "ScoreType",
                table: "matches");
        }
    }
}
