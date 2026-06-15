using System.Text.Json;
using DiscordBot.Core;
using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

/// <summary>
/// Polls configured YouTube channels and posts new uploads into a forum channel.
/// </summary>
public class YoutubeMonitorService : BackgroundService
{
    private const string YoutubeApiBaseUrl = "https://www.googleapis.com/youtube/v3/";
    private const string YoutubePlaylistItemsEndpoint = "playlistItems?part=snippet&maxResults=15&playlistId={0}&key={1}";
    private const string YoutubeChannelsContentDetailsEndpoint = "channels?part=contentDetails&id={0}&key={1}";
    private const int MinimumPollIntervalMinutes = 60;
    private static readonly TimeSpan QuotaBackoffDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan RateLimitBackoffDuration = TimeSpan.FromMinutes(10);

    private readonly DiscordSocketClient _client;
    private readonly BotConfig _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private readonly YoutubeChannelSearchService _channelSearchService;
    private readonly ILogger<YoutubeMonitorService> _logger;
    private readonly object _apiBackoffLock = new();

    private DateTime _youtubeApiBackoffUntilUtc;
    private string? _youtubeApiBackoffReason;
    private bool _hasLoggedPollIntervalClamp;

    public YoutubeMonitorService(
        DiscordSocketClient client,
        BotConfig config,
        IServiceProvider serviceProvider,
        YoutubeChannelSearchService channelSearchService,
        ILogger<YoutubeMonitorService> logger)
    {
        _client = client;
        _config = config;
        _serviceProvider = serviceProvider;
        _channelSearchService = channelSearchService;
        _logger = logger;

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HomotechsualBot/1.0");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (_client.ConnectionState != ConnectionState.Connected && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollChannelsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling YouTube feeds.");
            }

            var delay = await GetPollIntervalAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(delay), stoppingToken);
        }
    }

    private async Task PollChannelsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        await EnsureSeededAsync(db, cancellationToken);

        var settings = await db.YoutubeMonitorSettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (settings == null || !settings.Enabled || settings.ForumChannelId == 0)
        {
            _logger.LogInformation("YouTube monitor is disabled or not configured — skipping.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.YoutubeMonitor.YouTubeDataApiKey))
        {
            _logger.LogWarning("YouTube monitor is enabled but YouTubeDataApiKey is not configured. Set Bot:YoutubeMonitor:YouTubeDataApiKey.");
            return;
        }

        if (TryGetApiBackoff(out var backoffUntilUtc, out var backoffReason))
        {
            _logger.LogWarning(
                "YouTube monitor is backing off YouTube Data API polling until {BackoffUntilUtc:o}. Reason: {BackoffReason}",
                backoffUntilUtc,
                backoffReason);
            return;
        }

        var channels = await db.YoutubeTrackedChannels
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.ChannelName)
            .ToListAsync(cancellationToken);

        if (channels.Count == 0)
        {
            _logger.LogInformation("YouTube monitor has no tracked channels configured — skipping.");
            return;
        }

        var normalizedChannels = new List<YoutubeTrackedChannel>(channels.Count);
        var invalidChannels = new List<string>();
        var normalizedUpdated = 0;

        foreach (var trackedChannel in channels)
        {
            var originalReference = trackedChannel.ChannelId;

            if (!YoutubeChannelReferenceParser.TryNormalize(originalReference, out var normalizedReference) ||
                string.IsNullOrWhiteSpace(normalizedReference))
            {
                var searchResult = await _channelSearchService.SearchAsync(originalReference, cancellationToken);
                if (searchResult == null)
                {
                    trackedChannel.IsEnabled = false;
                    trackedChannel.UpdatedAt = DateTime.UtcNow;
                    invalidChannels.Add(originalReference);
                    continue;
                }

                normalizedReference = searchResult.ChannelId;
                trackedChannel.ChannelId = searchResult.ChannelId;

                if (string.IsNullOrWhiteSpace(trackedChannel.ChannelName) ||
                    string.Equals(trackedChannel.ChannelName.Trim(), originalReference.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    trackedChannel.ChannelName = searchResult.ChannelName;
                }

                trackedChannel.UpdatedAt = DateTime.UtcNow;
                normalizedUpdated++;
            }

            // Reject placeholder/invalid UC IDs so they do not spam feed polling warnings.
            if (normalizedReference.StartsWith("UC", StringComparison.OrdinalIgnoreCase) &&
                !LooksLikeYoutubeChannelId(normalizedReference))
            {
                trackedChannel.IsEnabled = false;
                trackedChannel.UpdatedAt = DateTime.UtcNow;
                invalidChannels.Add(trackedChannel.ChannelId);
                continue;
            }

            if (!string.Equals(trackedChannel.ChannelId, normalizedReference, StringComparison.Ordinal))
            {
                trackedChannel.ChannelId = normalizedReference;
                trackedChannel.UpdatedAt = DateTime.UtcNow;
                normalizedUpdated++;
            }

            normalizedChannels.Add(trackedChannel);
        }

        BotMetrics.YoutubeTrackedChannels.Set(normalizedChannels.Count);

        if (invalidChannels.Count > 0 || normalizedUpdated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (invalidChannels.Count > 0)
        {
            _logger.LogWarning(
                "YouTube monitor disabled {InvalidCount} invalid tracked channel(s): {Channels}",
                invalidChannels.Count,
                string.Join(", ", invalidChannels));
        }

        if (normalizedChannels.Count == 0)
        {
            _logger.LogInformation("YouTube monitor has no valid tracked channels configured — skipping.");
            return;
        }

        var effectivePollInterval = GetEffectivePollIntervalMinutes(settings.PollIntervalMinutes);
        if (settings.PollIntervalMinutes < MinimumPollIntervalMinutes && !_hasLoggedPollIntervalClamp)
        {
            _logger.LogWarning(
                "YouTube monitor PollIntervalMinutes={ConfiguredInterval} is too aggressive for YouTube Data API search quota. Clamping to {EffectiveInterval} minute(s).",
                settings.PollIntervalMinutes,
                effectivePollInterval);
            _hasLoggedPollIntervalClamp = true;
        }

        _logger.LogInformation("YouTube monitor started. Polling {Count} valid channel(s) every {Interval} minute(s).", normalizedChannels.Count, effectivePollInterval);

        if (_client.GetChannel(settings.ForumChannelId) is not IForumChannel forumChannel)
        {
            _logger.LogWarning("Configured YouTube forum channel {ChannelId} was not found or is not a forum channel.", settings.ForumChannelId);
            return;
        }

        foreach (var youtubeChannel in normalizedChannels)
        {
            await PollChannelAsync(db, forumChannel, settings, youtubeChannel, cancellationToken);

            if (TryGetApiBackoff(out _, out _))
            {
                break;
            }
        }
    }

    private async Task<int> GetPollIntervalAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        await EnsureSeededAsync(db, cancellationToken);

        var settings = await db.YoutubeMonitorSettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return GetEffectivePollIntervalMinutes(settings?.PollIntervalMinutes ?? MinimumPollIntervalMinutes);
    }

    private static int GetEffectivePollIntervalMinutes(int configuredInterval)
    {
        if (configuredInterval <= 0)
        {
            return MinimumPollIntervalMinutes;
        }

        return Math.Max(configuredInterval, MinimumPollIntervalMinutes);
    }

    private async Task EnsureSeededAsync(HomotechsualBotContext db, CancellationToken cancellationToken)
    {
        if (!await db.YoutubeMonitorSettings.AnyAsync(cancellationToken))
        {
            var youtubeConfig = _config.YoutubeMonitor;
            db.YoutubeMonitorSettings.Add(new YoutubeMonitorSettings
            {
                Enabled = youtubeConfig.Enabled,
                ForumChannelId = youtubeConfig.ForumChannelId,
                PollIntervalMinutes = youtubeConfig.PollIntervalMinutes,
                DefaultPostTitleTemplate = string.IsNullOrWhiteSpace(youtubeConfig.DefaultPostTitleTemplate)
                    ? "[{ChannelName}] {VideoTitle}"
                    : youtubeConfig.DefaultPostTitleTemplate,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!await db.YoutubeTrackedChannels.AnyAsync(cancellationToken) && _config.YoutubeMonitor.Channels.Count > 0)
        {
            foreach (var channel in _config.YoutubeMonitor.Channels.Where(c => !string.IsNullOrWhiteSpace(c.ChannelId)))
            {
                var keywords = channel.KeywordFilters?.Count > 0
                    ? string.Join(";", channel.KeywordFilters.Select(k => k.Trim()))
                    : null;

                var channelReference = channel.ChannelId.Trim();
                var channelName = string.IsNullOrWhiteSpace(channel.ChannelName) ? channelReference : channel.ChannelName.Trim();

                if (!YoutubeChannelReferenceParser.TryNormalize(channelReference, out var normalizedReference) ||
                    string.IsNullOrWhiteSpace(normalizedReference))
                {
                    var searchResult = await _channelSearchService.SearchAsync(channelReference, cancellationToken);
                    if (searchResult == null)
                    {
                        _logger.LogWarning("Skipping configured YouTube seed channel because it could not be resolved: {ChannelReference}", channelReference);
                        continue;
                    }

                    normalizedReference = searchResult.ChannelId;
                    channelName = string.IsNullOrWhiteSpace(channel.ChannelName) ? searchResult.ChannelName : channelName;
                }

                db.YoutubeTrackedChannels.Add(new YoutubeTrackedChannel
                {
                    ChannelId = normalizedReference,
                    ChannelName = channelName,
                    PostTitleTemplate = string.IsNullOrWhiteSpace(channel.PostTitleTemplate) ? null : channel.PostTitleTemplate.Trim(),
                    KeywordFilters = keywords,
                    IsEnabled = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task PollChannelAsync(
        HomotechsualBotContext db,
        IForumChannel forumChannel,
        YoutubeMonitorSettings settings,
        YoutubeTrackedChannel youtubeChannel,
        CancellationToken cancellationToken)
    {
        if (!YoutubeChannelReferenceParser.TryNormalize(youtubeChannel.ChannelId, out var normalizedReference))
        {
            _logger.LogWarning(
                "Skipping tracked YouTube channel because the stored reference is not a supported YouTube channel ID, @handle, or feed URL. DbId={DbId}, ChannelId={ChannelId}, ChannelName={ChannelName}",
                youtubeChannel.Id,
                youtubeChannel.ChannelId,
                youtubeChannel.ChannelName ?? "(null)");
            return;
        }

        var feed = await LoadFeedAsync(normalizedReference, cancellationToken);
        if (feed == null)
        {
            _logger.LogDebug(
                "Skipping tracked YouTube channel due to feed load failure. DbId={DbId}, ChannelId={ChannelId}, ChannelName={ChannelName}",
                youtubeChannel.Id,
                youtubeChannel.ChannelId,
                youtubeChannel.ChannelName ?? "(null)");
            return;
        }

        var channelName = !string.IsNullOrWhiteSpace(youtubeChannel.ChannelName)
            ? youtubeChannel.ChannelName!.Trim()
            : feed.ChannelName;

        var stateKey = youtubeChannel.ChannelId.Trim();
        var state = await db.FeedPostStates.FirstOrDefaultAsync(x => x.FeedType == "YouTube" && x.SourceId == stateKey, cancellationToken);

        if (state == null)
        {
            state = new FeedPostState
            {
                FeedType = "YouTube",
                SourceId = stateKey
            };
            db.FeedPostStates.Add(state);
        }

        if (string.IsNullOrWhiteSpace(state.LastPostedItemId))
        {
            var latestVideoId = feed.Videos.FirstOrDefault()?.VideoId;
            if (!string.IsNullOrWhiteSpace(latestVideoId))
            {
                state.LastPostedItemId = latestVideoId;
                state.LastCheckedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("YouTube channel {ChannelId} initialised — latest video {VideoId} stored as baseline.", youtubeChannel.ChannelId, latestVideoId);
            }

            return;
        }

        var pendingVideos = GetPendingVideos(feed.Videos, state, youtubeChannel.KeywordFilters);
        if (pendingVideos.Count == 0)
        {
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var tagName = NormalizeForumTagName(channelName);
        var forumTag = await EnsureForumTagAsync(forumChannel, tagName);
        if (forumTag == null)
        {
            _logger.LogWarning("Could not resolve or create forum tag {TagName} for YouTube channel {ChannelId}.", tagName, youtubeChannel.ChannelId);
            return;
        }

        var resolvedForumTag = (ForumTag)forumTag;

        foreach (var video in pendingVideos)
        {
            _logger.LogInformation(
                "Observed YouTube candidate video {VideoId} from {ChannelName} ({ChannelId}) published {PublishedAtUtc}.",
                video.VideoId,
                channelName,
                youtubeChannel.ChannelId,
                video.PublishedAt.Kind == DateTimeKind.Utc ? video.PublishedAt : video.PublishedAt.ToUniversalTime());

            var postTitle = BuildPostTitle(settings, youtubeChannel, channelName, video);
            var roleMention = _config.YoutubeMonitor.RoleId != 0
                ? $"<@&{_config.YoutubeMonitor.RoleId}>"
                : string.Empty;
            var body = BuildPostBody(youtubeChannel.ChannelId, channelName, video, roleMention);

            _logger.LogInformation("Posting YouTube video {VideoId} with title ({TitleLength} chars): {PostTitle}", video.VideoId, postTitle.Length, postTitle);

            try
            {
                await forumChannel.CreatePostAsync(postTitle, text: body, tags: new ForumTag[] { resolvedForumTag });
                state.LastPostedItemId = video.VideoId;
                state.LastCheckedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Posted YouTube video {VideoId} from {ChannelName} to forum channel {ForumChannelId}.", video.VideoId, channelName, forumChannel.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post YouTube video {VideoId} from {ChannelName}.", video.VideoId, channelName);
                break;
            }
        }
    }

    private static List<YouTubeVideoEntry> GetPendingVideos(IReadOnlyList<YouTubeVideoEntry> videos, FeedPostState state, string? keywordFilters = null)
    {
        var pending = new List<YouTubeVideoEntry>();
        
        if (!string.IsNullOrWhiteSpace(state.LastPostedItemId))
        {
            var lastPostedIndex = videos
                .Select((video, index) => new { Video = video, Index = index })
                .FirstOrDefault(entry => string.Equals(entry.Video.VideoId, state.LastPostedItemId, StringComparison.OrdinalIgnoreCase))
                ?.Index;

            if (lastPostedIndex.HasValue)
            {
                pending = videos.Take(lastPostedIndex.Value).Reverse().ToList();
            }
        }
        else if (state.LastCheckedAt.HasValue)
        {
            pending = videos
                .Where(video => video.PublishedAt > state.LastCheckedAt.Value)
                .Reverse()
                .ToList();
        }

        // Apply keyword filters if configured
        if (!string.IsNullOrWhiteSpace(keywordFilters))
        {
            var keywords = keywordFilters
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();

            if (keywords.Count > 0)
            {
                pending = pending
                    .Where(video => keywords.Any(keyword => 
                        video.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        video.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
        }

        return pending;
    }

    private async Task<ForumTag?> EnsureForumTagAsync(IForumChannel forumChannel, string tagName)
    {
        var existingTag = forumChannel.Tags.FirstOrDefault(tag => tag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (existingTag != null)
        {
            return existingTag;
        }

        var updatedTags = forumChannel.Tags.Select(tag => tag.ToForumTagBuilder()).ToList();
        updatedTags.Add(new ForumTagBuilder(tagName, id: null, isModerated: false));

        await forumChannel.ModifyAsync(properties => properties.Tags = Optional.Create<IEnumerable<IForumTag>>(updatedTags.Cast<IForumTag>().ToList()));

        existingTag = forumChannel.Tags.FirstOrDefault(tag => tag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        return existingTag;
    }

    private static string BuildPostTitle(YoutubeMonitorSettings settings, YoutubeTrackedChannel youtubeChannel, string channelName, YouTubeVideoEntry video)
    {
        var template = string.IsNullOrWhiteSpace(youtubeChannel.PostTitleTemplate)
            ? settings.DefaultPostTitleTemplate
            : youtubeChannel.PostTitleTemplate!;

        template = NormalizeTemplateNewlines(template);

        string title;
        if (!string.IsNullOrWhiteSpace(template))
        {
            var publishedAtUtc = video.PublishedAt.Kind == DateTimeKind.Utc
                ? video.PublishedAt
                : video.PublishedAt.ToUniversalTime();
            var publishedUnix = new DateTimeOffset(publishedAtUtc).ToUnixTimeSeconds();

            title = template
                .Replace("{ChannelName}", channelName, StringComparison.OrdinalIgnoreCase)
                .Replace("{ChannelId}", youtubeChannel.ChannelId, StringComparison.OrdinalIgnoreCase)
                .Replace("{VideoTitle}", video.Title, StringComparison.OrdinalIgnoreCase)
                .Replace("{VideoId}", video.VideoId, StringComparison.OrdinalIgnoreCase)
                .Replace("{VideoUrl}", video.Url, StringComparison.OrdinalIgnoreCase)
                .Replace("{PublishedDate}", publishedAtUtc.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
                .Replace("{PublishedAtUtc}", publishedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), StringComparison.OrdinalIgnoreCase)
                .Replace("{PublishedAtDiscord}", $"<t:{publishedUnix}:f>", StringComparison.OrdinalIgnoreCase)
                .Replace("{PublishedAtDiscordRelative}", $"<t:{publishedUnix}:R>", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            title = $"[{channelName}] {video.Title}";
        }

        if (title.Length > 100)
        {
            title = title[..100];
            if (char.IsHighSurrogate(title[^1]))
                title = title[..^1];
        }

        return string.IsNullOrWhiteSpace(title) ? $"[{channelName}] {video.VideoId}" : title;
    }

    private string BuildPostBody(string channelId, string channelName, YouTubeVideoEntry video, string roleMention)
    {
        var template = string.IsNullOrWhiteSpace(_config.YoutubeMonitor.DefaultPostBodyTemplate)
            ? "New video from **{ChannelName}**\n{VideoUrl}"
            : _config.YoutubeMonitor.DefaultPostBodyTemplate;

        template = NormalizeTemplateNewlines(template);

        var publishedAtUtc = video.PublishedAt.Kind == DateTimeKind.Utc
            ? video.PublishedAt
            : video.PublishedAt.ToUniversalTime();
        var publishedUnix = new DateTimeOffset(publishedAtUtc).ToUnixTimeSeconds();

        var renderedBody = template
            .Replace("{ChannelName}", channelName, StringComparison.OrdinalIgnoreCase)
            .Replace("{ChannelId}", channelId, StringComparison.OrdinalIgnoreCase)
            .Replace("{VideoTitle}", video.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{VideoId}", video.VideoId, StringComparison.OrdinalIgnoreCase)
            .Replace("{VideoUrl}", video.Url, StringComparison.OrdinalIgnoreCase)
            .Replace("{PublishedDate}", publishedAtUtc.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{PublishedAtUtc}", publishedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), StringComparison.OrdinalIgnoreCase)
            .Replace("{PublishedAtDiscord}", $"<t:{publishedUnix}:f>", StringComparison.OrdinalIgnoreCase)
            .Replace("{PublishedAtDiscordRelative}", $"<t:{publishedUnix}:R>", StringComparison.OrdinalIgnoreCase)
            .Replace("{VideoDescription}", video.Description, StringComparison.OrdinalIgnoreCase)
            .Replace("{RoleMention}", roleMention, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(roleMention) && !template.Contains("{RoleMention}", StringComparison.OrdinalIgnoreCase))
        {
            return $"{roleMention}\n{renderedBody}";
        }

        return renderedBody;
    }

    private static string NormalizeTemplateNewlines(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        // Environment-backed templates are often provided as escaped one-line values.
        return template
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);
    }

    private static string NormalizeForumTagName(string channelName)
    {
        var trimmed = channelName.Trim();
        return trimmed.Length <= 20 ? trimmed : trimmed[..20].Trim();
    }

    private async Task<YouTubeFeed?> LoadFeedAsync(string channelReference, CancellationToken cancellationToken)
    {
        var normalizedReference = channelReference.Trim();
        var apiKey = _config.YoutubeMonitor.YouTubeDataApiKey?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        if (TryGetApiBackoff(out _, out _))
        {
            return null;
        }

        var channelId = await ResolveChannelIdAsync(normalizedReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelId))
        {
            _logger.LogWarning("Failed to resolve YouTube channel ID for reference {ChannelReference}.", normalizedReference);
            return null;
        }

        var uploadsPlaylistId = TryBuildUploadsPlaylistId(channelId)
            ?? await GetUploadsPlaylistIdAsync(channelId, apiKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(uploadsPlaylistId))
        {
            _logger.LogWarning("Failed to resolve uploads playlist for channel {ChannelId}.", channelId);
            return null;
        }

        var requestUrl = YoutubeApiBaseUrl + string.Format(
            YoutubePlaylistItemsEndpoint,
            Uri.EscapeDataString(uploadsPlaylistId),
            Uri.EscapeDataString(apiKey));

        try
        {
            using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var reason = TryGetYoutubeApiErrorReason(payload);
                var statusCode = (int)response.StatusCode;

                if (IsQuotaErrorReason(reason))
                {
                    SetApiBackoff(QuotaBackoffDuration, reason!);
                }
                else if (IsRateLimitErrorReason(reason))
                {
                    SetApiBackoff(RateLimitBackoffDuration, reason!);
                }

                _logger.LogWarning(
                    "Failed to load YouTube uploads playlist items for channel {ChannelId}. HTTP {StatusCode}. Reason: {Reason}",
                    channelId,
                    statusCode,
                    string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
                return null;
            }

            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return new YouTubeFeed(normalizedReference, new List<YouTubeVideoEntry>());
            }

            var videos = new List<YouTubeVideoEntry>();
            string? channelName = null;

            foreach (var item in itemsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("snippet", out var snippetElement))
                {
                    continue;
                }

                if (!snippetElement.TryGetProperty("resourceId", out var resourceIdElement) ||
                    !resourceIdElement.TryGetProperty("videoId", out var videoIdElement))
                {
                    continue;
                }

                var videoId = videoIdElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(videoId))
                {
                    continue;
                }

                var title = snippetElement.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString()?.Trim()
                    : null;
                var description = snippetElement.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

                channelName ??= snippetElement.TryGetProperty("channelTitle", out var channelTitleElement)
                    ? channelTitleElement.GetString()?.Trim()
                    : null;

                var publishedAt = DateTime.UtcNow;
                if (snippetElement.TryGetProperty("publishedAt", out var publishedAtElement))
                {
                    var publishedAtText = publishedAtElement.GetString();
                    if (!string.IsNullOrWhiteSpace(publishedAtText) && DateTimeOffset.TryParse(publishedAtText, out var parsedPublishedAt))
                    {
                        publishedAt = parsedPublishedAt.UtcDateTime;
                    }
                }

                videos.Add(new YouTubeVideoEntry(
                    videoId,
                    string.IsNullOrWhiteSpace(title) ? videoId : title,
                    $"https://www.youtube.com/watch?v={videoId}",
                    publishedAt,
                    description));
            }

            var resolvedChannelName = string.IsNullOrWhiteSpace(channelName) ? normalizedReference : channelName;
            var orderedVideos = videos
                .OrderByDescending(video => video.PublishedAt)
                .ToList();

            return new YouTubeFeed(resolvedChannelName!, orderedVideos);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load YouTube Data API videos for channel {ChannelReference}.", normalizedReference);
            return null;
        }
    }

    private static string? TryBuildUploadsPlaylistId(string channelId)
    {
        var trimmed = channelId.Trim();
        if (!trimmed.StartsWith("UC", StringComparison.OrdinalIgnoreCase) || trimmed.Length < 3)
        {
            return null;
        }

        return "UU" + trimmed[2..];
    }

    private async Task<string?> GetUploadsPlaylistIdAsync(string channelId, string apiKey, CancellationToken cancellationToken)
    {
        var requestUrl = YoutubeApiBaseUrl + string.Format(
            YoutubeChannelsContentDetailsEndpoint,
            Uri.EscapeDataString(channelId),
            Uri.EscapeDataString(apiKey));

        using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var reason = TryGetYoutubeApiErrorReason(payload);
            var statusCode = (int)response.StatusCode;

            if (IsQuotaErrorReason(reason))
            {
                SetApiBackoff(QuotaBackoffDuration, reason!);
            }
            else if (IsRateLimitErrorReason(reason))
            {
                SetApiBackoff(RateLimitBackoffDuration, reason!);
            }

            _logger.LogWarning(
                "Failed to load YouTube channel content details for channel {ChannelId}. HTTP {StatusCode}. Reason: {Reason}",
                channelId,
                statusCode,
                string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("items", out var itemsElement) ||
            itemsElement.ValueKind != JsonValueKind.Array ||
            itemsElement.GetArrayLength() == 0)
        {
            return null;
        }

        var firstItem = itemsElement[0];
        if (!firstItem.TryGetProperty("contentDetails", out var contentDetailsElement) ||
            !contentDetailsElement.TryGetProperty("relatedPlaylists", out var relatedPlaylistsElement) ||
            !relatedPlaylistsElement.TryGetProperty("uploads", out var uploadsElement))
        {
            return null;
        }

        var uploadsPlaylistId = uploadsElement.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(uploadsPlaylistId) ? null : uploadsPlaylistId;
    }

    private static bool LooksLikeYoutubeChannelId(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("UC", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 20;
    }

    private bool TryGetApiBackoff(out DateTime backoffUntilUtc, out string backoffReason)
    {
        lock (_apiBackoffLock)
        {
            if (_youtubeApiBackoffUntilUtc > DateTime.UtcNow)
            {
                backoffUntilUtc = _youtubeApiBackoffUntilUtc;
                backoffReason = string.IsNullOrWhiteSpace(_youtubeApiBackoffReason) ? "unknown" : _youtubeApiBackoffReason;
                BotMetrics.YoutubeApiBackoffActive.Set(1);
                return true;
            }

            _youtubeApiBackoffUntilUtc = DateTime.MinValue;
            _youtubeApiBackoffReason = null;
            backoffUntilUtc = DateTime.MinValue;
            backoffReason = string.Empty;
            BotMetrics.YoutubeApiBackoffActive.Set(0);
            return false;
        }
    }

    private void SetApiBackoff(TimeSpan duration, string reason)
    {
        var until = DateTime.UtcNow.Add(duration);

        lock (_apiBackoffLock)
        {
            if (until <= _youtubeApiBackoffUntilUtc)
            {
                return;
            }

            _youtubeApiBackoffUntilUtc = until;
            _youtubeApiBackoffReason = reason;
        }

        BotMetrics.YoutubeApiBackoffActive.Set(1);

        _logger.LogWarning(
            "YouTube Data API backoff activated for {BackoffMinutes} minute(s) due to {Reason}. Next retry after {BackoffUntilUtc:o}.",
            Math.Round(duration.TotalMinutes),
            reason,
            until);
    }

    private static bool IsQuotaErrorReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Equals("quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
               reason.Equals("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
               reason.Equals("dailyLimitExceededUnreg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRateLimitErrorReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Equals("rateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
               reason.Equals("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetYoutubeApiErrorReason(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("error", out var errorElement))
            {
                return null;
            }

            if (errorElement.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in errorsElement.EnumerateArray())
                {
                    if (entry.TryGetProperty("reason", out var reasonElement))
                    {
                        var reasonValue = reasonElement.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(reasonValue))
                        {
                            return reasonValue;
                        }
                    }
                }
            }

            if (errorElement.TryGetProperty("message", out var messageElement))
            {
                var message = messageElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<string?> ResolveChannelIdAsync(string channelReference, CancellationToken cancellationToken)
    {
        var trimmed = channelReference.Trim();
        if (LooksLikeYoutubeChannelId(trimmed))
        {
            return trimmed;
        }

        if (TryExtractChannelIdFromUrl(trimmed, out var channelIdFromUrl) && LooksLikeYoutubeChannelId(channelIdFromUrl))
        {
            return channelIdFromUrl;
        }

        var searchResult = await _channelSearchService.SearchAsync(trimmed, cancellationToken);
        return searchResult?.ChannelId;
    }

    private static bool TryExtractChannelIdFromUrl(string input, out string channelId)
    {
        channelId = string.Empty;

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            !uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length >= 2 && segments[0].Equals("channel", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(segments[1]))
        {
            channelId = segments[1].Trim();
            return true;
        }

        return false;
    }

    private sealed record YouTubeFeed(string ChannelName, IReadOnlyList<YouTubeVideoEntry> Videos);

    private sealed record YouTubeVideoEntry(string VideoId, string Title, string Url, DateTime PublishedAt, string Description = "");

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }
}

