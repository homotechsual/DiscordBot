using DiscordBot.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomotechsualBot.Migrations
{
    [DbContext(typeof(HomotechsualBotContext))]
    [Migration("20261123100000_AddGitHubIssueSubscriptions")]
    public partial class AddGitHubIssueSubscriptions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubIssueSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    IncludeLabels = table.Column<string>(type: "TEXT", nullable: true),
                    ExcludeLabels = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubIssueSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubIssueSubscriptions_GitHubTrackedRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "GitHubTrackedRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubIssueSubscriptions_RepositoryId_ChannelId",
                table: "GitHubIssueSubscriptions",
                columns: new[] { "RepositoryId", "ChannelId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GitHubIssueSubscriptions");
        }
    }
}
