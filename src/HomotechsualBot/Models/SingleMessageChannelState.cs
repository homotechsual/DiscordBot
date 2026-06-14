namespace DiscordBot.Models;

public class SingleMessageChannelState
{
    public ulong ChannelId { get; set; }
    public bool IsEnabled { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
