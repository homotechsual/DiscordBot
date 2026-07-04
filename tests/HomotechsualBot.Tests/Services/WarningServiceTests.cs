using DiscordBot.Core.Data;
using DiscordBot.Models;
using DiscordBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomotechsualBot.Tests.Services;

public sealed class WarningServiceTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;

    public WarningServiceTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<HomotechsualBotContext>(o => o.UseInMemoryDatabase(dbName));
        _provider = services.BuildServiceProvider();
        _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private WarningService BuildService() =>
        new(_scopeFactory, NullLogger<WarningService>.Instance);

    private async Task<int> CountWarningsAsync(ulong guildId, ulong userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        return await db.UserWarnings.CountAsync(w => w.GuildId == guildId && w.UserId == userId);
    }

    [Fact]
    public async Task AddWarningAsync_FirstWarning_ReturnsCountOneAndDoesNotKick()
    {
        var service = BuildService();

        var result = await service.AddWarningAsync(1UL, 100UL, "test reason", WarningSource.Manual, 999UL, null);

        Assert.Equal(1, result.WarningCount);
        Assert.False(result.Kicked);
        Assert.Null(result.KickFailureReason);
        Assert.Equal(1, await CountWarningsAsync(1UL, 100UL));
    }

    [Fact]
    public async Task AddWarningAsync_ScopesCountByGuildAndUser()
    {
        var service = BuildService();

        await service.AddWarningAsync(1UL, 100UL, "r1", WarningSource.Manual, 999UL, null);
        await service.AddWarningAsync(2UL, 100UL, "r2", WarningSource.Manual, 999UL, null);
        var result = await service.AddWarningAsync(1UL, 100UL, "r3", WarningSource.Manual, 999UL, null);

        Assert.Equal(2, result.WarningCount);
    }

    [Fact]
    public async Task AddWarningAsync_BelowThreshold_DoesNotAttemptKickEvenWithGuildUser()
    {
        var service = BuildService();

        var result = await service.AddWarningAsync(1UL, 100UL, "r1", WarningSource.AutoAllCaps, null, null);

        Assert.Equal(1, result.WarningCount);
        Assert.False(result.Kicked);
    }

    [Fact]
    public async Task AddWarningAsync_AtThresholdWithNullGuildUser_DoesNotKick()
    {
        var service = BuildService();

        await service.AddWarningAsync(1UL, 100UL, "r1", WarningSource.Manual, 999UL, null);
        await service.AddWarningAsync(1UL, 100UL, "r2", WarningSource.Manual, 999UL, null);
        var result = await service.AddWarningAsync(1UL, 100UL, "r3", WarningSource.Manual, 999UL, guildUserForKick: null);

        Assert.Equal(3, result.WarningCount);
        Assert.False(result.Kicked);
        Assert.Null(result.KickFailureReason);
    }

    [Fact]
    public async Task AddWarningAsync_RecordsSourceAndModerator()
    {
        var service = BuildService();

        await service.AddWarningAsync(1UL, 100UL, "all-caps message", WarningSource.AutoAllCaps, null, null);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        var stored = await db.UserWarnings.SingleAsync(w => w.GuildId == 1UL && w.UserId == 100UL, TestContext.Current.CancellationToken);
        Assert.Equal(WarningSource.AutoAllCaps, stored.Source);
        Assert.Null(stored.ModeratorId);
        Assert.Equal("all-caps message", stored.Reason);
    }
}
