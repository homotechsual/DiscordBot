namespace DiscordBot.Models;

public class ModLogThreadLink
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public ulong ThreadId { get; set; }
    public bool TitleConfirmed { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}
