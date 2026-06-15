using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGroupChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSchemasForUserIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HumanParticipants");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Rooms",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "PromptSamples",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RoomId",
                table: "LogEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "ImageModels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "ImageConnections",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "AiModels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "AiConnections",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_UserId",
                table: "Rooms",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptSamples_UserId",
                table: "PromptSamples",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_RoomId",
                table: "LogEntries",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageModels_UserId",
                table: "ImageModels",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageConnections_UserId",
                table: "ImageConnections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_UserId",
                table: "AppSettings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_UserId",
                table: "AiModels",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_UserId",
                table: "AiConnections",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiConnections_AspNetUsers_UserId",
                table: "AiConnections",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AiModels_AspNetUsers_UserId",
                table: "AiModels",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AppSettings_AspNetUsers_UserId",
                table: "AppSettings",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ImageConnections_AspNetUsers_UserId",
                table: "ImageConnections",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ImageModels_AspNetUsers_UserId",
                table: "ImageModels",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LogEntries_Rooms_RoomId",
                table: "LogEntries",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MemoryBlocks_Rooms_RoomId",
                table: "MemoryBlocks",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PromptSamples_AspNetUsers_UserId",
                table: "PromptSamples",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_AspNetUsers_UserId",
                table: "Rooms",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SceneArchives_Rooms_RoomId",
                table: "SceneArchives",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TranscriptTurns_Rooms_RoomId",
                table: "TranscriptTurns",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiConnections_AspNetUsers_UserId",
                table: "AiConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_AiModels_AspNetUsers_UserId",
                table: "AiModels");

            migrationBuilder.DropForeignKey(
                name: "FK_AppSettings_AspNetUsers_UserId",
                table: "AppSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_ImageConnections_AspNetUsers_UserId",
                table: "ImageConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_ImageModels_AspNetUsers_UserId",
                table: "ImageModels");

            migrationBuilder.DropForeignKey(
                name: "FK_LogEntries_Rooms_RoomId",
                table: "LogEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_MemoryBlocks_Rooms_RoomId",
                table: "MemoryBlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_PromptSamples_AspNetUsers_UserId",
                table: "PromptSamples");

            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_AspNetUsers_UserId",
                table: "Rooms");

            migrationBuilder.DropForeignKey(
                name: "FK_SceneArchives_Rooms_RoomId",
                table: "SceneArchives");

            migrationBuilder.DropForeignKey(
                name: "FK_TranscriptTurns_Rooms_RoomId",
                table: "TranscriptTurns");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_UserId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_PromptSamples_UserId",
                table: "PromptSamples");

            migrationBuilder.DropIndex(
                name: "IX_LogEntries_RoomId",
                table: "LogEntries");

            migrationBuilder.DropIndex(
                name: "IX_ImageModels_UserId",
                table: "ImageModels");

            migrationBuilder.DropIndex(
                name: "IX_ImageConnections_UserId",
                table: "ImageConnections");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_UserId",
                table: "AppSettings");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_UserId",
                table: "AiModels");

            migrationBuilder.DropIndex(
                name: "IX_AiConnections_UserId",
                table: "AiConnections");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PromptSamples");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "LogEntries");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ImageModels");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ImageConnections");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AiConnections");

            migrationBuilder.CreateTable(
                name: "HumanParticipants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    AccentHex = table.Column<string>(type: "TEXT", nullable: false),
                    AppearanceSummary = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundHex = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPlayerCharacter = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ParticipationMode = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    TtsVoice = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumanParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HumanParticipants_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HumanParticipants_RoomId",
                table: "HumanParticipants",
                column: "RoomId");
        }
    }
}
