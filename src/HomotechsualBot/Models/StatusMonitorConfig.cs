namespace DiscordBot.Models;

public class StatusMonitorConfig
{
    /// <summary>
    /// Whether the status monitor is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The Discord channel ID to post status updates to.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Optional Discord role ID to mention on new status updates.
    /// </summary>
    public ulong RoleId { get; set; }

    /// <summary>
    /// The RSS feed URL to poll.
    /// </summary>
    public string FeedUrl { get; set; } = "https://status.haloservicesolutions.com/pages/63ef45da7ee94905308a1a4a/rss";

    /// <summary>
    /// How often (in minutes) to poll the feed. Defaults to 5.
    /// </summary>
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Validates the status monitor configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (Enabled)
        {
            if (ChannelId == 0)
                throw new InvalidOperationException("StatusMonitorConfig: When enabled, ChannelId must be a valid Discord channel ID (non-zero). Current value: 0. Check HOMOTECHSUALBOT_Bot__StatusMonitor__ChannelId environment variable.");

            if (string.IsNullOrWhiteSpace(FeedUrl) || !Uri.TryCreate(FeedUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException($"StatusMonitorConfig: FeedUrl must be a valid URL. Current value: '{FeedUrl}'.");

            if (PollIntervalMinutes <= 0)
                throw new InvalidOperationException($"StatusMonitorConfig: PollIntervalMinutes must be greater than 0. Current value: {PollIntervalMinutes}.");
        }
    }
}

