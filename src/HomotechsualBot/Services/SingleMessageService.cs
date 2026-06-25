using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data;

namespace DiscordBot.Services;

public class SingleMessageService
{
    private const int HistoryBackfillBatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<SingleMessageService> _logger;
    private readonly ModerationLogService _logService;
    private readonly ModerationExemptionService _exemptionService;
    private readonly SemaphoreSlim _historyBackfillLock = new(1, 1);
    private readonly SemaphoreSlim _schemaInitLock = new(1, 1);
    private volatile bool _schemaInitialized;

    public SingleMessageService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient client,
        ILogger<SingleMessageService> logger,
        ModerationLogService logService,
        ModerationExemptionService exemptionService)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
        _logService = logService;
        _exemptionService = exemptionService;
    }

    public async Task<bool> IsEnabledAsync(ulong channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        if (!await EnsureSchemaInitializedAsync(db))
        {
            return false;
        }

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
        if (_exemptionService.IsExempt(message.Author)) return;

        var guildUser = channel.Guild.GetUser(message.Author.Id);
        if (_exemptionService.IsExempt(guildUser)) return;

        var channelId = channel.Id;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        if (!await EnsureSchemaInitializedAsync(db))
        {
            return;
        }

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

        if (!await EnsureSchemaInitializedAsync(db))
        {
            return "❌ Failed to initialize database schema. Check logs and retry.";
        }

        var state = await db.SingleMessageChannelStates.FindAsync(channelId);
        if (state is null)
        {
            state = new SingleMessageChannelState { ChannelId = channelId, IsEnabled = true };
            db.SingleMessageChannelStates.Add(state);
        }
        else
        {
            state.IsEnabled = true;
        }

        state.GuildId = guildId;

        int prePopulated = 0;
        if (scanHistory)
        {
            var batch = await ScanHistoryBatchAsync(db, channelId, guildId, null, HistoryBackfillBatchSize);
            prePopulated = batch.AddedRecords;
            state.BackfillInProgress = batch.HasMore && batch.NextBeforeMessageId.HasValue;
            state.BackfillBeforeMessageId = state.BackfillInProgress ? batch.NextBeforeMessageId : null;
        }
        else
        {
            state.BackfillInProgress = false;
            state.BackfillBeforeMessageId = null;
        }

        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var scanSuffix = prePopulated > 0
            ? $" {prePopulated} existing user(s) pre-populated from recent history."
            : string.Empty;

        var backfillSuffix = scanHistory && state.BackfillInProgress
            ? " Background backfill started and will continue scanning older messages in batches."
            : string.Empty;

        return $"✅ Single-message enforcement enabled for <#{channelId}>.{scanSuffix}{backfillSuffix}";
    }

    public async Task<string> DisableChannelAsync(ulong channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        if (!await EnsureSchemaInitializedAsync(db))
        {
            return "❌ Failed to initialize database schema. Check logs and retry.";
        }

        var state = await db.SingleMessageChannelStates.FindAsync(channelId);
        if (state is null)
            return $"ℹ️ <#{channelId}> is not currently configured as a single-message channel.";

        if (!state.IsEnabled)
            return $"ℹ️ <#{channelId}> already has enforcement disabled.";

        state.IsEnabled = false;
        state.BackfillInProgress = false;
        state.BackfillBeforeMessageId = null;
        state.UpdatedAt = DateTime.UtcNow;

        var records = await db.SingleMessageRecords
            .Where(r => r.ChannelId == channelId)
            .ToListAsync();
        db.SingleMessageRecords.RemoveRange(records);
        await db.SaveChangesAsync();

        return $"✅ Single-message enforcement disabled for <#{channelId}>. {records.Count} user record{(records.Count == 1 ? "" : "s")} cleared.";
    }

    public async Task<string> ResetUserAsync(ulong channelId, ulong userId, string userMention)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        if (!await EnsureSchemaInitializedAsync(db))
        {
            return "❌ Failed to initialize database schema. Check logs and retry.";
        }

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

        if (!await EnsureSchemaInitializedAsync(db))
        {
            return Array.Empty<SingleMessageRecord>();
        }

        return await db.SingleMessageRecords
            .AsNoTracking()
            .Where(r => r.ChannelId == channelId)
            .OrderBy(r => r.PostedAt)
            .ToListAsync();
    }

    public async Task ProcessHistoryBackfillOnceAsync(CancellationToken cancellationToken)
    {
        if (!await _historyBackfillLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

            if (!await EnsureSchemaInitializedAsync(db))
            {
                return;
            }

            var state = await db.SingleMessageChannelStates
                .FirstOrDefaultAsync(
                    s => s.IsEnabled && s.BackfillInProgress && s.BackfillBeforeMessageId.HasValue && s.GuildId.HasValue,
                    cancellationToken);

            if (state is null)
            {
                return;
            }

            var batch = await ScanHistoryBatchAsync(
                db,
                state.ChannelId,
                state.GuildId!.Value,
                state.BackfillBeforeMessageId,
                HistoryBackfillBatchSize);

            if (batch.HasMore && batch.NextBeforeMessageId.HasValue)
            {
                state.BackfillBeforeMessageId = batch.NextBeforeMessageId.Value;
            }
            else
            {
                state.BackfillInProgress = false;
                state.BackfillBeforeMessageId = null;
                _logger.LogInformation(
                    "Single-message history backfill completed for channel {ChannelId}",
                    state.ChannelId);
            }

            state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            if (batch.AddedRecords > 0)
            {
                _logger.LogInformation(
                    "Single-message history backfill progress for channel {ChannelId}: +{AddedRecords} user record(s) this batch",
                    state.ChannelId,
                    batch.AddedRecords);
            }
        }
        finally
        {
            _historyBackfillLock.Release();
        }
    }

    private async Task<bool> EnsureSchemaInitializedAsync(HomotechsualBotContext db)
    {
        if (_schemaInitialized)
        {
            return true;
        }

        await _schemaInitLock.WaitAsync();
        try
        {
            if (_schemaInitialized)
            {
                return true;
            }

            if (!db.Database.IsRelational())
            {
                await db.Database.EnsureCreatedAsync();
                _schemaInitialized = true;
                return true;
            }

            await db.Database.MigrateAsync();

            if (!db.Database.IsSqlite())
            {
                _schemaInitialized = true;
                return true;
            }

            var hasChannelStates = await TableExistsAsync(db, "SingleMessageChannelStates");
            var hasRecords = await TableExistsAsync(db, "SingleMessageRecords");

            if (!hasChannelStates || !hasRecords)
            {
                _logger.LogWarning(
                    "SingleMessage schema is incomplete after migrations (ChannelStates={HasChannelStates}, Records={HasRecords}). Creating missing tables.",
                    hasChannelStates,
                    hasRecords);

                await CreateMissingSingleMessageSchemaAsync(db);
            }

            await EnsureColumnExistsAsync(db, "SingleMessageChannelStates", "GuildId", "INTEGER NULL");
            await EnsureColumnExistsAsync(db, "SingleMessageChannelStates", "BackfillBeforeMessageId", "INTEGER NULL");
            await EnsureColumnExistsAsync(db, "SingleMessageChannelStates", "BackfillInProgress", "INTEGER NOT NULL DEFAULT 0");

            _schemaInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SingleMessage database schema");
            return false;
        }
        finally
        {
            _schemaInitLock.Release();
        }
    }

    private static async Task<bool> TableExistsAsync(HomotechsualBotContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;

        if (closeWhenDone)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();
            return result is not null and not DBNull;
        }
        finally
        {
            if (closeWhenDone)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> TableColumnExistsAsync(HomotechsualBotContext db, string tableName, string columnName)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;

        if (closeWhenDone)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM pragma_table_info($tableName) WHERE name = $columnName;";

            var tableParam = command.CreateParameter();
            tableParam.ParameterName = "$tableName";
            tableParam.Value = tableName;
            command.Parameters.Add(tableParam);

            var columnParam = command.CreateParameter();
            columnParam.ParameterName = "$columnName";
            columnParam.Value = columnName;
            command.Parameters.Add(columnParam);

            var result = await command.ExecuteScalarAsync();
            return result is long l && l > 0;
        }
        finally
        {
            if (closeWhenDone)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task EnsureColumnExistsAsync(HomotechsualBotContext db, string tableName, string columnName, string sqlTypeDefinition)
    {
        if (await TableColumnExistsAsync(db, tableName, columnName))
        {
            return;
        }

        ValidateSqlIdentifier(tableName);
        ValidateSqlIdentifier(columnName);

        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;

        if (closeWhenDone)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {sqlTypeDefinition};";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (closeWhenDone)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void ValidateSqlIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
        {
            throw new InvalidOperationException($"Unsafe SQL identifier: {identifier}");
        }
    }

    private static async Task CreateMissingSingleMessageSchemaAsync(HomotechsualBotContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SingleMessageChannelStates" (
                "ChannelId" INTEGER NOT NULL CONSTRAINT "PK_SingleMessageChannelStates" PRIMARY KEY,
                "GuildId" INTEGER NULL,
                "IsEnabled" INTEGER NOT NULL DEFAULT 0,
                "BackfillBeforeMessageId" INTEGER NULL,
                "BackfillInProgress" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                "UpdatedAt" TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SingleMessageRecords" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SingleMessageRecords" PRIMARY KEY AUTOINCREMENT,
                "ChannelId" INTEGER NOT NULL,
                "UserId" INTEGER NOT NULL,
                "MessageId" INTEGER NOT NULL,
                "PostedAt" TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                CONSTRAINT "FK_SingleMessageRecords_SingleMessageChannelStates_ChannelId"
                    FOREIGN KEY ("ChannelId") REFERENCES "SingleMessageChannelStates" ("ChannelId") ON DELETE CASCADE
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SingleMessageRecords_ChannelId_UserId"
            ON "SingleMessageRecords" ("ChannelId", "UserId");
            """);
    }

    private async Task<(int AddedRecords, ulong? NextBeforeMessageId, bool HasMore)> ScanHistoryBatchAsync(
        HomotechsualBotContext db,
        ulong channelId,
        ulong guildId,
        ulong? beforeMessageId,
        int limit)
    {
        var guild = _client.GetGuild(guildId);
        if (guild?.GetChannel(channelId) is not ITextChannel textChannel)
        {
            _logger.LogWarning("Could not resolve channel {ChannelId} in guild {GuildId} for history scan", channelId, guildId);
            return (0, null, false);
        }

        var existingUserIds = await db.SingleMessageRecords
            .Where(r => r.ChannelId == channelId)
            .Select(r => r.UserId)
            .ToHashSetAsync();

        var messagesEnumerable = beforeMessageId.HasValue
            ? textChannel.GetMessagesAsync(beforeMessageId.Value, Direction.Before, limit)
            : textChannel.GetMessagesAsync(limit);

        var messages = await messagesEnumerable.FlattenAsync();
        var orderedMessages = messages.OrderByDescending(m => m.Timestamp).ToList();

        if (orderedMessages.Count == 0)
        {
            return (0, null, false);
        }

        var newRecords = messages
            .Where(m => !m.Author.IsBot
                        && !existingUserIds.Contains(m.Author.Id)
                        && !_exemptionService.IsExempt(m.Author.Id)
                        && !_exemptionService.IsExempt(guild.GetUser(m.Author.Id)))
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

        var nextBeforeMessageId = orderedMessages[^1].Id;
        var hasMore = orderedMessages.Count == limit;

        return (newRecords.Count, nextBeforeMessageId, hasMore);
    }
}
