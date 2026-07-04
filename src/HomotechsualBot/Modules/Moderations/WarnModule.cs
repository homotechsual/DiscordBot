using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;

public class WarnModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;
    private readonly WarningService _warningService;

    public WarnModule(ModerationLogService logService, WarningService warningService)
    {
        _logService = logService;
        _warningService = warningService;
    }

    [SlashCommand("warn", "Warn a member")]
    [RequireUserPermission(GuildPermission.KickMembers)]
    [RequireBotPermission(GuildPermission.KickMembers)]
    public async Task WarnAsync(
        [Summary(description: "User to warn")] SocketGuildUser user,
        [Summary(description: "Reason for warning")] string reason = "No reason provided")
    {
        if (user == null)
        {
            await RespondAsync("❌ User not found.", ephemeral: true);
            return;
        }

        await _logService.LogActionAsync(ModerationLogEntry.Create(
            ModerationActionType.Warn, user, Context.User, reason));

        var result = await _warningService.AddWarningAsync(
            Context.Guild.Id, user.Id, reason, WarningSource.Manual, Context.User.Id, user);

        if (result.Kicked)
        {
            await RespondAsync($"⚠️ {user.Username} has been kicked for having {result.WarningCount} warnings!");
        }
        else if (result.KickFailureReason is not null)
        {
            await RespondAsync($"❌ Kick failed: {result.KickFailureReason}", ephemeral: true);
        }
        else
        {
            await RespondAsync($"⚠️ {user.Username} has been warned ({result.WarningCount}/3). Reason: {reason}");
        }
    }
}
