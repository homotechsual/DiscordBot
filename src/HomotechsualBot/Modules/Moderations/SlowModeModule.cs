using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;
public class SlowModeModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public SlowModeModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("slowmode", "Set slowmode for channel")]
    [RequireUserPermission(Discord.ChannelPermission.ManageChannels)]
    [RequireBotPermission(Discord.ChannelPermission.ManageChannels)]
    public async Task SlowmodeAsync(
    [Summary("seconds", "Slowmode duration (seconds)")] int seconds)
    {
        var channel = (SocketTextChannel)Context.Channel;

        await channel.ModifyAsync(prop => prop.SlowModeInterval = seconds);
        var actionType = seconds > 0 ? ModerationActionType.SlowModeSet : ModerationActionType.SlowModeCleared;
        var reason = seconds > 0 ? $"{seconds}s slowmode in <#{channel.Id}>" : $"Slowmode cleared in <#{channel.Id}>";
        await _logService.LogActionAsync(new ModerationLogEntry(
            actionType, null, 0, Context.User, reason, DateTimeOffset.UtcNow));
        await RespondAsync($"Slowmode has been set to {seconds} seconds!");
    }

}
