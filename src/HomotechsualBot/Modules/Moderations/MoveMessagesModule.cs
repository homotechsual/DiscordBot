using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Discord.Webhook;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;

public class MoveMessagesModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxMoveCount = 25;

    private readonly ModerationLogService _logService;

    public MoveMessagesModule(ModerationLogService logService)
    {
        _logService = logService;
    }

    [SlashCommand("move-messages", "Move recent messages from the current channel to another channel")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [RequireBotPermission(GuildPermission.ManageMessages | GuildPermission.ManageWebhooks)]
    public async Task MoveMessagesAsync(
        [Summary("destination", "Channel to move messages into")] SocketTextChannel destination,
        [Summary("amount", "Number of recent messages to move")]
        [MinValue(1)]
        [MaxValue(MaxMoveCount)]
        int amount = 10)
    {
        await DeferAsync(ephemeral: true);

        if (Context.Channel is not SocketTextChannel sourceChannel)
        {
            await FollowupAsync("❌ This command can only be used in a text channel.", ephemeral: true);
            return;
        }

        if (destination.Id == sourceChannel.Id)
        {
            await FollowupAsync("❌ Destination channel must be different from the source channel.", ephemeral: true);
            return;
        }

        var botUser = Context.Guild.CurrentUser;
        var sourcePerms = botUser.GetPermissions(sourceChannel);
        var destinationPerms = botUser.GetPermissions(destination);

        if (!sourcePerms.ViewChannel || !sourcePerms.ReadMessageHistory || !sourcePerms.ManageMessages)
        {
            await FollowupAsync($"❌ I need View Channel, Read Message History, and Manage Messages in #{sourceChannel.Name}.", ephemeral: true);
            return;
        }

        if (!destinationPerms.ViewChannel || !destinationPerms.SendMessages || !destinationPerms.ManageWebhooks)
        {
            await FollowupAsync($"❌ I need View Channel, Send Messages, and Manage Webhooks in #{destination.Name}.", ephemeral: true);
            return;
        }

        if (Context.User is IGuildUser invokingUser)
        {
            var sourceInvokingPerms = invokingUser.GetPermissions(sourceChannel);
            if (!sourceInvokingPerms.ManageMessages)
            {
                await FollowupAsync($"❌ You need Manage Messages in #{sourceChannel.Name} to move messages.", ephemeral: true);
                return;
            }

            var destinationInvokingPerms = invokingUser.GetPermissions(destination);
            if (!destinationInvokingPerms.ViewChannel || !destinationInvokingPerms.SendMessages)
            {
                await FollowupAsync($"❌ You need View Channel and Send Messages in #{destination.Name} to move messages there.", ephemeral: true);
                return;
            }
        }

        var messages = await sourceChannel.GetMessagesAsync(amount).FlattenAsync();
        var sourceMessages = messages
            .Where(message => message.Type == MessageType.Default || message.Type == MessageType.Reply)
            .OrderBy(message => message.Timestamp)
            .ToList();

        if (sourceMessages.Count == 0)
        {
            await FollowupAsync("❌ No moveable messages were found.", ephemeral: true);
            return;
        }

        using var webhook = await GetOrCreateWebhookClientAsync(destination);
        var movedCount = 0;
        var failedCount = 0;

        foreach (var message in sourceMessages)
        {
            try
            {
                var movedMessageId = await webhook.SendMessageAsync(
                    BuildMovedContent(message),
                    username: GetDisplayName(message.Author),
                    avatarUrl: GetAvatarUrl(message.Author),
                    allowedMentions: AllowedMentions.None);

                await CopyMessageMetadataAsync(message, destination, movedMessageId);

                await message.DeleteAsync();
                movedCount++;
            }
            catch
            {
                failedCount++;
            }
        }

        if (movedCount > 0)
        {
            await _logService.LogActionAsync(ModerationLogEntry.CreateUnknownTarget(
                ModerationActionType.MoveMessages,
                0,
                Context.User,
                $"Moved {movedCount} messages from <#{sourceChannel.Id}> to <#{destination.Id}>"));
        }

        await FollowupAsync(
            $"✅ Moved {movedCount} message(s) from <#{sourceChannel.Id}> to <#{destination.Id}>." +
            (failedCount > 0 ? $" ⚠️ Failed to move {failedCount} message(s)." : string.Empty),
            ephemeral: true);
    }

    [SlashCommand("move-thread", "Move the current thread into a forum channel")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [RequireBotPermission(GuildPermission.ManageMessages | GuildPermission.ManageWebhooks | GuildPermission.CreatePublicThreads | GuildPermission.SendMessagesInThreads)]
    public async Task MoveThreadAsync(
        [Summary("destination", "Forum channel to move the thread into")]
        [ChannelTypes(ChannelType.Forum)]
        IForumChannel destination)
    {
        await DeferAsync(ephemeral: true);

        if (Context.Channel is not IThreadChannel sourceThread)
        {
            await FollowupAsync("❌ This command can only be used inside a thread.", ephemeral: true);
            return;
        }

        var botUser = Context.Guild.CurrentUser;
        var sourcePerms = botUser.GetPermissions(sourceThread);
        var destinationPerms = botUser.GetPermissions(destination);

        if (!sourcePerms.ViewChannel || !sourcePerms.ReadMessageHistory || !sourcePerms.ManageMessages)
        {
            await FollowupAsync($"❌ I need View Channel, Read Message History, and Manage Messages in #{sourceThread.Name}.", ephemeral: true);
            return;
        }

        if (!destinationPerms.ViewChannel || !destinationPerms.ManageWebhooks || !destinationPerms.CreatePublicThreads || !destinationPerms.SendMessagesInThreads)
        {
            await FollowupAsync($"❌ I need View Channel, Create Public Threads, Send Messages in Threads, and Manage Webhooks in #{destination.Name}.", ephemeral: true);
            return;
        }

        if (Context.User is IGuildUser invokingUser)
        {
            var sourceInvokingPerms = invokingUser.GetPermissions(sourceThread);
            if (!sourceInvokingPerms.ManageMessages)
            {
                await FollowupAsync($"❌ You need Manage Messages in #{sourceThread.Name} to move threads.", ephemeral: true);
                return;
            }

            var destinationInvokingPerms = invokingUser.GetPermissions(destination);
            if (!destinationInvokingPerms.ViewChannel || !destinationInvokingPerms.CreatePublicThreads || !destinationInvokingPerms.SendMessagesInThreads)
            {
                await FollowupAsync($"❌ You need View Channel, Create Public Threads, and Send Messages in Threads in #{destination.Name} to move threads there.", ephemeral: true);
                return;
            }
        }

        var sourceMessages = await FetchThreadMessagesAsync(sourceThread);
        if (sourceMessages.Count == 0)
        {
            await FollowupAsync("❌ No moveable messages were found in the thread.", ephemeral: true);
            return;
        }

        var starterNote = $"Moved from <#{sourceThread.Id}> by <@{Context.User.Id}>";
        var movedThread = await destination.CreatePostAsync(
            sourceThread.Name,
            text: starterNote,
            archiveDuration: destination.DefaultAutoArchiveDuration);

        using var webhook = await GetOrCreateWebhookClientAsync(destination);
        var movedCount = 0;
        var failedCount = 0;

        foreach (var message in sourceMessages)
        {
            try
            {
                var movedMessageId = await webhook.SendMessageAsync(
                    BuildMovedContent(message),
                    username: GetDisplayName(message.Author),
                    avatarUrl: GetAvatarUrl(message.Author),
                    allowedMentions: AllowedMentions.None,
                    threadId: movedThread.Id);

                await CopyMessageMetadataAsync(message, movedThread, movedMessageId);

                await message.DeleteAsync();
                movedCount++;
            }
            catch
            {
                failedCount++;
            }
        }

        if (movedCount > 0)
        {
            await _logService.LogActionAsync(ModerationLogEntry.CreateUnknownTarget(
                ModerationActionType.MoveThread,
                0,
                Context.User,
                $"Moved thread <#{sourceThread.Id}> into <#{destination.Id}> as <#{movedThread.Id}>"));
        }

        await FollowupAsync(
            $"✅ Moved thread <#{sourceThread.Id}> into <#{destination.Id}> as <#{movedThread.Id}>." +
            (failedCount > 0 ? $" ⚠️ Failed to move {failedCount} message(s)." : string.Empty),
            ephemeral: true);
    }

    private async Task<DiscordWebhookClient> GetOrCreateWebhookClientAsync(IIntegrationChannel destination)
    {
        var existingWebhook = (await destination.GetWebhooksAsync())
            .FirstOrDefault(webhook => string.Equals(webhook.Name, "HomotechsualBot Message Move", StringComparison.OrdinalIgnoreCase));

        if (existingWebhook is not null)
        {
            return new DiscordWebhookClient(existingWebhook);
        }

        return new DiscordWebhookClient(await destination.CreateWebhookAsync("HomotechsualBot Message Move"));
    }

    private static async Task<List<IMessage>> FetchThreadMessagesAsync(IThreadChannel thread)
    {
        var collectedMessages = new List<IMessage>();
        IMessage? beforeMessage = null;

        while (true)
        {
            var batch = beforeMessage is null
                ? await thread.GetMessagesAsync(100).FlattenAsync()
                : await thread.GetMessagesAsync(beforeMessage, Direction.Before, 100, CacheMode.AllowDownload).FlattenAsync();

            var currentBatch = batch
                .Where(message => message.Type == MessageType.Default || message.Type == MessageType.Reply)
                .ToList();

            if (currentBatch.Count == 0)
            {
                break;
            }

            collectedMessages.AddRange(currentBatch);

            if (currentBatch.Count < 100)
            {
                break;
            }

            beforeMessage = currentBatch.Last();
        }

        return collectedMessages
            .DistinctBy(message => message.Id)
            .OrderBy(message => message.Timestamp)
            .ToList();
    }

    private static string GetDisplayName(IUser user)
        => user is SocketGuildUser guildUser && !string.IsNullOrWhiteSpace(guildUser.DisplayName)
            ? guildUser.DisplayName
            : user.Username;

    private static string? GetAvatarUrl(IUser user)
        => user.GetAvatarUrl(size: 128) ?? user.GetDefaultAvatarUrl();

    private static async Task CopyMessageMetadataAsync(IMessage sourceMessage, IMessageChannel destinationChannel, ulong destinationMessageId)
    {
        try
        {
            var destinationMessage = await destinationChannel.GetMessageAsync(destinationMessageId);
            if (destinationMessage is not IUserMessage userMessage)
            {
                return;
            }

            await CopyReactionsAsync(sourceMessage, userMessage);

            if (sourceMessage.IsPinned)
            {
                await userMessage.PinAsync();
            }
        }
        catch
        {
            // Metadata replay is best-effort; message movement should still complete if a copy step fails.
        }
    }

    private static Task<int> CopyReactionsAsync(IMessage sourceMessage, IUserMessage destinationMessage)
        => ApplyReactionsAsync(emote => destinationMessage.AddReactionAsync(emote), sourceMessage.Reactions.Keys);

    internal static async Task<int> ApplyReactionsAsync(Func<IEmote, Task> addReactionAsync, IEnumerable<IEmote> reactionEmotes)
    {
        var appliedCount = 0;

        foreach (var reactionEmote in reactionEmotes)
        {
            try
            {
                await addReactionAsync(reactionEmote);
                appliedCount++;
            }
            catch
            {
                // Keep replaying remaining reactions when one fails.
            }
        }

        return appliedCount;
    }

    private static string BuildMovedContent(IMessage message)
    {
        var content = string.IsNullOrWhiteSpace(message.Content)
            ? "*(no text)*"
            : message.Content;

        if (message.IsPinned)
        {
            content = $"[Pinned message]{Environment.NewLine}{content}";
        }

        if (message.Attachments.Count > 0)
        {
            var attachmentLines = string.Join(Environment.NewLine, message.Attachments.Select(attachment => attachment.Url));
            content = $"{content}{Environment.NewLine}{attachmentLines}";
        }

        if (content.Length <= 1900)
        {
            return content;
        }

        return content[..1900] + "\n…";
    }
}
