using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnmatchedScoreboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "unmatched_scoreboard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FiukWins = table.Column<int>(type: "integer", nullable: false),
                    LanyokWins = table.Column<int>(type: "integer", nullable: false),
                    LastVictor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unmatched_scoreboard", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "unmatched_picks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Character = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ScoreboardId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unmatched_picks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_unmatched_picks_unmatched_scoreboard_ScoreboardId",
                        column: x => x.ScoreboardId,
                        principalTable: "unmatched_scoreboard",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_unmatched_picks_ScoreboardId",
                table: "unmatched_picks",
                column: "ScoreboardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unmatched_picks");

            migrationBuilder.DropTable(
                name: "unmatched_scoreboard");
        }
    }
}
