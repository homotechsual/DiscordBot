using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class ModerationLogService
{
    private readonly DiscordSocketClient? _client;
    private readonly ModerationLogConfig _config;
    private readonly ILogger<ModerationLogService> _logger;

    public ModerationLogService(
        DiscordSocketClient? client,
        ModerationLogConfig config,
        ILogger<ModerationLogService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task LogSpamDetectedAsync(
        IUser user,
        IReadOnlyList<ITextChannel> channels,
        string fingerprint,
        IReadOnlyList<(ulong ChannelId, ulong MessageId)> deletedMessages,
        string? imageUrl = null)
    {
        if (_config.ForumChannelId == 0) return;

        try
        {
            var forum = await ResolveForumChannelAsync();
            if (forum is null)
            {
                _logger.LogWarning("ModerationLog: channel {Id} is not a forum channel or not cached", _config.ForumChannelId);
                return;
            }

            var threadTitle = user is not null
                ? $"[{user.Id}] {user.Username}"
                : $"Unknown User - {ModerationActionType.SpamDetected} (ID: 0)";

            var embed = BuildSpamEmbed(user, channels, fingerprint, imageUrl);
            var components = BuildSpamButtons(user?.Id ?? 0, forum.Guild.Id);

            await forum.CreatePostAsync(threadTitle, embed: embed, components: components);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModerationLog: failed to create spam detection thread");
        }
    }

    public async Task LogActionAsync(ModerationLogEntry entry)
    {
        if (_config.ForumChannelId == 0) return;

        try
        {
            var forum = await ResolveForumChannelAsync();
            if (forum is null)
            {
                _logger.LogWarning("ModerationLog: channel {Id} is not a forum channel or not cached", _config.ForumChannelId);
                return;
            }

            var threadTitle = BuildThreadTitle(entry);
            var embed = BuildActionEmbed(entry);

            await forum.CreatePostAsync(threadTitle, embed: embed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModerationLog: failed to create forum post for {Action}", entry.ActionType);
        }
    }

    public async Task AppendToThreadAsync(IMessageChannel? thread, Embed embed)
    {
        if (thread is null) return;

        try
        {
            await thread.SendMessageAsync(embed: embed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModerationLog: failed to append to thread {ThreadId}", thread.Id);
        }
    }

    private static string BuildThreadTitle(ModerationLogEntry entry)
    {
        if (entry.Target is not null)
            return $"[{entry.TargetId}] {entry.Target.Username}";

        return $"Unknown User - {entry.ActionType} (ID: {entry.TargetId})";
    }

    private Embed BuildSpamEmbed(
        IUser? user,
        IReadOnlyList<ITextChannel> channels,
        string fingerprint,
        string? imageUrl = null)
    {
        var parts = fingerprint.Split('|', 2);
        var text = parts[0];
        var attachments = parts.Length > 1 ? parts[1] : string.Empty;
        var channelMentions = channels.Count > 0
            ? string.Join(", ", channels.Select(c => $"<#{c.Id}>"))
            : "Unknown";

        var builder = new EmbedBuilder()
            .WithTitle("🚨 Cross-Channel Spam Detected")
            .WithColor(new Color(0xE74C3C))
            .AddField("User", user is not null ? $"<@{user.Id}> ({user.Id})" : "Unknown", inline: true)
            .AddField("Channels", channelMentions, inline: true)
            .AddField("Message", string.IsNullOrWhiteSpace(text) ? "*(no text)*" : text)
            .AddField("Action", "28-day timeout applied")
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (!string.IsNullOrWhiteSpace(attachments))
            builder.AddField("Attachments", attachments);

        if (!string.IsNullOrWhiteSpace(imageUrl))
            builder.WithImageUrl(imageUrl);

        AppendModRoleFooter(builder);
        return builder.Build();
    }

    private Embed BuildActionEmbed(ModerationLogEntry entry)
    {
        var color = entry.ActionType switch
        {
            ModerationActionType.Ban or ModerationActionType.Kick or ModerationActionType.SpamDetected
                => new Color(0xE74C3C),
            ModerationActionType.Mute or ModerationActionType.Warn or ModerationActionType.PurgeUser
                => new Color(0xF1C40F),
            ModerationActionType.Unmute or ModerationActionType.Unban
                => new Color(0xE67E22),
            _ => new Color(0x95A5A6)
        };

        var targetDisplay = entry.Target is not null
            ? $"<@{entry.TargetId}> ({entry.TargetId})"
            : $"Unknown ({entry.TargetId})";

        var moderatorDisplay = entry.Moderator is not null
            ? $"<@{entry.Moderator.Id}>"
            : "Automated";

        var builder = new EmbedBuilder()
            .WithTitle($"🔨 {entry.ActionType}")
            .WithColor(color)
            .AddField("Target", targetDisplay, inline: true)
            .AddField("Moderator", moderatorDisplay, inline: true)
            .WithTimestamp(entry.Timestamp);

        if (!string.IsNullOrWhiteSpace(entry.Reason))
            builder.AddField("Reason", entry.Reason);

        AppendModRoleFooter(builder);
        return builder.Build();
    }

    private void AppendModRoleFooter(EmbedBuilder builder)
    {
        if (_config.ModeratorRoleId != 0)
            builder.WithFooter($"<@&{_config.ModeratorRoleId}>");
    }

    private static MessageComponent BuildSpamButtons(ulong userId, ulong guildId) =>
        new ComponentBuilder()
            .WithButton("🔨 Ban User", $"spam_ban:{userId}:{guildId}", ButtonStyle.Danger)
            .WithButton("✅ Dismiss", $"spam_dismiss:{userId}:{guildId}", ButtonStyle.Secondary)
            .Build();

    private async Task<IForumChannel?> ResolveForumChannelAsync()
    {
        if (_client is null)
            return null;

        if (_client.GetChannel(_config.ForumChannelId) is IForumChannel cachedForum)
            return cachedForum;

        try
        {
            var restChannel = await _client.Rest.GetChannelAsync(_config.ForumChannelId);
            if (restChannel is IForumChannel restForum)
                return restForum;

            _logger.LogWarning("ModerationLog: channel {Id} was resolved via REST but is not a forum channel", _config.ForumChannelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ModerationLog: failed to resolve forum channel {Id} via REST", _config.ForumChannelId);
        }

        return null;
    }
}
