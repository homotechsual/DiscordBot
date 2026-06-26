using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace DiscordBot.Services;

public class EventAuditLogService
{
    private const int SnapshotRetentionMinutes = 120;
    private const int MaxSnapshots = 5000;

    private readonly DiscordSocketClient _client;
    private readonly ModerationLogConfig _config;
    private readonly ILogger<EventAuditLogService> _logger;
    private readonly ConcurrentDictionary<ulong, MessageSnapshot> _recentMessages = new();

    public EventAuditLogService(
        DiscordSocketClient client,
        ModerationLogConfig config,
        ILogger<EventAuditLogService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public Task HandleMessageReceivedAsync(SocketMessage message)
    {
        if (!_config.EventAuditEnabled || !_config.LogMessageDeletes)
        {
            return Task.CompletedTask;
        }

        if (message.Author.IsBot)
        {
            return Task.CompletedTask;
        }

        _recentMessages[message.Id] = new MessageSnapshot(
            message.Author.Id,
            message.Content,
            message.Channel.Id,
            GetAuthorDisplay(message.Author),
            GetAuthorAvatarUrl(message.Author),
            DateTimeOffset.UtcNow);

        if (_recentMessages.Count > MaxSnapshots)
        {
            PruneSnapshots();
        }

        return Task.CompletedTask;
    }

    public async Task HandleMessageDeletedAsync(
        Cacheable<IMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        if (!_config.EventAuditEnabled || !_config.LogMessageDeletes || _config.EventAuditChannelId == 0)
        {
            return;
        }

        try
        {
            var channel = await cachedChannel.GetOrDownloadAsync();
            if (channel is not ITextChannel textChannel)
            {
                return;
            }

            var auditChannel = await ResolveAuditChannelAsync();
            if (auditChannel is null)
            {
                return;
            }

            var message = await cachedMessage.GetOrDownloadAsync();
            if (message?.Author.IsBot == true) return;

            var authorId = message?.Author.Id;
            var content = message?.Content;
            var authorDisplay = message?.Author is null ? null : GetAuthorDisplay(message.Author);
            var authorAvatarUrl = message?.Author is null ? null : GetAuthorAvatarUrl(message.Author);

            if ((!authorId.HasValue || string.IsNullOrWhiteSpace(content)) &&
                TryGetRecentMessageSnapshot(cachedMessage.Id, out var snapshot))
            {
                authorId ??= snapshot.AuthorId;
                if (string.IsNullOrWhiteSpace(content))
                {
                    content = snapshot.Content;
                }

                if (string.IsNullOrWhiteSpace(authorDisplay))
                {
                    authorDisplay = snapshot.AuthorDisplay;
                }

                if (string.IsNullOrWhiteSpace(authorAvatarUrl))
                {
                    authorAvatarUrl = snapshot.AuthorAvatarUrl;
                }
            }

            await Task.Delay(1500);

            var actor = await FindRecentAuditActorAsync(
                textChannel.Guild,
                actionKeywords: ["MessageDeleted"],
                targetUserId: authorId,
                channelId: textChannel.Id,
                messageId: cachedMessage.Id);

            var resolvedAuthorId = authorId ?? actor?.TargetUserId;

            if (resolvedAuthorId.HasValue && IsIgnoredUser(resolvedAuthorId.Value))
            {
                return;
            }

            if (resolvedAuthorId.HasValue && (string.IsNullOrWhiteSpace(authorDisplay) || string.IsNullOrWhiteSpace(authorAvatarUrl)))
            {
                var resolvedUser = await textChannel.Guild.GetUserAsync(resolvedAuthorId.Value, CacheMode.AllowDownload);
                if (resolvedUser is not null)
                {
                    if (resolvedUser.IsBot) return;
                    authorDisplay ??= GetAuthorDisplay(resolvedUser);
                    authorAvatarUrl ??= GetAuthorAvatarUrl(resolvedUser);
                }
            }

            authorDisplay ??= resolvedAuthorId.HasValue
                ? $"<@{resolvedAuthorId.Value}> ({resolvedAuthorId.Value})"
                : "Unknown";

            authorAvatarUrl ??= resolvedAuthorId.HasValue
                ? GetDefaultAvatarUrl(resolvedAuthorId.Value)
                : GetDefaultAvatarUrl(0);

            var authorMention = resolvedAuthorId.HasValue
                ? $"<@{resolvedAuthorId.Value}>"
                : "Unknown";

            var authorFieldValue = resolvedAuthorId.HasValue
                ? $"{authorMention} ({resolvedAuthorId.Value})"
                : authorDisplay;

            if (!string.IsNullOrWhiteSpace(content) && content.Length > 1024)
            {
                content = content[..1021] + "...";
            }

            var embed = new EmbedBuilder()
                .WithTitle("🗑️ Message Deleted")
                .WithColor(new Color(0xE67E22))
                .WithAuthor(authorDisplay, iconUrl: authorAvatarUrl)
                .AddField("Channel", $"<#{textChannel.Id}>", inline: true)
                .AddField("Author", authorFieldValue, inline: true)
                .AddField("Deleted By", actor?.ActorDisplay ?? "Unknown / self-delete", inline: false)
                .AddField("Message ID", cachedMessage.Id, inline: true)
                .WithThumbnailUrl(authorAvatarUrl)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (!string.IsNullOrWhiteSpace(content))
            {
                embed.AddField("Content", content);
            }

            if (!string.IsNullOrWhiteSpace(actor?.Reason))
            {
                embed.AddField("Reason", actor.Reason);
            }

            await auditChannel.SendMessageAsync(embed: embed.Build());
            _recentMessages.TryRemove(cachedMessage.Id, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventAudit: failed to log a message deletion event");
        }
    }

    public async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (!_config.EventAuditEnabled || !_config.LogMemberLeaves || _config.EventAuditChannelId == 0)
        {
            return;
        }

        if (IsIgnoredUser(user.Id))
        {
            return;
        }

        try
        {
            var auditChannel = await ResolveAuditChannelAsync();
            if (auditChannel is null || auditChannel.GuildId != guild.Id)
            {
                return;
            }

            await Task.Delay(1500);

            var actor = await FindRecentAuditActorAsync(
                guild,
                actionKeywords: ["Kick", "Ban"],
                targetUserId: user.Id,
                channelId: null);

            var title = actor is null ? "👋 Member Left" : "🚪 Member Removed";
            var action = actor is null ? "Voluntary leave (or no matching audit log entry)" : actor.Action;
            var authorDisplay = GetAuthorDisplay(user);
            var avatarUrl = GetAuthorAvatarUrl(user) ?? GetDefaultAvatarUrl(user.Id);

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(actor is null ? new Color(0x95A5A6) : new Color(0xE74C3C))
                .WithAuthor(authorDisplay, iconUrl: avatarUrl)
                .WithThumbnailUrl(avatarUrl)
                .AddField("Member", $"{authorDisplay} ({user.Id})", inline: true)
                .AddField("Action", action, inline: true)
                .AddField("Actor", actor?.ActorDisplay ?? "Unknown", inline: true)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (!string.IsNullOrWhiteSpace(actor?.Reason))
            {
                embed.AddField("Reason", actor.Reason);
            }

            await auditChannel.SendMessageAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventAudit: failed to log a member leave event for user {UserId}", user.Id);
        }
    }

    public async Task HandleUserJoinedAsync(SocketGuildUser user)
    {
        if (!_config.EventAuditEnabled || !_config.LogMemberJoins || _config.EventAuditChannelId == 0)
        {
            return;
        }

        if (IsIgnoredUser(user.Id))
        {
            return;
        }

        try
        {
            var auditChannel = await ResolveAuditChannelAsync();
            if (auditChannel is null || auditChannel.GuildId != user.Guild.Id)
            {
                return;
            }

            var authorDisplay = GetAuthorDisplay(user);
            var avatarUrl = GetAuthorAvatarUrl(user) ?? GetDefaultAvatarUrl(user.Id);

            var embed = new EmbedBuilder()
                .WithTitle("📥 Member Joined")
                .WithColor(new Color(0x2ECC71))
                .WithAuthor(authorDisplay, iconUrl: avatarUrl)
                .WithThumbnailUrl(avatarUrl)
                .AddField("Member", $"{authorDisplay} ({user.Id})", inline: true)
                .WithTimestamp(DateTimeOffset.UtcNow);

            await auditChannel.SendMessageAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventAudit: failed to log a member join event for user {UserId}", user.Id);
        }
    }

    private async Task<ITextChannel?> ResolveAuditChannelAsync()
    {
        if (_client.GetChannel(_config.EventAuditChannelId) is ITextChannel cachedTextChannel)
        {
            return cachedTextChannel;
        }

        try
        {
            var restChannel = await _client.Rest.GetChannelAsync(_config.EventAuditChannelId);
            if (restChannel is ITextChannel textChannel)
            {
                return textChannel;
            }

            _logger.LogWarning(
                "EventAudit: configured channel {ChannelId} is not a standard text channel",
                _config.EventAuditChannelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventAudit: failed to resolve channel {ChannelId}", _config.EventAuditChannelId);
        }

        return null;
    }

    private async Task<AuditActorInfo?> FindRecentAuditActorAsync(
        IGuild guild,
        IReadOnlyList<string> actionKeywords,
        ulong? targetUserId,
        ulong? channelId,
        ulong? messageId = null)
    {
        try
        {
            var lookbackCutoff = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(5, _config.AuditLogLookbackSeconds));
            var entries = await guild.GetAuditLogsAsync(25);

            foreach (var entry in entries)
            {
                var actionText = GetActionText(entry);
                if (string.IsNullOrWhiteSpace(actionText) ||
                    !actionKeywords.Any(k => actionText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var createdAt = GetCreatedAt(entry);
                if (createdAt < lookbackCutoff)
                {
                    continue;
                }

                if (targetUserId.HasValue && TryGetTargetUserId(entry, out var targetId) && targetId != targetUserId.Value)
                {
                    continue;
                }

                if (channelId.HasValue && TryGetChannelId(entry, out var loggedChannelId) && loggedChannelId != channelId.Value)
                {
                    continue;
                }

                if (messageId.HasValue && TryGetMessageId(entry, out var loggedMessageId) && loggedMessageId != messageId.Value)
                {
                    continue;
                }

                var actorDisplay = TryGetActorDisplay(entry, out var actor)
                    ? actor
                    : "Unknown";

                ulong? matchedTargetUserId = TryGetTargetUserId(entry, out var matchedTargetId)
                    ? matchedTargetId
                    : null;

                return new AuditActorInfo(actionText, actorDisplay, GetReason(entry), matchedTargetUserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EventAudit: failed to query audit logs in guild {GuildId}", guild.Id);
        }

        return null;
    }

    private static string? GetActionText(object entry)
        => entry.GetType().GetProperty("Action", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry)
            ?.ToString();

    private static DateTimeOffset GetCreatedAt(object entry)
    {
        var createdAtValue = entry.GetType().GetProperty("CreatedAt", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry);

        return createdAtValue switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            _ => DateTimeOffset.MinValue
        };
    }

    private static string? GetReason(object entry)
        => entry.GetType().GetProperty("Reason", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry)
            ?.ToString();

    private static bool TryGetActorDisplay(object entry, out string actorDisplay)
    {
        actorDisplay = string.Empty;

        var userValue = entry.GetType().GetProperty("User", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry);

        if (userValue is IUser user)
        {
            actorDisplay = $"<@{user.Id}> ({user.Id})";
            return true;
        }

        return false;
    }

    private static bool TryGetTargetUserId(object entry, out ulong targetUserId)
    {
        targetUserId = 0;

        var directTargetId = entry.GetType().GetProperty("TargetId", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry);

        if (TryConvertToUlong(directTargetId, out targetUserId))
        {
            return true;
        }

        var targetValue = entry.GetType().GetProperty("Target", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry);

        if (TryGetIdProperty(targetValue, out targetUserId))
        {
            return true;
        }

        var dataValue = entry.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry);

        if (dataValue is null)
        {
            return false;
        }

        var dataTarget = dataValue.GetType().GetProperty("Target", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(dataValue);

        return TryGetIdProperty(dataTarget, out targetUserId);
    }

    private static bool TryGetChannelId(object entry, out ulong channelId)
    {
        channelId = 0;

        var dataValue = entry.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry);

        if (dataValue is null)
        {
            return false;
        }

        var channelValue = dataValue.GetType().GetProperty("ChannelId", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(dataValue);

        return TryConvertToUlong(channelValue, out channelId);
    }

    private static bool TryGetMessageId(object entry, out ulong messageId)
    {
        messageId = 0;

        var dataValue = entry.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(entry);

        if (dataValue is null)
        {
            return false;
        }

        var messageValue = dataValue.GetType().GetProperty("MessageId", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(dataValue);

        return TryConvertToUlong(messageValue, out messageId);
    }

    private static bool TryGetIdProperty(object? value, out ulong id)
    {
        id = 0;

        if (value is null)
        {
            return false;
        }

        var idValue = value.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(value);

        return TryConvertToUlong(idValue, out id);
    }

    private static bool TryConvertToUlong(object? value, out ulong result)
    {
        result = 0;

        switch (value)
        {
            case ulong ulongValue:
                result = ulongValue;
                return true;
            case long longValue when longValue >= 0:
                result = (ulong)longValue;
                return true;
            case int intValue when intValue >= 0:
                result = (ulong)intValue;
                return true;
            default:
                return false;
        }
    }

    private static string GetAuthorDisplay(IUser user)
    {
        if (user is SocketGuildUser guildUser && !string.IsNullOrWhiteSpace(guildUser.DisplayName))
        {
            return $"{guildUser.DisplayName} (@{user.Username})";
        }

        return user.Username;
    }

    private static string? GetAuthorAvatarUrl(IUser user)
        => user.GetAvatarUrl(size: 128) ?? user.GetDefaultAvatarUrl();

    private static string GetDefaultAvatarUrl(ulong userId)
    {
        var avatarIndex = (int)((userId >> 22) % 6);
        return $"https://cdn.discordapp.com/embed/avatars/{avatarIndex}.png";
    }

    private bool IsIgnoredUser(ulong userId)
        => _config.IgnoredUserIds.Contains(userId);

    private bool TryGetRecentMessageSnapshot(ulong messageId, out MessageSnapshot snapshot)
    {
        snapshot = default;

        if (!_recentMessages.TryGetValue(messageId, out var stored))
        {
            return false;
        }

        if (stored.CapturedAt < DateTimeOffset.UtcNow.AddMinutes(-SnapshotRetentionMinutes))
        {
            _recentMessages.TryRemove(messageId, out _);
            return false;
        }

        snapshot = stored;
        return true;
    }

    private void PruneSnapshots()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-SnapshotRetentionMinutes);
        foreach (var kvp in _recentMessages)
        {
            if (kvp.Value.CapturedAt < cutoff)
            {
                _recentMessages.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed record AuditActorInfo(string Action, string ActorDisplay, string? Reason, ulong? TargetUserId);
    private readonly record struct MessageSnapshot(
        ulong AuthorId,
        string? Content,
        ulong ChannelId,
        string AuthorDisplay,
        string? AuthorAvatarUrl,
        DateTimeOffset CapturedAt);
}
