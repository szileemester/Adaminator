using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupStagePlayoffVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The generated defaultValue: "" is not a valid enum member name for the string<->enum
            // conversion - use the members that describe what every existing Group Stage + Playoff
            // already is, namely round-robin groups feeding a double elimination playoff. No backfill
            // is needed: these defaults are the legacy behaviour, so existing rows convert cleanly and
            // keep playing exactly as before.
            migrationBuilder.AddColumn<string>(
                name: "GroupStageKind",
                table: "tournaments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "RoundRobin");

            migrationBuilder.AddColumn<string>(
                name: "PlayoffKind",
                table: "tournaments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "DoubleElimination");

            // 0 means "the largest capacity the roster fills" - the rule the playoff size was derived by
            // before it became an explicit setting, so existing tournaments need no backfill either.
            migrationBuilder.AddColumn<int>(
                name: "PlayoffSize",
                table: "tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 0 means "ceil(log2 roster)". Only ever read for a Swiss group stage, which no existing
            // tournament uses.
            migrationBuilder.AddColumn<int>(
                name: "SwissRounds",
                table: "tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupStageKind",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "PlayoffKind",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "PlayoffSize",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "SwissRounds",
                table: "tournaments");
        }
    }
}
