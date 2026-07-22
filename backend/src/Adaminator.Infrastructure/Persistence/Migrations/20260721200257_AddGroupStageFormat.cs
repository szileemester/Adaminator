using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupStageFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default to a valid enum member name (not "") so existing rows round-trip through the
            // string<->enum conversion; Standard is the neutral default for all types.
            migrationBuilder.AddColumn<string>(
                name: "GroupStageFormat",
                table: "tournaments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupStageFormat",
                table: "tournaments");
        }
    }
}
