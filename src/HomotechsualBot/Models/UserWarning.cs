namespace DiscordBot.Models;

public class UserWarning
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public WarningSource Source { get; set; }
    public ulong? ModeratorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
