namespace DiscordBot.Models;

public class AllCapsModerationConfig
{
    public bool Enabled { get; set; } = true;
    public bool DeleteMessage { get; set; } = true;
    public int MinLetters { get; set; } = 8;
    public double MinUppercaseRatio { get; set; } = 0.7;
    public int WarningDurationSeconds { get; set; } = 10;
}
