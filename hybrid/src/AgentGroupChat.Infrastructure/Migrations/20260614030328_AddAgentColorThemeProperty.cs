using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGroupChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentColorThemeProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorTheme",
                table: "Agents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorTheme",
                table: "Agents");
        }
    }
}
