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
public class PurgeUser : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ModerationLogService _logService;

    public PurgeUser(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("purge_user", "Delete messages from a member")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    public async Task PurgeUserAsync(
    [Summary("user", "User whose messages to delete (mention or ID)")] string userInput,
    [Summary("amount", "Number of messages to delete per channel")] int amount = 10,
    [Summary("all_channels", "Delete from all channels (true) or just current channel (false)")] bool allChannels = false)
    {
        if (!((SocketGuildUser)Context.User).GuildPermissions.ManageMessages)
        {
            await RespondAsync("You don't have permission to delete messages!", ephemeral: true);
            return;
        }

        // Parse user ID from input (handles both mentions and raw IDs)
        ulong userId;
        if (MentionUtils.TryParseUser(userInput, out userId) || ulong.TryParse(userInput, out userId))
        {
            await DeferAsync(ephemeral: true);
            var result = await PurgeUserMessagesAsync(Context, userId, amount, allChannels);
            await _logService.LogActionAsync(ModerationLogEntry.CreateUnknownTarget(
                ModerationActionType.PurgeUser, userId, Context.User, result));
            await FollowupAsync(result, ephemeral: true);
        }
        else
        {
            await RespondAsync("Invalid user. Please mention a user or provide a valid user ID.", ephemeral: true);
        }
    }

    /// <summary>
    /// Purges messages from a specific user. Can be called from other modules.
    /// </summary>
    /// <param name="context">The interaction context</param>
    /// <param name="userId">The user ID whose messages to delete</param>
    /// <param name="amount">Number of messages to delete per channel</param>
    /// <param name="allChannels">Whether to purge from all channels or just the current one</param>
    /// <returns>A result message indicating how many messages were deleted</returns>
    public static async Task<string> PurgeUserMessagesAsync(SocketInteractionContext context, ulong userId, int amount, bool allChannels)
    {
        int totalDeleted = 0;
        var channelsToProcess = new List<SocketTextChannel>();

        if (allChannels)
        {
            var guild = context.Guild;
            channelsToProcess.AddRange(guild.TextChannels.Where(c => 
                c.GetUser(context.Client.CurrentUser.Id)?.GetPermissions(c).ManageMessages ?? false));
        }
        else
        {
            channelsToProcess.Add((SocketTextChannel)context.Channel);
        }

        foreach (var channel in channelsToProcess)
        {
            try
            {
                var messages = await channel.GetMessagesAsync(100).FlattenAsync();
                var userMessages = messages
                    .Where(m => m.Author.Id == userId)
                    .Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14) // Discord bulk delete limitation
                    .Take(amount)
                    .ToList();

                if (userMessages.Any())
                {
                    if (userMessages.Count == 1)
                    {
                        // Single message deletion
                        await userMessages[0].DeleteAsync();
                    }
                    else
                    {
                        // Bulk deletion (only works for messages less than 14 days old)
                        await channel.DeleteMessagesAsync(userMessages);
                    }
                    totalDeleted += userMessages.Count;
                }
            }
            catch (Exception ex)
            {
                // Log the error and continue
                Console.WriteLine($"Error deleting messages in {channel.Name}: {ex.Message}");
                continue;
            }
        }

        if (totalDeleted == 0)
        {
            return $"No messages found from that user in {(allChannels ? "any accessible channels" : "this channel")}. Note: Messages older than 14 days cannot be bulk deleted.";
        }
        else
        {
            return $"Deleted {totalDeleted} messages from <@{userId}> {(allChannels ? "across all channels" : "in this channel")}.";
        }
    }

}
