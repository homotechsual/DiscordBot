using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using DiscordBot.Core;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

/// <summary>
/// Polls tracked GitHub repositories and posts new issues, pull requests,
/// workflow runs, and releases to Discord channels.
/// </summary>
public class GitHubMonitorService : BackgroundService
{
    private const string GitHubApiBaseUrl = "https://api.github.com/";
    private const int MinimumPollIntervalMinutes = 5;
    private const int PageSize = 20;
    private static readonly TimeSpan RateLimitBackoffDuration = TimeSpan.FromMinutes(15);

    private readonly DiscordSocketClient _client;
    private readonly BotConfig _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GitHubMonitorService> _logger;
    private readonly HttpClient _httpClient;
    private readonly object _backoffLock = new();

    private DateTime _backoffUntilUtc;

    public GitHubMonitorService(
        DiscordSocketClient client,
        BotConfig config,
        IServiceProvider serviceProvider,
        ILogger<GitHubMonitorService> logger)
    {
        _client = client;
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _httpClient = new HttpClient { BaseAddress = new Uri(GitHubApiBaseUrl) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HomotechsualBot/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrWhiteSpace(_config.GitHubMonitor.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.GitHubMonitor.Token.Trim());
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (_client.ConnectionState != ConnectionState.Connected && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("GitHub monitor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = MinimumPollIntervalMinutes;

            try
            {
                intervalMinutes = await PollRepositoriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling GitHub repositories.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("GitHub monitor stopped.");
    }

    private async Task<int> PollRepositoriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        await EnsureSeededAsync(db, cancellationToken);

        var settings = await db.GitHubMonitorSettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var pollInterval = GetEffectivePollIntervalMinutes(settings?.PollIntervalMinutes ?? MinimumPollIntervalMinutes);

        if (settings == null || !settings.Enabled)
        {
            _logger.LogDebug("GitHub monitor is disabled - skipping poll.");
            return pollInterval;
        }

        if (IsBackingOff(out var backoffUntilUtc))
        {
            _logger.LogWarning("GitHub monitor is backing off API polling until {BackoffUntilUtc:o}.", backoffUntilUtc);
            return pollInterval;
        }

        var repositories = await db.GitHubTrackedRepositories
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Owner).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        BotMetrics.GitHubTrackedRepositories.Set(repositories.Count);
        BotMetrics.GitHubLastPollTimestamp.Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (repositories.Count == 0)
        {
            _logger.LogDebug("GitHub monitor has no tracked repositories - skipping poll.");
            return pollInterval;
        }

        foreach (var repository in repositories)
        {
            foreach (var category in Enum.GetValues<GitHubEventCategory>())
            {
                if (!repository.IsCategoryEnabled(category))
                    continue;

                var channel = ResolveChannel(repository, settings, category);
                if (channel == null)
                {
                    _logger.LogWarning(
                        "GitHub monitor could not resolve a text channel for {Repository} {Category} - skipping.",
                        repository.FullName,
                        category);
                    continue;
                }

                await PollCategoryAsync(db, repository, settings, category, channel, cancellationToken);

                if (IsBackingOff(out _))
                    return pollInterval;
            }
        }

        return pollInterval;
    }

    private IMessageChannel? ResolveChannel(GitHubTrackedRepository repository, GitHubMonitorSettings settings, GitHubEventCategory category)
    {
        var channelId = repository.GetCategoryChannelId(category);
        if (channelId == 0)
            channelId = repository.ChannelId;
        if (channelId == 0)
            channelId = settings.DefaultChannelId;
        if (channelId == 0)
            return null;

        return _client.GetChannel(channelId) as IMessageChannel;
    }

    private async Task PollCategoryAsync(
        HomotechsualBotContext db,
        GitHubTrackedRepository repository,
        GitHubMonitorSettings settings,
        GitHubEventCategory category,
        IMessageChannel channel,
        CancellationToken cancellationToken)
    {
        var feedType = $"GitHub:{category}";
        var sourceId = repository.FullName;

        var state = await db.FeedPostStates
            .FirstOrDefaultAsync(x => x.FeedType == feedType && x.SourceId == sourceId, cancellationToken);

        if (state == null)
        {
            state = new FeedPostState { FeedType = feedType, SourceId = sourceId };
            db.FeedPostStates.Add(state);
        }

        var items = await FetchItemsAsync(repository, category, cancellationToken);
        if (items == null)
            return;

        var isBaseline = string.IsNullOrWhiteSpace(state.LastPostedItemId) && state.LastCheckedAt == null;
        var lastPostedId = long.TryParse(state.LastPostedItemId, out var parsed) ? parsed : 0L;
        var lastCheckedAt = state.LastCheckedAt ?? DateTime.UtcNow;

        if (isBaseline)
        {
            state.LastPostedItemId = items.Count > 0 ? items.Max(x => x.Id).ToString() : "0";
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "GitHub monitor initialised {Repository} {Category} baseline at item {ItemId}.",
                repository.FullName,
                category,
                state.LastPostedItemId);
            return;
        }

        // Workflow runs are matched on completion time because a run's ID is assigned when it starts,
        // so runs can finish out of ID order.
        var pending = category == GitHubEventCategory.Actions
            ? items.Where(x => x.UpdatedAt > lastCheckedAt).OrderBy(x => x.UpdatedAt).ToList()
            : items.Where(x => x.Id > lastPostedId).OrderBy(x => x.Id).ToList();

        foreach (var item in pending)
        {
            try
            {
                await channel.SendMessageAsync(
                    text: BuildMention(settings),
                    embed: BuildEmbed(repository, category, item),
                    allowedMentions: settings.RoleId == 0 ? AllowedMentions.None : null);

                BotMetrics.GitHubNotificationsPosted.Inc();
                _logger.LogInformation(
                    "Posted GitHub {Category} item {ItemId} from {Repository} to channel {ChannelId}.",
                    category,
                    item.Id,
                    repository.FullName,
                    channel.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to post GitHub {Category} item {ItemId} from {Repository}.",
                    category,
                    item.Id,
                    repository.FullName);
                break;
            }

            if (item.Id > lastPostedId)
            {
                lastPostedId = item.Id;
                state.LastPostedItemId = lastPostedId.ToString();
            }

            state.LastCheckedAt = item.UpdatedAt > lastCheckedAt ? item.UpdatedAt : state.LastCheckedAt;
            await db.SaveChangesAsync(cancellationToken);
        }

        state.LastCheckedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<GitHubFeedItem>?> FetchItemsAsync(
        GitHubTrackedRepository repository,
        GitHubEventCategory category,
        CancellationToken cancellationToken)
    {
        var owner = Uri.EscapeDataString(repository.Owner);
        var name = Uri.EscapeDataString(repository.Name);

        var requestUri = category switch
        {
            GitHubEventCategory.Issues => $"repos/{owner}/{name}/issues?state=all&sort=created&direction=desc&per_page={PageSize}",
            GitHubEventCategory.PullRequests => $"repos/{owner}/{name}/pulls?state=all&sort=created&direction=desc&per_page={PageSize}",
            GitHubEventCategory.Actions => $"repos/{owner}/{name}/actions/runs?status=completed&per_page={PageSize}",
            GitHubEventCategory.Releases => $"repos/{owner}/{name}/releases?per_page={PageSize}",
            _ => null
        };

        if (requestUri == null)
            return null;

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
                    response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
                    remaining.FirstOrDefault() == "0")
                {
                    ApplyBackoff();
                    _logger.LogWarning(
                        "GitHub API rate limit exhausted while fetching {Category} for {Repository}. Backing off for {Minutes} minute(s).",
                        category,
                        repository.FullName,
                        RateLimitBackoffDuration.TotalMinutes);
                    return null;
                }

                _logger.LogWarning(
                    "GitHub API returned {StatusCode} while fetching {Category} for {Repository}.",
                    (int)response.StatusCode,
                    category,
                    repository.FullName);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return category switch
            {
                GitHubEventCategory.Issues => ParseIssues(document.RootElement),
                GitHubEventCategory.PullRequests => ParsePullRequests(document.RootElement),
                GitHubEventCategory.Actions => ParseWorkflowRuns(document.RootElement),
                GitHubEventCategory.Releases => ParseReleases(document.RootElement),
                _ => null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub {Category} for {Repository}.", category, repository.FullName);
            return null;
        }
    }

    private static List<GitHubFeedItem> ParseIssues(JsonElement root)
    {
        var items = new List<GitHubFeedItem>();
        if (root.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var element in root.EnumerateArray())
        {
            // The issues endpoint also returns pull requests; they are handled separately.
            if (element.TryGetProperty("pull_request", out _))
                continue;

            items.Add(new GitHubFeedItem
            {
                Id = GetInt64(element, "number"),
                Title = $"#{GetInt64(element, "number")} {GetString(element, "title")}",
                Url = GetString(element, "html_url"),
                Author = GetString(element.TryGetProperty("user", out var user) ? user : default, "login"),
                Status = GetString(element, "state"),
                Body = GetString(element, "body"),
                UpdatedAt = GetDateTime(element, "updated_at")
            });
        }

        return items;
    }

    private static List<GitHubFeedItem> ParsePullRequests(JsonElement root)
    {
        var items = new List<GitHubFeedItem>();
        if (root.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var element in root.EnumerateArray())
        {
            var isDraft = element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True;

            items.Add(new GitHubFeedItem
            {
                Id = GetInt64(element, "number"),
                Title = $"#{GetInt64(element, "number")} {GetString(element, "title")}",
                Url = GetString(element, "html_url"),
                Author = GetString(element.TryGetProperty("user", out var user) ? user : default, "login"),
                Status = isDraft ? "draft" : GetString(element, "state"),
                Body = GetString(element, "body"),
                UpdatedAt = GetDateTime(element, "updated_at")
            });
        }

        return items;
    }

    private static List<GitHubFeedItem> ParseWorkflowRuns(JsonElement root)
    {
        var items = new List<GitHubFeedItem>();
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("workflow_runs", out var runs) ||
            runs.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var element in runs.EnumerateArray())
        {
            items.Add(new GitHubFeedItem
            {
                Id = GetInt64(element, "id"),
                Title = $"{GetString(element, "name")} #{GetInt64(element, "run_number")}",
                Url = GetString(element, "html_url"),
                Author = GetString(element.TryGetProperty("actor", out var actor) ? actor : default, "login"),
                Status = GetString(element, "conclusion"),
                Body = GetString(element, "head_branch") is { Length: > 0 } branch ? $"Branch `{branch}`" : string.Empty,
                UpdatedAt = GetDateTime(element, "updated_at")
            });
        }

        return items;
    }

    private static List<GitHubFeedItem> ParseReleases(JsonElement root)
    {
        var items = new List<GitHubFeedItem>();
        if (root.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var element in root.EnumerateArray())
        {
            if (element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                continue;

            var title = GetString(element, "name");
            if (string.IsNullOrWhiteSpace(title))
                title = GetString(element, "tag_name");

            var isPrerelease = element.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;

            items.Add(new GitHubFeedItem
            {
                Id = GetInt64(element, "id"),
                Title = title,
                Url = GetString(element, "html_url"),
                Author = GetString(element.TryGetProperty("author", out var author) ? author : default, "login"),
                Status = isPrerelease ? "prerelease" : "release",
                Body = GetString(element, "body"),
                UpdatedAt = GetDateTime(element, "published_at")
            });
        }

        return items;
    }

    private static string BuildMention(GitHubMonitorSettings settings) =>
        settings.RoleId == 0 ? string.Empty : $"<@&{settings.RoleId}>";

    private static Embed BuildEmbed(GitHubTrackedRepository repository, GitHubEventCategory category, GitHubFeedItem item)
    {
        var builder = new EmbedBuilder()
            .WithTitle(Truncate(item.Title, 256))
            .WithUrl(string.IsNullOrWhiteSpace(item.Url) ? null : item.Url)
            .WithColor(GetColor(category, item.Status))
            .WithAuthor($"{repository.FullName} • {GetCategoryLabel(category)}")
            .WithTimestamp(new DateTimeOffset(DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc)));

        if (!string.IsNullOrWhiteSpace(item.Author))
            builder.AddField("By", item.Author, true);

        if (!string.IsNullOrWhiteSpace(item.Status))
            builder.AddField("Status", item.Status, true);

        if (!string.IsNullOrWhiteSpace(item.Body))
            builder.WithDescription(Truncate(item.Body, 500));

        return builder.Build();
    }

    private static string GetCategoryLabel(GitHubEventCategory category) => category switch
    {
        GitHubEventCategory.Issues => "Issue",
        GitHubEventCategory.PullRequests => "Pull request",
        GitHubEventCategory.Actions => "Workflow run",
        GitHubEventCategory.Releases => "Release",
        _ => category.ToString()
    };

    private static Color GetColor(GitHubEventCategory category, string status) => category switch
    {
        GitHubEventCategory.Actions => status switch
        {
            "success" => Color.Green,
            "failure" or "timed_out" => Color.Red,
            "cancelled" => Color.LightGrey,
            _ => Color.Orange
        },
        GitHubEventCategory.Releases => Color.Purple,
        GitHubEventCategory.PullRequests => Color.Teal,
        _ => Color.Blue
    };

    private async Task EnsureSeededAsync(HomotechsualBotContext db, CancellationToken cancellationToken)
    {
        var changed = false;
        var githubConfig = _config.GitHubMonitor;

        if (!await db.GitHubMonitorSettings.AnyAsync(cancellationToken))
        {
            db.GitHubMonitorSettings.Add(new GitHubMonitorSettings
            {
                Enabled = githubConfig.Enabled,
                DefaultChannelId = githubConfig.DefaultChannelId,
                RoleId = githubConfig.RoleId,
                PollIntervalMinutes = githubConfig.PollIntervalMinutes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            changed = true;
        }

        if (!await db.GitHubTrackedRepositories.AnyAsync(cancellationToken) && githubConfig.Repositories.Count > 0)
        {
            foreach (var repository in githubConfig.Repositories)
            {
                if (string.IsNullOrWhiteSpace(repository.Owner) || string.IsNullOrWhiteSpace(repository.Name))
                    continue;

                db.GitHubTrackedRepositories.Add(new GitHubTrackedRepository
                {
                    Owner = repository.Owner.Trim(),
                    Name = repository.Name.Trim(),
                    ChannelId = repository.ChannelId,
                    IssuesEnabled = repository.Issues,
                    IssuesChannelId = repository.IssuesChannelId,
                    PullRequestsEnabled = repository.PullRequests,
                    PullRequestsChannelId = repository.PullRequestsChannelId,
                    ActionsEnabled = repository.Actions,
                    ActionsChannelId = repository.ActionsChannelId,
                    ReleasesEnabled = repository.Releases,
                    ReleasesChannelId = repository.ReleasesChannelId,
                    IsEnabled = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static int GetEffectivePollIntervalMinutes(int configuredInterval) =>
        configuredInterval <= 0 ? MinimumPollIntervalMinutes : Math.Max(configuredInterval, MinimumPollIntervalMinutes);

    private void ApplyBackoff()
    {
        lock (_backoffLock)
        {
            _backoffUntilUtc = DateTime.UtcNow.Add(RateLimitBackoffDuration);
        }

        BotMetrics.GitHubApiBackoffActive.Set(1);
    }

    private bool IsBackingOff(out DateTime backoffUntilUtc)
    {
        lock (_backoffLock)
        {
            backoffUntilUtc = _backoffUntilUtc;
        }

        var active = backoffUntilUtc > DateTime.UtcNow;
        BotMetrics.GitHubApiBackoffActive.Set(active ? 1 : 0);
        return active;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long GetInt64(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var parsed)
            ? parsed
            : 0L;

    private static DateTime GetDateTime(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(value.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTime.UtcNow;

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        var truncated = value[..(maxLength - 1)];
        if (char.IsHighSurrogate(truncated[^1]))
            truncated = truncated[..^1];

        return truncated + "…";
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class GitHubFeedItem
    {
        public long Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    }
}
