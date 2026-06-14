namespace DiscordBot.Models;

public class YoutubeMonitorSettings
{
    public int Id { get; set; }

    public bool Enabled { get; set; } = false;

    public ulong ForumChannelId { get; set; }

    public int PollIntervalMinutes { get; set; } = 15;

    public string DefaultPostTitleTemplate { get; set; } = "[{ChannelName}] {VideoTitle}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
