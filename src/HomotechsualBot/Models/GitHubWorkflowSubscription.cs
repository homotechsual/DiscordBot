namespace DiscordBot.Models;

public class GitHubWorkflowSubscription
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public string Workflow { get; set; } = string.Empty;
    public ulong ChannelId { get; set; }

    public static bool IsValidWorkflow(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 255 &&
        value == value.Trim() && !value.Contains('/') && !value.Contains('\\') &&
        (long.TryParse(value, out var id) && id > 0 ||
         value.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
         value.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
}
