namespace DiscordBot.Models;

public class SingleMessageRecord
{
    public int Id { get; set; }
    public ulong ChannelId { get; set; }
    public ulong UserId { get; set; }
    public ulong MessageId { get; set; }
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;

    public SingleMessageChannelState? Channel { get; set; }
}
