using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace DiscordBot.Services;

public class ModerationLogService
{
    private readonly DiscordSocketClient? _client;
    private readonly ModerationLogConfig _config;
    private readonly ILogger<ModerationLogService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ModerationLogService(
        DiscordSocketClient? client,
        ModerationLogConfig config,
        ILogger<ModerationLogService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _client = client;
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task LogSpamDetectedAsync(
        IUser user,
        IReadOnlyList<ITextChannel> channels,
        string fingerprint,
        IReadOnlyList<(ulong ChannelId, ulong MessageId)> deletedMessages,
        byte[]? imageBytes = null,
        string? imageFilename = null)
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

            var embed = BuildSpamEmbed(user, channels, fingerprint, imageFilename);
            var components = BuildSpamButtons(user?.Id ?? 0, forum.Guild.Id);

            var thread = user is not null ? await ResolveThreadAsync(forum, user, user.Id) : null;

            if (thread is not null)
            {
                await PostIntoThreadAsync(thread, embed, components, imageBytes, imageFilename);
                return;
            }

            var threadTitle = user is not null
                ? $"[{user.Id}] {user.Username}"
                : $"Unknown User - {ModerationActionType.SpamDetected} (ID: 0)";

            var created = await CreateForumPostAsync(forum, threadTitle, embed, components, imageBytes, imageFilename);

            if (user is not null)
            {
                await SaveThreadLinkAsync(forum.Guild.Id, user.Id, created.Id, titleConfirmed: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModerationLog: failed to create spam detection thread");
        }
    }

    private static async Task PostIntoThreadAsync(
        IThreadChannel thread, Embed embed, MessageComponent? components, byte[]? imageBytes, string? imageFilename)
    {
        if (imageBytes is not null && imageFilename is not null)
        {
            await using var stream = new MemoryStream(imageBytes, writable: false);
            var attachment = new FileAttachment(stream, imageFilename);
            await thread.SendFileAsync(attachment, embed: embed, components: components);
        }
        else
        {
            await thread.SendMessageAsync(embed: embed, components: components);
        }
    }

    private static async Task<IThreadChannel> CreateForumPostAsync(
        IForumChannel forum, string title, Embed embed, MessageComponent? components, byte[]? imageBytes, string? imageFilename)
    {
        if (imageBytes is not null && imageFilename is not null)
        {
            await using var stream = new MemoryStream(imageBytes, writable: false);
            var attachment = new FileAttachment(stream, imageFilename);
            return await forum.CreatePostWithFilesAsync(title, [attachment], embed: embed, components: components);
        }

        return await forum.CreatePostAsync(title, embed: embed, components: components);
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

            var embed = BuildActionEmbed(entry);

            var thread = entry.TargetId != 0
                ? await ResolveThreadAsync(forum, entry.Target, entry.TargetId)
                : null;

            if (thread is not null)
            {
                await thread.SendMessageAsync(embed: embed);
                return;
            }

            var threadTitle = BuildThreadTitle(entry);
            var created = await forum.CreatePostAsync(threadTitle, embed: embed);

            if (entry.TargetId != 0)
            {
                await SaveThreadLinkAsync(forum.Guild.Id, entry.TargetId, created.Id, titleConfirmed: true);
            }
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

    private async Task<IThreadChannel?> ResolveThreadAsync(IForumChannel forum, IUser? target, ulong targetId)
    {
        var guildId = forum.Guild.Id;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
            var link = await db.ModLogThreadLinks
                .FirstOrDefaultAsync(l => l.GuildId == guildId && l.UserId == targetId);

            if (link is not null)
            {
                var existing = await ResolveThreadByIdAsync(link.ThreadId);
                if (existing is null)
                {
                    db.ModLogThreadLinks.Remove(link);
                    await db.SaveChangesAsync();
                }
                else
                {
                    if (existing.IsArchived)
                    {
                        await existing.ModifyAsync(p => p.Archived = false);
                    }

                    link.LastUsedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                    return existing;
                }
            }
        }

        if (target is null)
        {
            // No live user object to scan titles by username or rename a thread with —
            // the DB-linked lookup above is the only resolution available for this call.
            // The caller creates a fresh post (and still persists the link by ID) if this misses.
            return null;
        }

        var active = await forum.GetActiveThreadsAsync();
        var archived = await forum.GetPublicArchivedThreadsAsync(limit: 200);
        var byId = active.Concat(archived).ToDictionary(t => t.Id);
        var candidates = byId.Values.Select(t => new ThreadCandidate(t.Id, t.Name, t.CreatedAt));

        var match = FindTitleMatch(candidates, targetId, target.Username);
        if (match is null)
        {
            return null;
        }

        var thread = byId[match.ThreadId];
        var confirmed = match.Kind == ThreadMatchKind.ExactId;

        if (!confirmed)
        {
            await thread.ModifyAsync(p => p.Name = $"[{targetId}] {target.Username}");
            await thread.SendMessageAsync(
                $"🔗 Linking this thread to <@{targetId}> (`{targetId}`) based on a title match — let me know if that's wrong.");
        }

        if (thread.IsArchived)
        {
            await thread.ModifyAsync(p => p.Archived = false);
        }

        await SaveThreadLinkAsync(guildId, targetId, thread.Id, confirmed);
        return thread;
    }

    private async Task<IThreadChannel?> ResolveThreadByIdAsync(ulong threadId)
    {
        if (_client is null) return null;

        if (_client.GetChannel(threadId) is IThreadChannel cached)
        {
            return cached;
        }

        try
        {
            var rest = await _client.Rest.GetChannelAsync(threadId);
            return rest as IThreadChannel;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ModerationLog: failed to resolve thread {Id} via REST", threadId);
            return null;
        }
    }

    private async Task SaveThreadLinkAsync(ulong guildId, ulong userId, ulong threadId, bool titleConfirmed)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        db.ModLogThreadLinks.Add(new ModLogThreadLink
        {
            GuildId = guildId,
            UserId = userId,
            ThreadId = threadId,
            TitleConfirmed = titleConfirmed,
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static string BuildThreadTitle(ModerationLogEntry entry)
    {
        if (entry.Target is not null)
            return $"[{entry.TargetId}] {entry.Target.Username}";

        return $"Unknown User - {entry.ActionType} (ID: {entry.TargetId})";
    }

    public static ThreadMatch? FindTitleMatch(IEnumerable<ThreadCandidate> threads, ulong userId, string username)
    {
        var list = threads as IReadOnlyList<ThreadCandidate> ?? threads.ToList();
        var idTag = $"[{userId}]";

        var idMatches = list.Where(t => t.Name.Contains(idTag, StringComparison.Ordinal)).ToList();
        if (idMatches.Count > 0)
        {
            return new ThreadMatch(idMatches[0].ThreadId, ThreadMatchKind.ExactId);
        }

        var fuzzyMatches = list
            .Where(t => t.Name.Contains(username, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (fuzzyMatches.Count == 0)
        {
            return null;
        }

        var best = fuzzyMatches.OrderByDescending(t => t.CreatedAt).First();
        return new ThreadMatch(best.ThreadId, ThreadMatchKind.FuzzyUsername);
    }

    private Embed BuildSpamEmbed(
        IUser? user,
        IReadOnlyList<ITextChannel> channels,
        string fingerprint,
        string? imageFilename = null)
    {
        var text = fingerprint.Split('|', 2)[0];
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

        if (!string.IsNullOrWhiteSpace(imageFilename))
            builder.WithImageUrl($"attachment://{imageFilename}");

        AppendModRoleFooter(builder);
        return builder.Build();
    }

    private Embed BuildActionEmbed(ModerationLogEntry entry)
    {
        var color = entry.ActionType switch
        {
            ModerationActionType.Ban or ModerationActionType.Kick or ModerationActionType.SpamDetected
                => new Color(0xE74C3C),
            ModerationActionType.Mute or ModerationActionType.Warn or ModerationActionType.AutoWarnAllCaps or ModerationActionType.PurgeUser
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

public readonly record struct ThreadCandidate(ulong ThreadId, string Name, DateTimeOffset CreatedAt);

public enum ThreadMatchKind
{
    ExactId,
    FuzzyUsername
}

public record ThreadMatch(ulong ThreadId, ThreadMatchKind Kind);
