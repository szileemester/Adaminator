using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDoubleEliminationRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LoserToMatchId",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LoserToSlotA",
                table: "matches",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WinnerToMatchId",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WinnerToSlotA",
                table: "matches",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoserToMatchId",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "LoserToSlotA",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "WinnerToMatchId",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "WinnerToSlotA",
                table: "matches");
        }
    }
}
