using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGroupChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRemainingColorThemeProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccentHex",
                table: "TranscriptTurns");

            migrationBuilder.DropColumn(
                name: "AccentHex",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "BackgroundHex",
                table: "Agents");

            migrationBuilder.RenameColumn(
                name: "BackgroundHex",
                table: "TranscriptTurns",
                newName: "ColorTheme");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ColorTheme",
                table: "TranscriptTurns",
                newName: "BackgroundHex");

            migrationBuilder.AddColumn<string>(
                name: "AccentHex",
                table: "TranscriptTurns",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccentHex",
                table: "Agents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BackgroundHex",
                table: "Agents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
