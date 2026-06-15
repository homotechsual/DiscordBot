namespace DiscordBot.Models;

public class ModerationExemptionsConfig
{
    public List<ulong> ExemptUserIds { get; set; } = [];
    public List<ulong> ExemptRoleIds { get; set; } = [];
}
