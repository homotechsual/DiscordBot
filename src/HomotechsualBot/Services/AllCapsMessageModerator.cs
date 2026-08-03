using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Core.Data;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public sealed partial class AllCapsMessageModerator
{
    private const string WarningText = "please avoid typing in all caps.";

    private readonly AllCapsModerationConfig _config;
    private readonly AcronymAllowlistService _acronymAllowlistService;
    private readonly ModerationExemptionService _exemptionService;
    private readonly ModerationLogService _logService;
    private readonly WarningService _warningService;
    private readonly ILogger<AllCapsMessageModerator> _logger;

    public AllCapsMessageModerator(
        AllCapsModerationConfig config,
        AcronymAllowlistService acronymAllowlistService,
        ModerationExemptionService exemptionService,
        ModerationLogService logService,
        WarningService warningService,
        ILogger<AllCapsMessageModerator> logger)
    {
        _config = config;
        _acronymAllowlistService = acronymAllowlistService;
        _exemptionService = exemptionService;
        _logService = logService;
        _warningService = warningService;
        _logger = logger;
    }

    public async Task HandleMessageAsync(SocketMessage rawMessage)
    {
        if (!_config.Enabled) return;
        if (rawMessage is not SocketUserMessage message) return;
        if (message.Author.IsBot) return;
        if (message.Channel is not SocketTextChannel channel) return;
        if (_exemptionService.IsExempt(message.Author)) return;

        var guildUser = channel.Guild.GetUser(message.Author.Id);
        if (_exemptionService.IsExempt(guildUser)) return;

        if (await _acronymAllowlistService.IsAllowlistedMessageAsync(message.Content ?? string.Empty)) return;

        var evaluation = Evaluate(message.Content ?? string.Empty, _config);
        if (!evaluation.ShouldTrigger) return;

        if (_config.DeleteMessage)
        {
            try
            {
                await message.DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "AllCaps: could not delete message {MessageId}", message.Id);
            }
        }

        var warningMessage = $"{message.Author.Mention} {WarningText}";

        try
        {
            var sent = await channel.SendMessageAsync(warningMessage);
            ScheduleSelfDelete(sent, _config.WarningDurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AllCaps: could not post channel warning for {UserId}", message.Author.Id);
        }

        try
        {
            var dm = await message.Author.CreateDMChannelAsync();
            await dm.SendMessageAsync(warningMessage);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AllCaps: could not DM warning to {UserId}", message.Author.Id);
        }

        try
        {
            await _warningService.AddWarningAsync(
                channel.Guild.Id, message.Author.Id, "All-caps message", WarningSource.AutoAllCaps, null, guildUser);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AllCaps: failed to record warning for {UserId} in guild {GuildId}", message.Author.Id, channel.Guild.Id);
        }

        await _logService.LogActionAsync(ModerationLogEntry.CreateAutomated(
            ModerationActionType.AutoWarnAllCaps, message.Author, Truncate(message.Content ?? string.Empty, 1000)));
    }

    private void ScheduleSelfDelete(IUserMessage message, int delaySeconds)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                await message.DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "AllCaps: could not self-delete warning message {MessageId}", message.Id);
            }
        });
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    public static AllCapsEvaluation Evaluate(string content, AllCapsModerationConfig config)
    {
        var stripped = CodeBlockPattern().Replace(content, " ");
        stripped = UrlPattern().Replace(stripped, " ");
        stripped = MentionPattern().Replace(stripped, " ");
        stripped = EmojiPattern().Replace(stripped, " ");

        var letterCount = 0;
        var uppercaseCount = 0;
        foreach (var c in stripped)
        {
            if (!char.IsLetter(c)) continue;
            letterCount++;
            if (char.IsUpper(c)) uppercaseCount++;
        }

        var uppercaseRatio = letterCount == 0 ? 0 : uppercaseCount / (double)letterCount;
        var shouldTrigger = letterCount >= config.MinLetters &&
            uppercaseRatio >= CalculateRequiredRatio(letterCount, config);

        return new AllCapsEvaluation(shouldTrigger, letterCount, uppercaseCount);
    }

    public static AllCapsEvaluation Evaluate(string content, int minLetters, double minUppercaseRatio) =>
        Evaluate(content, new AllCapsModerationConfig
        {
            MinLetters = minLetters,
            MinUppercaseRatio = minUppercaseRatio,
            EnableLengthScaling = false
        });

    private static double CalculateRequiredRatio(int letterCount, AllCapsModerationConfig config)
    {
        var startRatio = Math.Clamp(config.MinUppercaseRatio, 0, 1);
        if (!config.EnableLengthScaling) return startRatio;

        var floorRatio = Math.Clamp(config.MinScaledUppercaseRatio, 0, startRatio);
        var dropPerLetter = Math.Max(0, config.UppercaseRatioDropPerLetter);
        var extraLetters = Math.Max(0, letterCount - config.MinLetters);
        var scaledRatio = startRatio - extraLetters * dropPerLetter;

        return Math.Clamp(scaledRatio, floorRatio, startRatio);
    }

    [GeneratedRegex(@"```[\s\S]*?```|`[^`]*`")]
    private static partial Regex CodeBlockPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"<@!?\d+>|<#\d+>|<@&\d+>")]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"<a?:\w+:\d+>")]
    private static partial Regex EmojiPattern();
}

public readonly record struct AllCapsEvaluation(bool ShouldTrigger, int LetterCount, int UppercaseCount);
