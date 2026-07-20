using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTiebreakerPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default to a valid enum member name (not "") so existing rows round-trip through the
            // string<->enum conversion; ComputedThenMatch is the neutral default for all types.
            migrationBuilder.AddColumn<string>(
                name: "TiebreakerPolicy",
                table: "tournaments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ComputedThenMatch");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TiebreakerPolicy",
                table: "tournaments");
        }
    }
}
