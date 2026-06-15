using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public sealed class CrossChannelSpamDetector : IDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly CrossChannelSpamConfig _config;
    private readonly ModerationLogService _logService;
    private readonly ILogger<CrossChannelSpamDetector> _logger;
    private readonly ConcurrentDictionary<ulong, List<SpamCandidate>> _candidates = new();
    private readonly object _lock = new();
    private readonly Timer _cleanupTimer;

    public CrossChannelSpamDetector(
        DiscordSocketClient client,
        CrossChannelSpamConfig config,
        ModerationLogService logService,
        ILogger<CrossChannelSpamDetector> logger)
    {
        _client = client;
        _config = config;
        _logService = logService;
        _logger = logger;
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task HandleMessageAsync(SocketMessage rawMessage)
    {
        if (!_config.Enabled) return;
        if (rawMessage is not SocketUserMessage message) return;
        if (message.Author.IsBot) return;
        if (message.Channel is not SocketTextChannel channel) return;

        var fingerprint = ComputeFingerprint(message.Content ?? string.Empty,
            message.Attachments.Select(a => a.Filename).ToArray());
        if (string.IsNullOrEmpty(fingerprint)) return;

        var userId = message.Author.Id;
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromSeconds(_config.TimeWindowSeconds);
        var candidate = new SpamCandidate(channel.Id, fingerprint, now, message.Id);

        List<SpamCandidate>? burst = null;

        lock (_lock)
        {
            var list = _candidates.GetOrAdd(userId, _ => []);
            list.RemoveAll(c => now - c.Timestamp > window);

            var matching = list
                .Where(c => c.Fingerprint == fingerprint && c.ChannelId != channel.Id)
                .ToList();

            if (matching.Count + 1 < _config.MinimumChannelCount)
            {
                list.Add(candidate);
            }
            else
            {
                burst = [.. matching, candidate];
                _candidates.TryRemove(userId, out _);
            }
        }

        if (burst is not null)
            await EnforceAsync(message, channel, burst);
    }

    private async Task EnforceAsync(
        SocketUserMessage triggeringMessage,
        SocketTextChannel triggeringChannel,
        List<SpamCandidate> burst)
    {
        var guild = triggeringChannel.Guild;
        var userId = triggeringMessage.Author.Id;

        var deleted = new List<(ulong ChannelId, ulong MessageId)>();
        foreach (var c in burst)
        {
            try
            {
                var chan = guild.GetTextChannel(c.ChannelId);
                if (chan is not null)
                {
                    await chan.DeleteMessageAsync(c.MessageId);
                    deleted.Add((c.ChannelId, c.MessageId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Spam: could not delete message {MsgId} in {ChanId}", c.MessageId, c.ChannelId);
            }
        }

        var guildUser = guild.GetUser(userId);
        if (guildUser is not null)
        {
            try
            {
                await guildUser.SetTimeOutAsync(TimeSpan.FromDays(28), new RequestOptions
                {
                    AuditLogReason = "Automated: cross-channel spam detected"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Spam: failed to apply timeout to user {UserId}", userId);
            }
        }
        else
        {
            _logger.LogWarning("Spam: user {UserId} not in guild cache; timeout not applied", userId);
        }

        var channels = burst
            .Select(c => guild.GetTextChannel(c.ChannelId))
            .OfType<ITextChannel>()
            .Distinct()
            .ToList();

        await _logService.LogSpamDetectedAsync(triggeringMessage.Author, channels, burst[0].Fingerprint, deleted);
    }

    public static string ComputeFingerprint(string content, string[] attachmentFilenames)
    {
        if (string.IsNullOrEmpty(content) && attachmentFilenames.Length == 0)
            return string.Empty;

        var sorted = attachmentFilenames.OrderBy(f => f);
        return $"{content}|{string.Join(",", sorted)}";
    }

    private void Cleanup()
    {
        var window = TimeSpan.FromSeconds(_config.TimeWindowSeconds);
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _candidates.Keys.ToArray())
        {
            if (!_candidates.TryGetValue(key, out var list)) continue;
            lock (_lock)
            {
                list.RemoveAll(c => now - c.Timestamp > window);
                if (list.Count == 0)
                    _candidates.TryRemove(key, out _);
            }
        }
    }

    public SpamDetectionStatus GetStatus() =>
        new(_config.Enabled, _config.TimeWindowSeconds, _config.MinimumChannelCount);

    public SpamSimulationResult Simulate(string content, string[] attachmentFilenames)
    {
        var fingerprint = ComputeFingerprint(content, attachmentFilenames);
        return new SpamSimulationResult(fingerprint, _config.Enabled, _config.MinimumChannelCount, _config.TimeWindowSeconds);
    }

    public void Dispose() => _cleanupTimer.Dispose();
}

internal record SpamCandidate(ulong ChannelId, string Fingerprint, DateTimeOffset Timestamp, ulong MessageId);

public record SpamDetectionStatus(bool Enabled, int TimeWindowSeconds, int MinimumChannelCount);

public record SpamSimulationResult(string Fingerprint, bool DetectionEnabled, int MinimumChannelCount, int TimeWindowSeconds)
{
    public bool WouldBeTracked => !string.IsNullOrEmpty(Fingerprint);
}
