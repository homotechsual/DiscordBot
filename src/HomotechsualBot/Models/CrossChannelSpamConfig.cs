namespace DiscordBot.Models;

public class CrossChannelSpamConfig
{
    public bool Enabled { get; set; }
    public int TimeWindowSeconds { get; set; } = 30;
    public int MinimumChannelCount { get; set; } = 3;
    /// <summary>Delete detected spam messages. Requires Manage Messages permission. Default: true.</summary>
    public bool DeleteMessages { get; set; } = true;
    /// <summary>Apply a 28-day timeout to the spammer. Requires Moderate Members permission. Default: true.</summary>
    public bool TimeoutOnDetection { get; set; } = true;
}
