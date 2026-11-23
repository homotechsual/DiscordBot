namespace DiscordBot.Models;

public class GitHubIssueSubscription
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public ulong ChannelId { get; set; }
    public string? IncludeLabels { get; set; }
    public string? ExcludeLabels { get; set; }

    public IReadOnlyList<string> GetIncludedLabels() => ParseLabels(IncludeLabels);
    public IReadOnlyList<string> GetExcludedLabels() => ParseLabels(ExcludeLabels);

    private static IReadOnlyList<string> ParseLabels(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}
