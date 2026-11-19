using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomotechsualBot.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubWorkflowSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubWorkflowSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Workflow = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubWorkflowSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubWorkflowSubscriptions_GitHubTrackedRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "GitHubTrackedRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubWorkflowSubscriptions_RepositoryId_Workflow_ChannelId",
                table: "GitHubWorkflowSubscriptions",
                columns: new[] { "RepositoryId", "Workflow", "ChannelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubWorkflowSubscriptions");
        }
    }
}
