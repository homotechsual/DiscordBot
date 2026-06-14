using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Services;
using System;
using System.Threading.Tasks;

namespace DiscordBot.Modules.Moderations;

public class BanModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public BanModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("ban", "Ban a member from the server")]
    [RequireUserPermission(GuildPermission.BanMembers)]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task BanAsync(
        SocketGuildUser user,
        string? reason = null,
        [Summary("purge_messages", "Delete user's recent messages")] bool purgeMessages = false,
        [Summary("purge_amount", "Number of messages to delete per channel (if purging)")] int purgeAmount = 10,
        [Summary("purge_all_channels", "Purge from all channels (true) or just current (false)")] bool purgeAllChannels = false)
    {
        reason ??= "There is no reason provided.";

        var guild = Context.Guild;
        var botUser = guild.CurrentUser;

        // Check: don't allow self-ban
        if (user.Id == Context.User.Id)
        {
            await RespondAsync("❌ You cannot ban yourself!", ephemeral: true);
            return;
        }

        // Check role position
        if (botUser.Hierarchy <= user.Hierarchy)
        {
            await RespondAsync("❌ Bot cannot ban users with equal or higher roles.", ephemeral: true);
            return;
        }

        try
        {
            // Defer if purging messages (may take time)
            if (purgeMessages)
            {
                await DeferAsync();
            }

            // Purge messages first if requested
            string purgeResult = "";
            if (purgeMessages)
            {
                purgeResult = await PurgeUser.PurgeUserMessagesAsync(Context, user.Id, purgeAmount, purgeAllChannels);
            }

            // Then ban the user
            await guild.AddBanAsync(user, pruneDays: 0, reason: reason);
            await _logService.LogActionAsync(ModerationLogEntry.Create(
                ModerationActionType.Ban, user, Context.User, reason));

            var response = $"✅ Banned **{user.Username}** (Reason: {reason})";
            if (purgeMessages)
            {
                response += $"\n{purgeResult}";
                await FollowupAsync(response);
            }
            else
            {
                await RespondAsync(response);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"❌ Ban failed: {ex.Message}";
            if (purgeMessages)
            {
                await FollowupAsync(errorMsg, ephemeral: true);
            }
            else
            {
                await RespondAsync(errorMsg, ephemeral: true);
            }
        }
    }
}
