namespace DiscordBot.Models;

public class FeedPostState
{
    public int Id { get; set; }

    public string FeedType { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string? LastPostedItemId { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
