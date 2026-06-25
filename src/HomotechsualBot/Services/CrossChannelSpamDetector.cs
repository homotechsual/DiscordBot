using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace DiscordBot.Services;

public sealed class CrossChannelSpamDetector : IDisposable
{
    private const string SelfTestPrefix = "[SPAM-SELF-TEST:";

    private readonly DiscordSocketClient _client;
    private readonly CrossChannelSpamConfig _config;
    private readonly ModerationExemptionService _exemptionService;
    private readonly ModerationLogService _logService;
    private readonly ILogger<CrossChannelSpamDetector> _logger;
    private readonly ConcurrentDictionary<ulong, List<SpamCandidate>> _candidates = new();
    private readonly object _lock = new();
    private readonly Timer _cleanupTimer;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<List<SpamCandidate>>> _pendingLiveTests = new();
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _confirmedSpammers = new();

    public CrossChannelSpamDetector(
        DiscordSocketClient client,
        CrossChannelSpamConfig config,
        ModerationExemptionService exemptionService,
        ModerationLogService logService,
        ILogger<CrossChannelSpamDetector> logger)
    {
        _client = client;
        _config = config;
        _exemptionService = exemptionService;
        _logService = logService;
        _logger = logger;
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task HandleMessageAsync(SocketMessage rawMessage)
    {
        if (!_config.Enabled)
        {
            _logger.LogDebug("Spam detector skipped message because detection is disabled");
            return;
        }

        if (rawMessage is not SocketUserMessage message) return;

        var isSelfTest = IsSelfTestMessage(message);
        if (message.Author.IsBot && !isSelfTest)
        {
            _logger.LogDebug("Spam detector skipped bot message {MessageId}", message.Id);
            return;
        }

        if (message.Channel is not SocketTextChannel channel) return;

        if (!isSelfTest && _exemptionService.IsExempt(message.Author))
        {
            _logger.LogDebug("Spam detector skipped exempt author {UserId}", message.Author.Id);
            return;
        }

        var guildUser = channel.Guild.GetUser(message.Author.Id);
        if (!isSelfTest && _exemptionService.IsExempt(guildUser))
        {
            _logger.LogDebug("Spam detector skipped exempt guild user {UserId}", message.Author.Id);
            return;
        }

        if (!isSelfTest &&
            _confirmedSpammers.TryGetValue(message.Author.Id, out var confirmedAt) &&
            DateTimeOffset.UtcNow - confirmedAt < TimeSpan.FromSeconds(60))
        {
            if (_config.DeleteMessages)
            {
                try
                {
                    await message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Spam: could not delete follow-on message {MessageId} from confirmed spammer {UserId}", message.Id, message.Author.Id);
                }
            }
            return;
        }

        var fingerprint = ComputeFingerprint(
            message.Content ?? string.Empty,
            message.Attachments.Select(AttachmentInfo.FromDiscord));
        if (string.IsNullOrEmpty(fingerprint))
        {
            _logger.LogDebug("Spam detector skipped empty fingerprint for message {MessageId}", message.Id);
            return;
        }

        var burst = TrackCandidate(message.Author.Id, channel.Id, message.Id, fingerprint, DateTimeOffset.UtcNow);

        _logger.LogDebug(
            "Spam detector tracked message {MessageId} in channel {ChannelId} for user {UserId}; Fingerprint={Fingerprint}; Matched={Matched}",
            message.Id,
            channel.Id,
            message.Author.Id,
            fingerprint,
            burst is not null);

        if (burst is not null)
        {
            if (isSelfTest)
            {
                _logger.LogInformation(
                    "Spam self-test detected burst for user {UserId} across {ChannelCount} channels within {Window}s",
                    message.Author.Id,
                    burst.Select(c => c.ChannelId).Distinct().Count(),
                    _config.TimeWindowSeconds);
                var nonce = ExtractSelfTestNonce(message.Content ?? string.Empty);
                if (nonce is not null && _pendingLiveTests.TryGetValue(nonce, out var tcs))
                {
                    tcs.TrySetResult(burst);
                }
                return;
            }

            _confirmedSpammers[message.Author.Id] = DateTimeOffset.UtcNow;
            await EnforceAsync(message, channel, burst);
        }
    }

    public async Task<SpamLiveTestResult> RunLiveSelfTestAsync(
        SocketGuild guild,
        IReadOnlyList<ITextChannel> channels,
        string content,
        IAttachment? attachment = null)
    {
        if (!_config.Enabled)
        {
            return new SpamLiveTestResult(false, "Cross-channel spam detection is disabled.", string.Empty, 0, channels.Count, [], 0);
        }

        if (channels.Count < 2)
        {
            return new SpamLiveTestResult(false, "Choose at least 2 channels for a meaningful test.", string.Empty, 0, channels.Count, [], 0);
        }

        var nonce = Guid.NewGuid().ToString("N")[..8];
        var payload = $"{SelfTestPrefix}{nonce}] {content}";
        var posted = new List<ulong>();
        var postedMessages = new List<(ulong ChannelId, ulong MessageId)>();
        byte[]? attachmentBytes = null;
        var attachmentName = attachment?.Filename;
        string fingerprint = string.Empty;

        var detectionTcs = new TaskCompletionSource<List<SpamCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLiveTests[nonce] = detectionTcs;

        if (attachment is not null)
        {
            try
            {
                using var httpClient = new HttpClient();
                attachmentBytes = await httpClient.GetByteArrayAsync(attachment.Url);
            }
            catch (Exception ex)
            {
                _pendingLiveTests.TryRemove(nonce, out _);
                _logger.LogWarning(ex, "Spam live self-test failed to download attachment from {Url}", attachment.Url);
                return new SpamLiveTestResult(false, "Failed to download test attachment. Try uploading again.", string.Empty, 0, 0, [], 0);
            }
        }

        foreach (var channel in channels)
        {
            IUserMessage sent;
            if (attachmentBytes is null)
            {
                sent = await channel.SendMessageAsync(payload);
            }
            else
            {
                await using var stream = new MemoryStream(attachmentBytes, writable: false);
                sent = await channel.SendFileAsync(stream, attachmentName ?? "spam-test.bin", text: payload);
            }

            posted.Add(channel.Id);
            postedMessages.Add((channel.Id, sent.Id));
            fingerprint = ComputeFingerprint(
                sent.Content ?? string.Empty,
                sent.Attachments.Select(AttachmentInfo.FromDiscord));
        }

        await Task.WhenAny(detectionTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        var detectedBurst = detectionTcs.Task.IsCompleted ? await detectionTcs.Task : null;
        _pendingLiveTests.TryRemove(nonce, out _);

        var cleanupErrors = 0;
        foreach (var postedMessage in postedMessages)
        {
            try
            {
                var cleanupChannel = guild.GetTextChannel(postedMessage.ChannelId);
                if (cleanupChannel is not null)
                {
                    await cleanupChannel.DeleteMessageAsync(postedMessage.MessageId);
                }
            }
            catch (Exception ex)
            {
                cleanupErrors++;
                _logger.LogDebug(ex, "Spam self-test cleanup failed for message {MessageId} in channel {ChannelId}", postedMessage.MessageId, postedMessage.ChannelId);
            }
        }

        var detected = detectedBurst is not null;
        var matchedChannels = detectedBurst?.Select(c => c.ChannelId).Distinct().Count() ?? 0;
        var message = detected
            ? $"Detected after posting to {posted.Count} channels."
            : $"Not detected. Posted to {posted.Count} channels; threshold is {_config.MinimumChannelCount}.";

        _logger.LogInformation(
            "Spam live self-test complete: Detected={Detected}, PostedChannels={PostedChannels}, MatchedChannels={MatchedChannels}, CleanupErrors={CleanupErrors}, Fingerprint={Fingerprint}",
            detected,
            posted.Count,
            matchedChannels,
            cleanupErrors,
            fingerprint);

        return new SpamLiveTestResult(detected, message, fingerprint, matchedChannels, posted.Count, posted, cleanupErrors);
    }

    private async Task EnforceAsync(
        SocketUserMessage triggeringMessage,
        SocketTextChannel triggeringChannel,
        List<SpamCandidate> burst)
    {
        var guild = triggeringChannel.Guild;
        var userId = triggeringMessage.Author.Id;

        // Download image before deleting — CDN URLs become inaccessible once the message is gone
        byte[]? imageBytes = null;
        string? imageFilename = null;
        var imageAttachment = triggeringMessage.Attachments
            .FirstOrDefault(a => (a.ContentType ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase));
        if (imageAttachment is not null)
        {
            try
            {
                using var httpClient = new HttpClient();
                imageBytes = await httpClient.GetByteArrayAsync(imageAttachment.Url);
                imageFilename = imageAttachment.Filename;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Spam: failed to download image attachment from {Url}", imageAttachment.Url);
            }
        }

        var deleted = new List<(ulong ChannelId, ulong MessageId)>();
        if (_config.DeleteMessages)
        {
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
        }
        else
        {
            _logger.LogInformation("Spam: message deletion skipped (DeleteMessages=false) for user {UserId}", userId);
        }

        if (_config.TimeoutOnDetection)
        {
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
        }
        else
        {
            _logger.LogInformation("Spam: timeout skipped (TimeoutOnDetection=false) for user {UserId}", userId);
        }

        var channels = burst
            .Select(c => guild.GetTextChannel(c.ChannelId))
            .OfType<ITextChannel>()
            .Distinct()
            .ToList();

        await _logService.LogSpamDetectedAsync(triggeringMessage.Author, channels, burst[0].Fingerprint, deleted, imageBytes, imageFilename);
    }

    public static string ComputeFingerprint(string content, IEnumerable<AttachmentInfo> attachments)
    {
        var normalizedContent = NormalizeContent(content);
        var attachmentParts = attachments
            .Select(ComputeAttachmentSignature)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        if (string.IsNullOrEmpty(normalizedContent) && attachmentParts.Length == 0)
            return string.Empty;

        return $"{normalizedContent}|{string.Join(",", attachmentParts)}";
    }

    private List<SpamCandidate>? TrackCandidate(
        ulong userId,
        ulong channelId,
        ulong messageId,
        string fingerprint,
        DateTimeOffset now)
    {
        var window = TimeSpan.FromSeconds(_config.TimeWindowSeconds);
        var candidate = new SpamCandidate(channelId, fingerprint, now, messageId);
        List<SpamCandidate>? burst = null;

        lock (_lock)
        {
            var list = _candidates.GetOrAdd(userId, _ => []);
            list.RemoveAll(c => now - c.Timestamp > window);

            var matching = list
                .Where(c => c.Fingerprint == fingerprint && c.ChannelId != channelId)
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

        return burst;
    }

    private static string NormalizeContent(string content)
    {
        return string.Join(' ', content
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string ComputeAttachmentSignature(AttachmentInfo attachment)
    {
        var contentType = (attachment.ContentType ?? "unknown").Trim().ToLowerInvariant();
        var extension = Path.GetExtension(attachment.Filename ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var width = attachment.Width?.ToString(CultureInfo.InvariantCulture) ?? "0";
        var height = attachment.Height?.ToString(CultureInfo.InvariantCulture) ?? "0";
        var size = attachment.Size.ToString(CultureInfo.InvariantCulture);
        var spoiler = attachment.IsSpoiler ? "1" : "0";
        return $"{contentType}:{size}:{width}:{height}:{extension}:{spoiler}";
    }

    private static bool IsSelfTestMessage(SocketUserMessage message)
    {
        return message.Content.StartsWith(SelfTestPrefix, StringComparison.Ordinal);
    }

    private static string? ExtractSelfTestNonce(string content)
    {
        if (!content.StartsWith(SelfTestPrefix, StringComparison.Ordinal)) return null;
        var end = content.IndexOf(']', SelfTestPrefix.Length);
        if (end < 0) return null;
        return content[SelfTestPrefix.Length..end];
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

        var spammerExpiry = now.AddSeconds(-60);
        foreach (var key in _confirmedSpammers.Keys.ToArray())
        {
            if (_confirmedSpammers.TryGetValue(key, out var confirmedAt) && confirmedAt < spammerExpiry)
                _confirmedSpammers.TryRemove(key, out _);
        }
    }

    public SpamDetectionStatus GetStatus() =>
        new(_config.Enabled, _config.TimeWindowSeconds, _config.MinimumChannelCount);

    public SpamSimulationResult Simulate(string content, string[] attachmentFilenames)
    {
        var attachments = attachmentFilenames
            .Select(f => new AttachmentInfo(f, 0, null, null, null, false))
            .ToArray();
        var fingerprint = ComputeFingerprint(content, attachments);
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

public record SpamLiveTestResult(
    bool Detected,
    string Message,
    string Fingerprint,
    int MatchedChannels,
    int PostedChannels,
    IReadOnlyList<ulong> ChannelIds,
    int CleanupErrors);

public readonly record struct AttachmentInfo(
    string Filename,
    int Size,
    int? Width,
    int? Height,
    string? ContentType,
    bool IsSpoiler)
{
    public static AttachmentInfo FromDiscord(IAttachment attachment)
    {
        return new AttachmentInfo(
            attachment.Filename,
            attachment.Size,
            attachment.Width,
            attachment.Height,
            attachment.ContentType,
            attachment.IsSpoiler());
    }
}
