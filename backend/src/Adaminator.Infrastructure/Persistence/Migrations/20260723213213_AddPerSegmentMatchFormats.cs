using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerSegmentMatchFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The generated defaultValue: "" is not a valid MatchFormat member name for the
            // string<->enum conversion - use a valid one ("Bo3", matching the entity's own in-code
            // default) so every existing row converts cleanly, then backfill each column below to the
            // value the domain's new fallback logic would have produced (SetDetails: every per-segment
            // format defaults to DefaultMatchFormat when not given explicitly).
            migrationBuilder.AddColumn<string>(
                name: "GrandFinalFormat",
                table: "tournaments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Bo3");

            migrationBuilder.AddColumn<string>(
                name: "GroupStageMatchFormat",
                table: "tournaments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Bo3");

            migrationBuilder.AddColumn<string>(
                name: "LowerBracketFormat",
                table: "tournaments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Bo3");

            migrationBuilder.AddColumn<string>(
                name: "UpperBracketFormat",
                table: "tournaments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Bo3");

            migrationBuilder.Sql(
                """
                UPDATE tournaments SET
                    "UpperBracketFormat" = "DefaultMatchFormat",
                    "LowerBracketFormat" = "DefaultMatchFormat",
                    "GrandFinalFormat" = "DefaultMatchFormat",
                    "GroupStageMatchFormat" = CASE WHEN "GroupStageFormat" = 'BestOfTwo' THEN 'Bo2' ELSE "DefaultMatchFormat" END;
                """);

            migrationBuilder.DropColumn(
                name: "GroupStageFormat",
                table: "tournaments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupStageFormat",
                table: "tournaments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.Sql(
                """
                UPDATE tournaments SET
                    "GroupStageFormat" = CASE WHEN "GroupStageMatchFormat" = 'Bo2' THEN 'BestOfTwo' ELSE 'Standard' END;
                """);

            migrationBuilder.DropColumn(
                name: "GrandFinalFormat",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "GroupStageMatchFormat",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "LowerBracketFormat",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "UpperBracketFormat",
                table: "tournaments");
        }
    }
}
