namespace DiscordBot.Models;

public class YoutubeTrackedChannel
{
    public int Id { get; set; }

    public string ChannelId { get; set; } = string.Empty;

    public string ChannelName { get; set; } = string.Empty;

    public string? PostTitleTemplate { get; set; }

    /// <summary>
    /// Semicolon-separated list of keywords to filter videos by title (case-insensitive).
    /// If provided, only videos with titles containing at least one keyword will be posted.
    /// </summary>
    public string? KeywordFilters { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
