namespace DiscordBot.Models;

/// <summary>
/// A repository watched by the GitHub monitor. Each event category can be
/// toggled independently and optionally routed to its own channel.
/// </summary>
public class GitHubTrackedRepository
{
    public int Id { get; set; }

    public string Owner { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Repository-level channel. Falls back to <see cref="GitHubMonitorSettings.DefaultChannelId"/> when zero.</summary>
    public ulong ChannelId { get; set; }

    public bool IssuesEnabled { get; set; } = true;

    public ulong IssuesChannelId { get; set; }

    public bool PullRequestsEnabled { get; set; } = true;

    public ulong PullRequestsChannelId { get; set; }

    public bool ActionsEnabled { get; set; } = false;

    public ulong ActionsChannelId { get; set; }

    public bool ReleasesEnabled { get; set; } = true;

    public ulong ReleasesChannelId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string FullName => $"{Owner}/{Name}";

    public bool IsCategoryEnabled(GitHubEventCategory category) => category switch
    {
        GitHubEventCategory.Issues => IssuesEnabled,
        GitHubEventCategory.PullRequests => PullRequestsEnabled,
        GitHubEventCategory.Actions => ActionsEnabled,
        GitHubEventCategory.Releases => ReleasesEnabled,
        _ => false
    };

    public ulong GetCategoryChannelId(GitHubEventCategory category) => category switch
    {
        GitHubEventCategory.Issues => IssuesChannelId,
        GitHubEventCategory.PullRequests => PullRequestsChannelId,
        GitHubEventCategory.Actions => ActionsChannelId,
        GitHubEventCategory.Releases => ReleasesChannelId,
        _ => 0
    };
}
