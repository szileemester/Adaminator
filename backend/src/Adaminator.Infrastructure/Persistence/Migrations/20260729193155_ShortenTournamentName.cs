using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShortenTournamentName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres refuses to narrow the column while any row is longer than the new limit - it
            // fails the ALTER outright rather than truncating - so anything over 50 is cut first.
            // Names are not unique, so shortening two of them to the same value breaks no constraint.
            migrationBuilder.Sql("""UPDATE tournaments SET "Name" = LEFT("Name", 50) WHERE LENGTH("Name") > 50;""");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "tournaments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "tournaments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);
        }
    }
}
