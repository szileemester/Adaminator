using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWinnerOnlyAndSetsAndRequireMatchScoreType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // WinnerOnly/Sets no longer exist as ScoreType members, and Match.ScoreType is now fixed
            // at creation from the tournament's default (mirrors MatchFormat) so it can no longer be
            // null. One backfill covers both: any removed enum value on either table, and any legacy
            // NULL match row (from before ScoreType was always set at creation), all collapse to
            // 'Games' before the column is locked down to NOT NULL.
            migrationBuilder.Sql(
                """
                UPDATE tournaments SET "DefaultScoreType" = 'Games' WHERE "DefaultScoreType" IN ('WinnerOnly', 'Sets');
                UPDATE matches SET "ScoreType" = 'Games' WHERE "ScoreType" IS NULL OR "ScoreType" IN ('WinnerOnly', 'Sets');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ScoreType",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Games",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossy: the original WinnerOnly/Sets/NULL values aren't recoverable.
            migrationBuilder.AlterColumn<string>(
                name: "ScoreType",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
