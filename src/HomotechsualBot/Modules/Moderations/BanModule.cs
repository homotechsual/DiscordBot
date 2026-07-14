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
        [Summary("user", "Member to ban (if still in server)")] SocketGuildUser? user = null,
        [Summary("user_id", "User ID to ban (works even if user already left)")] string? userId = null,
        string? reason = null,
        [Summary("purge_messages", "Delete user's recent messages")] bool purgeMessages = false,
        [Summary("purge_amount", "Number of messages to delete per channel (if purging)")] int purgeAmount = 10,
        [Summary("purge_all_channels", "Purge from all channels (true) or just current (false)")] bool purgeAllChannels = false)
    {
        reason ??= "There is no reason provided.";

        var guild = Context.Guild;
        var botUser = guild.CurrentUser;
        ulong targetUserId;

        if (user is null && string.IsNullOrWhiteSpace(userId))
        {
            await RespondAsync("❌ Provide either a user or a user ID.", ephemeral: true);
            return;
        }

        if (user is not null)
        {
            targetUserId = user.Id;
        }
        else if (!TryParseUserId(userId!, out targetUserId))
        {
            await RespondAsync("❌ Invalid user ID. Use a numeric Discord user ID.", ephemeral: true);
            return;
        }

        // Check: don't allow self-ban
        if (targetUserId == Context.User.Id)
        {
            await RespondAsync("❌ You cannot ban yourself!", ephemeral: true);
            return;
        }

        // Check role position only when we have a live guild member object.
        if (user is not null && botUser.Hierarchy <= user.Hierarchy)
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
                purgeResult = await PurgeUser.PurgeUserMessagesAsync(Context, targetUserId, purgeAmount, purgeAllChannels);
            }

            // Then ban the user
            await guild.AddBanAsync(targetUserId, pruneDays: 0, reason: reason);

            if (user is not null)
            {
                await _logService.LogActionAsync(ModerationLogEntry.Create(
                    ModerationActionType.Ban, user, Context.User, reason));
            }
            else
            {
                await _logService.LogActionAsync(ModerationLogEntry.CreateUnknownTarget(
                    ModerationActionType.Ban, targetUserId, Context.User, reason));
            }

            var targetDisplay = user is not null ? user.Username : $"ID {targetUserId}";
            var response = $"✅ Banned **{targetDisplay}** (Reason: {reason})";
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

    private static bool TryParseUserId(string rawUserId, out ulong userId)
    {
        var trimmed = rawUserId.Trim();

        // Support plain IDs and mention formats like <@123> or <@!123>.
        if (trimmed.StartsWith("<@") && trimmed.EndsWith(">"))
        {
            trimmed = trimmed.TrimStart('<', '@', '!').TrimEnd('>');
        }

        return ulong.TryParse(trimmed, out userId);
    }
}
