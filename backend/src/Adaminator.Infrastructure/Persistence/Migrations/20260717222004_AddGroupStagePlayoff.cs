using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupStagePlayoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GroupCount",
                table: "tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroupIndex",
                table: "participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GroupIndex",
                table: "matches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupCount",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "GroupIndex",
                table: "participants");

            migrationBuilder.DropColumn(
                name: "GroupIndex",
                table: "matches");
        }
    }
}
