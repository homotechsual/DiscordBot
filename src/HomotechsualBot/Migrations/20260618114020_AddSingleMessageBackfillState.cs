using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomotechsualBot.Migrations
{
    /// <inheritdoc />
    public partial class AddSingleMessageBackfillState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "BackfillBeforeMessageId",
                table: "SingleMessageChannelStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BackfillInProgress",
                table: "SingleMessageChannelStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<ulong>(
                name: "GuildId",
                table: "SingleMessageChannelStates",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackfillBeforeMessageId",
                table: "SingleMessageChannelStates");

            migrationBuilder.DropColumn(
                name: "BackfillInProgress",
                table: "SingleMessageChannelStates");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "SingleMessageChannelStates");
        }
    }
}
