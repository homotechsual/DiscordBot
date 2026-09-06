namespace DiscordBot.Models;

/// <summary>
/// Runtime-editable GitHub monitor settings (single row).
/// </summary>
public class GitHubMonitorSettings
{
    public int Id { get; set; }

    public bool Enabled { get; set; } = false;

    /// <summary>Fallback channel used by repositories that do not specify their own channel.</summary>
    public ulong DefaultChannelId { get; set; }

    public ulong RoleId { get; set; }

    public int PollIntervalMinutes { get; set; } = 10;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
