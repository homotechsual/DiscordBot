namespace DiscordBot.Models;

public class AllCapsModerationConfig
{
    public bool Enabled { get; set; } = true;
    public bool DeleteMessage { get; set; } = true;
    public int MinLetters { get; set; } = 8;
    public double MinUppercaseRatio { get; set; } = 0.8;
    public bool EnableLengthScaling { get; set; } = true;
    public double UppercaseRatioDropPerLetter { get; set; } = 0.01;
    public double MinScaledUppercaseRatio { get; set; } = 0.4;
    public int WarningDurationSeconds { get; set; } = 10;
}
