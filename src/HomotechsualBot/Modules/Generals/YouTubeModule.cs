using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using DiscordBot.Services;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Modules.Generals;

[Group("youtube", "Manage YouTube forum posting")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public class YouTubeModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly HomotechsualBotContext _db;
    private readonly YoutubeChannelSearchService _channelSearchService;
    private readonly BotConfig _botConfig;

    public YouTubeModule(HomotechsualBotContext db, YoutubeChannelSearchService channelSearchService, BotConfig botConfig)
    {
        _db = db;
        _channelSearchService = channelSearchService;
        _botConfig = botConfig;
    }

    [SlashCommand("set-forum-channel", "Set the forum channel used for YouTube posts")]
    public async Task SetForumChannelAsync([Summary("forum-channel", "Forum channel to post into")][ChannelTypes(ChannelType.Forum)] IForumChannel forumChannel)
    {
        await DeferAsync(ephemeral: true);

        var settings = await GetOrCreateSettingsAsync();
        settings.ForumChannelId = forumChannel.Id;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync($"✅ YouTube posts will be sent to <#{forumChannel.Id}>.");
    }

    [SlashCommand("enable", "Enable or disable YouTube monitoring")]
    public async Task EnableAsync([Summary("enabled", "Whether YouTube monitoring should be enabled")] bool enabled)
    {
        await DeferAsync(ephemeral: true);

        var settings = await GetOrCreateSettingsAsync();
        settings.Enabled = enabled;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync(enabled ? "✅ YouTube monitoring enabled." : "⛔ YouTube monitoring disabled.");
    }

    [SlashCommand("set-interval", "Set the polling interval in minutes")]
    public async Task SetIntervalAsync([Summary("minutes", "Polling interval in minutes")] int minutes)
    {
        await DeferAsync(ephemeral: true);

        if (minutes < 1 || minutes > 1440)
        {
            await FollowupAsync("❌ Interval must be between 1 and 1440 minutes.");
            return;
        }

        var settings = await GetOrCreateSettingsAsync();
        settings.PollIntervalMinutes = minutes;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync($"✅ YouTube polling interval set to {minutes} minute(s).");
    }

    [SlashCommand("set-default-template", "Set the default forum post title template")]
    public async Task SetDefaultTemplateAsync([Summary("template", "Title template; see README for supported placeholders")] string template)
    {
        await DeferAsync(ephemeral: true);

        if (string.IsNullOrWhiteSpace(template))
        {
            await FollowupAsync("❌ Template cannot be empty.");
            return;
        }

        var settings = await GetOrCreateSettingsAsync();
        settings.DefaultPostTitleTemplate = template.Trim();
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync($"✅ Default title template updated to `{settings.DefaultPostTitleTemplate}`.");
    }

    [SlashCommand("add-channel", "Track a YouTube channel")]
    public async Task AddChannelAsync(
        [Summary("channel", "YouTube channel ID, @handle, or feed URL")] string channelReference,
        [Summary("name", "Optional display name for forum tags")] string? displayName = null,
        [Summary("template", "Optional title template for this channel")] string? titleTemplate = null,
        [Summary("keywords", "Optional semicolon-separated keywords to filter videos (e.g., 'Halo;Campaign')")] string? keywords = null)
    {
        await DeferAsync(ephemeral: true);

        var resolvedName = displayName?.Trim();
        string channelId;

        if (YoutubeChannelReferenceParser.TryNormalize(channelReference, out var normalizedReference) && !string.IsNullOrWhiteSpace(normalizedReference))
        {
            channelId = normalizedReference;
        }
        else
        {
            var searchResult = await _channelSearchService.SearchAsync(channelReference, CancellationToken.None);
            if (searchResult == null)
            {
                await FollowupAsync("❌ Provide a valid YouTube channel ID, @handle, channel feed URL, or a channel name that can be resolved with the YouTube Data API.");
                return;
            }

            channelId = searchResult.ChannelId;
            resolvedName ??= searchResult.ChannelName;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            await FollowupAsync("❌ Provide a valid YouTube channel ID, @handle, or channel feed URL.");
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? resolvedName ?? channelId
            : displayName.Trim();

        var normalizedKeywords = string.IsNullOrWhiteSpace(keywords)
            ? null
            : string.Join(";", keywords.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k)));

        var existing = await _db.YoutubeTrackedChannels.FirstOrDefaultAsync(x => x.ChannelId == channelId);
        if (existing == null)
        {
            existing = new YoutubeTrackedChannel
            {
                ChannelId = channelId,
                ChannelName = normalizedName,
                PostTitleTemplate = string.IsNullOrWhiteSpace(titleTemplate) ? null : titleTemplate.Trim(),
                KeywordFilters = normalizedKeywords,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.YoutubeTrackedChannels.Add(existing);
        }
        else
        {
            existing.ChannelName = normalizedName;
            existing.PostTitleTemplate = string.IsNullOrWhiteSpace(titleTemplate) ? existing.PostTitleTemplate : titleTemplate.Trim();
            existing.KeywordFilters = normalizedKeywords;
            existing.IsEnabled = true;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        var keywordInfo = normalizedKeywords != null ? $" with keywords: `{normalizedKeywords}`" : "";
        await FollowupAsync($"✅ Tracking YouTube channel `{channelId}` as `{normalizedName}`{keywordInfo}.");
    }

    [SlashCommand("remove-channel", "Stop tracking a YouTube channel")]
    public async Task RemoveChannelAsync([Summary("channel", "YouTube channel ID or feed URL")] string channelReference)
    {
        await DeferAsync(ephemeral: true);

        if (!YoutubeChannelReferenceParser.TryNormalize(channelReference, out var channelId) || string.IsNullOrWhiteSpace(channelId))
        {
            await FollowupAsync("❌ Provide a valid YouTube channel ID or channel feed URL.");
            return;
        }

        var tracked = await _db.YoutubeTrackedChannels.FirstOrDefaultAsync(x => x.ChannelId == channelId);
        if (tracked == null)
        {
            await FollowupAsync($"ℹ No tracked YouTube channel found for `{channelId}`.");
            return;
        }

        _db.YoutubeTrackedChannels.Remove(tracked);
        await _db.SaveChangesAsync();

        await FollowupAsync($"✅ Stopped tracking `{channelId}`.");
    }

    [SlashCommand("list", "List the current YouTube monitor configuration")]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true);

        var settings = await GetOrCreateSettingsAsync();
        var channels = await _db.YoutubeTrackedChannels.AsNoTracking().OrderBy(x => x.ChannelName).ToListAsync();

        var embed = new EmbedBuilder()
            .WithTitle("YouTube monitor")
            .WithColor(Color.Blue)
            .AddField("Enabled", settings.Enabled ? "Yes" : "No", true)
            .AddField("Forum channel", settings.ForumChannelId == 0 ? "Not set" : $"<#{settings.ForumChannelId}>", true)
            .AddField("Poll interval", $"{settings.PollIntervalMinutes} minute(s)", true)
            .AddField("Default title template", settings.DefaultPostTitleTemplate, false)
            .AddField("Default body template", _botConfig.YoutubeMonitor.DefaultPostBodyTemplate, false);

        if (channels.Count == 0)
        {
            embed.AddField("Tracked channels", "None configured", false);
        }
        else
        {
            var lines = channels.Select(channel =>
            {
                var template = channel.PostTitleTemplate ?? settings.DefaultPostTitleTemplate;
                var status = channel.IsEnabled ? "Enabled" : "Disabled";
                var filters = string.IsNullOrWhiteSpace(channel.KeywordFilters) ? "None" : channel.KeywordFilters;
                return $"• `{channel.ChannelName}` (`{channel.ChannelId}`) - {status}\n  Template: `{template}` | Keywords: `{filters}`";
            });

            embed.AddField("Tracked channels", string.Join("\n", lines), false);
        }

        await FollowupAsync(embed: embed.Build());
    }

    private async Task<YoutubeMonitorSettings> GetOrCreateSettingsAsync()
    {
        var settings = await _db.YoutubeMonitorSettings
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (settings != null)
        {
            return settings;
        }

        settings = new YoutubeMonitorSettings
        {
            Enabled = false,
            ForumChannelId = 0,
            PollIntervalMinutes = 15,
            DefaultPostTitleTemplate = "[{ChannelName}] {VideoTitle}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.YoutubeMonitorSettings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }
}

