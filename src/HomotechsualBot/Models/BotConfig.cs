namespace DiscordBot.Models;

public class BotConfig
{
    public string Token { get; set; } = string.Empty;
    public string Prefix { get; set; } = "@";
    public ulong? GuildId { get; set; }
    public List<ulong> AllowedFunChannels { get; set; } = new List<ulong>();
    public Dictionary<string, int> Cooldowns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public StatusMonitorConfig StatusMonitor { get; set; } = new();
    public YoutubeMonitorConfig YoutubeMonitor { get; set; } = new();
    public HeartbeatConfig Heartbeat { get; set; } = new();

    /// <summary>
    /// Validates the bot configuration for required values and correct types.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Token))
            throw new InvalidOperationException("BotConfig: Token is required and cannot be empty.");

        if (string.IsNullOrWhiteSpace(Prefix))
            throw new InvalidOperationException("BotConfig: Prefix is required and cannot be empty.");

        StatusMonitor.Validate();
        YoutubeMonitor.Validate();
        Heartbeat.Validate();
    }
}
