using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;
using System;
using System.Threading.Tasks;

namespace DiscordBot.Modules.Moderations;

public class MuteModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public MuteModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("mute", "Mute (timeout) a member for a certain period of time (minutes)")]
    [RequireUserPermission(GuildPermission.ModerateMembers)]
    [RequireBotPermission(GuildPermission.ModerateMembers)]
    public async Task MuteAsync(
        [Summary(description: "User to mute")] SocketGuildUser user,
        [Summary(description: "Mute duration (minutes)")] int minutes,
        [Summary(description: "Reason (optional)")] string? reason = null)
    {
        if (user.Id == Context.User.Id)
        {
            await RespondAsync("❌ You cannot mute yourself!", ephemeral: true);
            return;
        }

        if (minutes <= 0)
        {
            await RespondAsync("❌ Minutes must be greater than 0.", ephemeral: true);
            return;
        }

        var bot = Context.Guild.CurrentUser;
        if (bot.Hierarchy <= user.Hierarchy)
        {
            await RespondAsync("❌ Cannot mute someone with a role higher than or equal to the bot.", ephemeral: true);
            return;
        }

        try
        {
            await user.SetTimeOutAsync(TimeSpan.FromMinutes(minutes), new RequestOptions
            {
                AuditLogReason = reason ?? "No reason provided"
            });
            await _logService.LogActionAsync(ModerationLogEntry.Create(
                ModerationActionType.Mute, user, Context.User, reason ?? $"{minutes} minute(s)"));

            await RespondAsync(
                $"✅ Successfully muted {user.Username} for {minutes} minutes. (Reason: {reason ?? "No reason provided"})"
            );
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ Mute failed: {ex.Message}", ephemeral: true);
        }
    }
}
