using Discord;
using Discord.Interactions;
using DiscordBot.Models;
using System.Collections.Concurrent;
using System.Text;

namespace DiscordBot.Attributes;

/// <summary>
/// Adds a per-user cooldown for a command to reduce spam.
/// </summary>
public class CooldownAttribute : PreconditionAttribute
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> Cooldowns = new();
    private readonly int _defaultSeconds;

    /// <summary>
    /// Creates a cooldown period in seconds.
    /// </summary>
    public CooldownAttribute(int seconds)
    {
        if (seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Cooldown duration must be greater than zero.");

        _defaultSeconds = seconds;
    }

    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services)
    {
        var now = DateTimeOffset.UtcNow;
        var commandKey = commandInfo.Name ?? "unknown";
        var key = $"{context.User.Id}:{commandKey}"; // per-user: each user has their own independent cooldown
        var duration = TimeSpan.FromSeconds(ResolveCooldownSeconds(commandKey, services));

        if (Cooldowns.TryGetValue(key, out var expiresAt) && expiresAt > now)
        {
            var remaining = expiresAt - now;
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            return Task.FromResult(PreconditionResult.FromError(
                $"You're using this command too quickly. Try again in {remainingSeconds}s."));
        }

        Cooldowns[key] = now.Add(duration);
        return Task.FromResult(PreconditionResult.FromSuccess());
    }

    private int ResolveCooldownSeconds(string commandKey, IServiceProvider services)
    {
        var config = services.GetService(typeof(BotConfig)) as BotConfig;
        if (config?.Cooldowns is null || config.Cooldowns.Count == 0)
            return _defaultSeconds;

        var normalizedCommand = NormalizeKey(commandKey);
        foreach (var entry in config.Cooldowns)
        {
            if (entry.Value <= 0)
                continue;

            if (NormalizeKey(entry.Key).Equals(normalizedCommand, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return _defaultSeconds;
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
