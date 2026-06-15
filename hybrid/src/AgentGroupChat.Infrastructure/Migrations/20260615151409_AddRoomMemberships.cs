using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGroupChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoomMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomMemberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomMemberships_RoomId",
                table: "RoomMemberships",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomMemberships_RoomId_UserId",
                table: "RoomMemberships",
                columns: new[] { "RoomId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomMemberships");
        }
    }
}
