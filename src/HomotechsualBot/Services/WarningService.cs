using Discord;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class WarningService
{
    private const int KickThreshold = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WarningService> _logger;

    public WarningService(IServiceScopeFactory scopeFactory, ILogger<WarningService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<WarningResult> AddWarningAsync(
        ulong guildId,
        ulong userId,
        string reason,
        WarningSource source,
        ulong? moderatorId,
        IGuildUser? guildUserForKick)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        db.UserWarnings.Add(new UserWarning
        {
            GuildId = guildId,
            UserId = userId,
            Reason = reason,
            Source = source,
            ModeratorId = moderatorId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var warningCount = await db.UserWarnings.CountAsync(w => w.GuildId == guildId && w.UserId == userId);

        if (warningCount < KickThreshold || guildUserForKick is null)
        {
            return new WarningResult(warningCount, false, null);
        }

        try
        {
            await guildUserForKick.KickAsync($"Over {KickThreshold} warnings (total: {warningCount})");
            return new WarningResult(warningCount, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WarningService: failed to kick user {UserId} after {Count} warnings", userId, warningCount);
            return new WarningResult(warningCount, false, ex.Message);
        }
    }
}

public record WarningResult(int WarningCount, bool Kicked, string? KickFailureReason);
