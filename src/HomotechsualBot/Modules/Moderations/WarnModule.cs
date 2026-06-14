using System;
using System.Collections.Generic;
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

    public WarnModule(ModerationLogService logService)
    {
        _logService = logService;
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

        // Save warning
        if (!WarnStorage.Warnings.ContainsKey(user.Id))
            WarnStorage.Warnings[user.Id] = new List<string>();

        WarnStorage.Warnings[user.Id].Add(reason);
        await _logService.LogActionAsync(ModerationLogEntry.Create(
            ModerationActionType.Warn, user, Context.User, reason));

        var warnCount = WarnStorage.Warnings[user.Id].Count;

        // Check warning count
        if (warnCount >= 3)
        {
            try
            {
                await user.KickAsync($"Over 3 warnings (total: {warnCount})");
                await RespondAsync($"⚠️ {user.Username} has been kicked for having {warnCount} warnings!");
            }
            catch (Exception ex)
            {
                await RespondAsync($"❌ Kick failed: {ex.Message}", ephemeral: true);
            }
        }
        else
        {
            await RespondAsync($"⚠️ {user.Username} has been warned ({warnCount}/3). Reason: {reason}");
        }
    }
}
