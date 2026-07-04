using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomotechsualBot.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModLogThreadLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ThreadId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    TitleConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModLogThreadLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserWarnings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ModeratorId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWarnings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModLogThreadLinks_GuildId_UserId",
                table: "ModLogThreadLinks",
                columns: new[] { "GuildId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWarnings_GuildId_UserId",
                table: "UserWarnings",
                columns: new[] { "GuildId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModLogThreadLinks");

            migrationBuilder.DropTable(
                name: "UserWarnings");
        }
    }
}
