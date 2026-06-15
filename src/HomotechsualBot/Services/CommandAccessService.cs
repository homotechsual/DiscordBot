using DiscordBot.Models;

namespace DiscordBot.Services;

public class CommandAccessService
{
    private static readonly string[] DefaultFunCommands = ["meme", "8ball", "roll", "joke", "say"];

    private readonly bool _disableAllFunCommands;
    private readonly HashSet<string> _funCommands;
    private readonly HashSet<string> _disabledCommands;
    private readonly Dictionary<string, HashSet<ulong>> _restrictedChannels;

    public CommandAccessService(CommandAccessConfig config)
    {
        _disableAllFunCommands = config.DisableAllFunCommands;
        var configuredFunCommands = config.FunCommands?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(Normalize) ?? [];

        _funCommands = configuredFunCommands.Any()
            ? configuredFunCommands.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : DefaultFunCommands.ToHashSet(StringComparer.OrdinalIgnoreCase);

        _disabledCommands = (config.DisabledCommands ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _restrictedChannels = (config.RestrictedChannels ?? new Dictionary<string, List<ulong>>())
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value is { Count: > 0 })
            .ToDictionary(
                kvp => Normalize(kvp.Key),
                kvp => kvp.Value.ToHashSet(),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetBlockReason(string commandName, ulong channelId, out string reason)
    {
        var normalized = Normalize(commandName);

        if (_disableAllFunCommands && _funCommands.Contains(normalized))
        {
            reason = "This fun command is currently disabled on this server.";
            return true;
        }

        if (_disabledCommands.Contains(normalized))
        {
            reason = "This command is currently disabled on this server.";
            return true;
        }

        if (_restrictedChannels.TryGetValue(normalized, out var allowedChannels) && !allowedChannels.Contains(channelId))
        {
            var channelList = string.Join(", ", allowedChannels.Select(id => $"<#{id}>"));
            reason = $"This command can only be used in: {channelList}";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
