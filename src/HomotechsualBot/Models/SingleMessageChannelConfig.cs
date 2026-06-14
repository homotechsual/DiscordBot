namespace DiscordBot.Models;

public class SingleMessageChannelConfig
{
    public ulong ChannelId { get; set; }
    public bool ScanHistoryOnEnable { get; set; } = false;
}
