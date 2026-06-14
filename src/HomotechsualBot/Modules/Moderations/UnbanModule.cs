using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;

public class UnbanModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public UnbanModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("unban", "Unban a member from the server by ID")]
    [RequireUserPermission(GuildPermission.BanMembers)]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task UnbanAsync(
        [Summary(description: "ID of user to unban")] ulong userId,
        [Summary(description: "Reason (optional)")] string? reason = null)
    {
        try
        {
            var guild = Context.Guild;

            // Get ban list
            var bans = await guild.GetBansAsync().FlattenAsync();
            var bannedUser = bans.FirstOrDefault(b => b.User.Id == userId)?.User;

            if (bannedUser == null)
            {
                await RespondAsync("❌ User not found in ban list.", ephemeral: true);
                return;
            }

            // Remove ban
            await guild.RemoveBanAsync(bannedUser, new RequestOptions
            {
                AuditLogReason = reason ?? "No reason provided"
            });
            await _logService.LogActionAsync(ModerationLogEntry.CreateUnknownTarget(
                ModerationActionType.Unban, bannedUser.Id, Context.User, reason));

            await RespondAsync(
                $"✅ Successfully unbanned **{bannedUser.Username}#{bannedUser.Discriminator}** (Reason: {reason ?? "No reason provided"})"
            );
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ Unban failed: {ex.Message}", ephemeral: true);
        }
    }
}
