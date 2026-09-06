namespace DiscordBot.Models;

/// <summary>
/// Seed configuration for the GitHub monitor. Values here are only used to
/// populate the runtime database settings the first time the bot starts; after
/// that the <c>/github</c> slash commands are the source of truth.
/// </summary>
public class GitHubMonitorConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>Fallback channel used by repositories that do not specify their own channel.</summary>
    public ulong DefaultChannelId { get; set; }

    /// <summary>Optional role mentioned when a notification is posted.</summary>
    public ulong RoleId { get; set; }

    /// <summary>Optional personal access token. Without one the GitHub API allows only 60 requests/hour.</summary>
    public string? Token { get; set; }

    public int PollIntervalMinutes { get; set; } = 10;

    public List<GitHubRepositoryConfig> Repositories { get; set; } = new();

    /// <summary>
    /// Validates the GitHub monitor configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (!Enabled)
            return;

        if (PollIntervalMinutes <= 0)
            throw new InvalidOperationException($"GitHubMonitorConfig: PollIntervalMinutes must be greater than 0. Current value: {PollIntervalMinutes}.");

        foreach (var repository in Repositories)
        {
            if (string.IsNullOrWhiteSpace(repository.Owner) || string.IsNullOrWhiteSpace(repository.Name))
                throw new InvalidOperationException("GitHubMonitorConfig: Every configured repository must set both Owner and Name. Check HOMOTECHSUALBOT_Bot__GitHubMonitor__Repositories__<index>__Owner/Name.");
        }
    }
}

public class GitHubRepositoryConfig
{
    public string Owner { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Channel for this repository. Falls back to <see cref="GitHubMonitorConfig.DefaultChannelId"/> when zero.</summary>
    public ulong ChannelId { get; set; }

    public bool Issues { get; set; } = true;

    public ulong IssuesChannelId { get; set; }

    public bool PullRequests { get; set; } = true;

    public ulong PullRequestsChannelId { get; set; }

    public bool Actions { get; set; } = false;

    public ulong ActionsChannelId { get; set; }

    public bool Releases { get; set; } = true;

    public ulong ReleasesChannelId { get; set; }
}
