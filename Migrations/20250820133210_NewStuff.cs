using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Play929Backend.Migrations
{
    /// <inheritdoc />
    public partial class NewStuff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Duration",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "EndedAt",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "GameSessions");

            migrationBuilder.RenameColumn(
                name: "GameType",
                table: "GameSessions",
                newName: "SessionToken");

            migrationBuilder.RenameColumn(
                name: "GameId",
                table: "GameSessions",
                newName: "Id");

            migrationBuilder.AddColumn<string>(
                name: "GameName",
                table: "GameSessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "GameSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GameLaunchTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LaunchToken = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Used = table.Column<bool>(type: "boolean", nullable: false),
                    GameSessionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameLaunchTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameLaunchTokens_GameSessions_GameSessionId",
                        column: x => x.GameSessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameLaunchTokens_GameSessionId",
                table: "GameLaunchTokens",
                column: "GameSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameLaunchTokens");

            migrationBuilder.DropColumn(
                name: "GameName",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "GameSessions");

            migrationBuilder.RenameColumn(
                name: "SessionToken",
                table: "GameSessions",
                newName: "GameType");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "GameSessions",
                newName: "GameId");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "Duration",
                table: "GameSessions",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAt",
                table: "GameSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "GameSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
