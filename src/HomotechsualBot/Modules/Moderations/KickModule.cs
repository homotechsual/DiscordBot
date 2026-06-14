using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;
using System;
using System.Threading.Tasks;

namespace DiscordBot.Modules.Moderations;

public class KickModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public KickModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("kick", "Kick a member from the server")]
    [RequireUserPermission(GuildPermission.KickMembers)]   // requires the user calling the command to have kick permissions
    [RequireBotPermission(GuildPermission.KickMembers)]    // requires the bot to have kick permissions
    public async Task KickAsync(
        [Summary(description: "User to kick")] SocketGuildUser user,
        [Summary(description: "Reason for kick (optional)")] string? reason = null)
    {
        if (user.Id == Context.User.Id)
        {
            await RespondAsync("❌ You cannot kick yourself!", ephemeral: true);
            return;
        }

        var bot = Context.Guild.CurrentUser;

        // check bot's role hierarchy compared to target user
        if (bot.Hierarchy <= user.Hierarchy)
        {
            await RespondAsync("❌ Cannot kick someone with a role higher than or equal to the bot.", ephemeral: true);
            return;
        }

        try
        {
            await user.KickAsync(reason ?? "No reason provided");
            await _logService.LogActionAsync(ModerationLogEntry.Create(
                ModerationActionType.Kick, user, Context.User, reason));
            await RespondAsync($"✅ Successfully kicked {user.Username} (Reason: {reason ?? "No reason provided"})");
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ Kick failed: {ex.Message}", ephemeral: true);
        }
    }
}
