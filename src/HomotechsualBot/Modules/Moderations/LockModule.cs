using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;
public class LockModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public LockModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("lock", "Lock this channel")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    [RequireBotPermission(GuildPermission.ManageChannels)]
    public async Task LockAsync()
    {
        var channel = (SocketTextChannel)Context.Channel;
        var everyone = channel.Guild.EveryoneRole;

        await channel.AddPermissionOverwriteAsync(everyone, new OverwritePermissions(sendMessages: PermValue.Deny));
        await _logService.LogActionAsync(new ModerationLogEntry(
            ModerationActionType.LockChannel, null, 0, Context.User,
            $"<#{channel.Id}> locked", DateTimeOffset.UtcNow));
        await RespondAsync("Channel has been locked 🔒");
    }

    [SlashCommand("unlock", "Unlock this channel")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    [RequireBotPermission(GuildPermission.ManageChannels)]
    public async Task UnlockAsync()
    {
        var channel = (SocketTextChannel)Context.Channel;
        var everyone = channel.Guild.EveryoneRole;

        await channel.AddPermissionOverwriteAsync(everyone, new OverwritePermissions(sendMessages: PermValue.Allow));
        await _logService.LogActionAsync(new ModerationLogEntry(
            ModerationActionType.UnlockChannel, null, 0, Context.User,
            $"<#{channel.Id}> unlocked", DateTimeOffset.UtcNow));
        await RespondAsync("Channel has been unlocked 🔓");
    }

}
