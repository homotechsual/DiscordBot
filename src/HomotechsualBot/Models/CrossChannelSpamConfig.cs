namespace DiscordBot.Models;

public class CrossChannelSpamConfig
{
    public bool Enabled { get; set; }
    public int TimeWindowSeconds { get; set; } = 30;
    public int MinimumChannelCount { get; set; } = 3;
}
