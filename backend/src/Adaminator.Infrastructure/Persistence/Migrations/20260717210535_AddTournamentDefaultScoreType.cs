using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentDefaultScoreType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultScoreType",
                table: "tournaments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Games");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultScoreType",
                table: "tournaments");
        }
    }
}
