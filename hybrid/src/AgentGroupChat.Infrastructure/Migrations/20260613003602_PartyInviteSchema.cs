using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGroupChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PartyInviteSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoomInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostUserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpirationDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RedeemedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    RedeemedDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomInvites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInvites_RoomId",
                table: "RoomInvites",
                column: "RoomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomInvites");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Agents");
        }
    }
}
