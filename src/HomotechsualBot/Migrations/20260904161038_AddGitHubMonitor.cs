using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomotechsualBot.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubMonitor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubMonitorSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    DefaultChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    PollIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 10),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubMonitorSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GitHubTrackedRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    IssuesEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    IssuesChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    PullRequestsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    PullRequestsChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ActionsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ActionsChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ReleasesEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    ReleasesChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubTrackedRepositories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubTrackedRepositories_Owner_Name",
                table: "GitHubTrackedRepositories",
                columns: new[] { "Owner", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubMonitorSettings");

            migrationBuilder.DropTable(
                name: "GitHubTrackedRepositories");
        }
    }
}
