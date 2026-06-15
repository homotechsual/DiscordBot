using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class SingleMessageService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<SingleMessageService> _logger;
    private readonly ModerationLogService _logService;

    public SingleMessageService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient client,
        ILogger<SingleMessageService> logger,
        ModerationLogService logService)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
        _logService = logService;
    }

    public async Task<bool> IsEnabledAsync(ulong channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        var state = await db.SingleMessageChannelStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ChannelId == channelId);
        return state?.IsEnabled == true;
    }

    public async Task HandleMessageAsync(SocketMessage rawMessage)
    {
        if (rawMessage is not SocketUserMessage message) return;
        if (message.Author.IsBot) return;
        if (message.Channel is not SocketTextChannel channel) return;

        var channelId = channel.Id;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        var state = await db.SingleMessageChannelStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ChannelId == channelId);

        if (state is null || !state.IsEnabled) return;

        var userId = message.Author.Id;
        var existing = await db.SingleMessageRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ChannelId == channelId && r.UserId == userId);

        if (existing is null)
        {
            try
            {
                db.SingleMessageRecords.Add(new SingleMessageRecord
                {
                    ChannelId = channelId,
                    UserId = userId,
                    MessageId = message.Id,
                    PostedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException)
            {
                // Concurrent insert from another handler invocation won the race;
                // fall through to the delete path below.
            }
        }

        try
        {
            await message.DeleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete duplicate message {MessageId} in channel {ChannelId}", message.Id, channelId);
            return;
        }

        await _logService.LogActionAsync(new ModerationLogEntry(
            ModerationActionType.SingleMessageEnforced,
            message.Author,
            userId,
            null,
            $"Duplicate message deleted in <#{channelId}>",
            DateTimeOffset.UtcNow));

        try
        {
            var notification = await channel.SendMessageAsync(
                $"<@{userId}> This channel only allows one message per user. Your original message has been kept.");
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                try { await notification.DeleteAsync(); } catch { }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send enforcement notification in channel {ChannelId}", channelId);
        }
    }

    public async Task<string> EnableChannelAsync(ulong channelId, ulong guildId, bool scanHistory = false)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        var state = await db.SingleMessageChannelStates.FindAsync(channelId);
        if (state is null)
        {
            state = new SingleMessageChannelState { ChannelId = channelId, IsEnabled = true };
            db.SingleMessageChannelStates.Add(state);
        }
        else
        {
            state.IsEnabled = true;
            state.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        int prePopulated = 0;
        if (scanHistory)
            prePopulated = await ScanHistoryAsync(db, channelId, guildId);

        var suffix = prePopulated > 0
            ? $" {prePopulated} existing user(s) pre-populated from message history."
            : string.Empty;

        return $"✅ Single-message enforcement enabled for <#{channelId}>.{suffix}";
    }

    public async Task<string> DisableChannelAsync(ulong channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        var state = await db.SingleMessageChannelStates.FindAsync(channelId);
        if (state is null)
            return $"ℹ️ <#{channelId}> is not currently configured as a single-message channel.";

        if (!state.IsEnabled)
            return $"ℹ️ <#{channelId}> already has enforcement disabled.";

        state.IsEnabled = false;
        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return $"✅ Single-message enforcement disabled for <#{channelId}>. Existing records retained.";
    }

    public async Task<string> ResetUserAsync(ulong channelId, ulong userId, string userMention)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        var record = await db.SingleMessageRecords
            .FirstOrDefaultAsync(r => r.ChannelId == channelId && r.UserId == userId);

        if (record is null)
            return $"ℹ️ No record found for {userMention} in <#{channelId}>.";

        db.SingleMessageRecords.Remove(record);
        await db.SaveChangesAsync();

        return $"✅ {userMention} has been reset in <#{channelId}> and may post again.";
    }

    public async Task<IReadOnlyList<SingleMessageRecord>> ListPostedUsersAsync(ulong channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        return await db.SingleMessageRecords
            .AsNoTracking()
            .Where(r => r.ChannelId == channelId)
            .OrderBy(r => r.PostedAt)
            .ToListAsync();
    }

    private async Task<int> ScanHistoryAsync(HomotechsualBotContext db, ulong channelId, ulong guildId)
    {
        var guild = _client.GetGuild(guildId);
        if (guild?.GetChannel(channelId) is not ITextChannel textChannel)
        {
            _logger.LogWarning("Could not resolve channel {ChannelId} in guild {GuildId} for history scan", channelId, guildId);
            return 0;
        }

        var existingUserIds = await db.SingleMessageRecords
            .Where(r => r.ChannelId == channelId)
            .Select(r => r.UserId)
            .ToHashSetAsync();

        var messages = await textChannel.GetMessagesAsync(100).FlattenAsync();
        var newRecords = messages
            .Where(m => !m.Author.IsBot && !existingUserIds.Contains(m.Author.Id))
            .GroupBy(m => m.Author.Id)
            .Select(g => g.OrderBy(m => m.Timestamp).First())
            .Select(m => new SingleMessageRecord
            {
                ChannelId = channelId,
                UserId = m.Author.Id,
                MessageId = m.Id,
                PostedAt = m.Timestamp.UtcDateTime
            })
            .ToList();

        if (newRecords.Count > 0)
        {
            db.SingleMessageRecords.AddRange(newRecords);
            await db.SaveChangesAsync();
        }

        return newRecords.Count;
    }
}
