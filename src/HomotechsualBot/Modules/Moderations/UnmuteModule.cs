using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;

public class UnmuteModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public UnmuteModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("unmute", "Remove mute (timeout) from a member")]
    [RequireUserPermission(GuildPermission.ModerateMembers)]
    [RequireBotPermission(GuildPermission.ModerateMembers)]
    public async Task UnmuteAsync(
        [Summary(description: "User to unmute")] SocketGuildUser user)
    {
        if (user == null)
        {
            await RespondAsync("❌ User not found in server.", ephemeral: true);
            return;
        }

        try
        {
            await user.RemoveTimeOutAsync();
            await _logService.LogActionAsync(ModerationLogEntry.Create(
                ModerationActionType.Unmute, user, Context.User));
            await RespondAsync($"✅ Successfully unmuted **{user.Username}**.");
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ Unmute failed: {ex.Message}", ephemeral: true);
        }
    }
}
