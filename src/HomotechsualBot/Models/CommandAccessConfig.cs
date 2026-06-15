namespace DiscordBot.Models;

public class CommandAccessConfig
{
    public bool DisableAllFunCommands { get; set; }
    public List<string> FunCommands { get; set; } = [];
    public List<string> DisabledCommands { get; set; } = [];
    public Dictionary<string, List<ulong>> RestrictedChannels { get; set; } = new();
}
