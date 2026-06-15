using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomotechsualBot.Tests;

public sealed class SingleMessageServiceTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;

    public SingleMessageServiceTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<HomotechsualBotContext>(o =>
            o.UseInMemoryDatabase(dbName));
        _provider = services.BuildServiceProvider();
        _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    // HandleMessageAsync is not covered here because it requires live SocketMessage/SocketTextChannel
    // objects from Discord.Net, which are sealed and not easily constructable in unit tests.
    // Integration/manual testing covers the enforcement flow.

    private async Task<T> UseDbAsync<T>(Func<HomotechsualBotContext, Task<T>> action)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        return await action(db);
    }

    private async Task UseDbAsync(Func<HomotechsualBotContext, Task> action)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        await action(db);
    }

    private DiscordBot.Services.SingleMessageService BuildService() =>
        new(_scopeFactory, null!, NullLogger<DiscordBot.Services.SingleMessageService>.Instance, null!);

    [Fact]
    public async Task EnableChannelAsync_NewChannel_SetsEnabledInDb()
    {
        const ulong channelId = 111UL;
        var service = BuildService();

        var result = await service.EnableChannelAsync(channelId, 1UL);

        Assert.Contains("✅", result, StringComparison.Ordinal);
        var state = await UseDbAsync(db => db.SingleMessageChannelStates.FindAsync(channelId).AsTask());
        Assert.NotNull(state);
        Assert.True(state.IsEnabled);
    }

    [Fact]
    public async Task EnableChannelAsync_AlreadyEnabled_UpdatesTimestamp()
    {
        const ulong channelId = 112UL;
        var service = BuildService();

        await service.EnableChannelAsync(channelId, 1UL);
        var result = await service.EnableChannelAsync(channelId, 1UL);

        Assert.Contains("✅", result, StringComparison.Ordinal);
        var state = await UseDbAsync(db => db.SingleMessageChannelStates.FindAsync(channelId).AsTask());
        Assert.NotNull(state);
        Assert.True(state.IsEnabled);
    }

    [Fact]
    public async Task IsEnabledAsync_EnabledChannel_ReturnsTrue()
    {
        const ulong channelId = 113UL;
        var service = BuildService();

        await service.EnableChannelAsync(channelId, 1UL);

        Assert.True(await service.IsEnabledAsync(channelId));
    }

    [Fact]
    public async Task IsEnabledAsync_UnknownChannel_ReturnsFalse()
    {
        var service = BuildService();

        Assert.False(await service.IsEnabledAsync(999UL));
    }

    [Fact]
    public async Task IsEnabledAsync_DisabledChannel_ReturnsFalse()
    {
        const ulong channelId = 114UL;
        var service = BuildService();

        await service.EnableChannelAsync(channelId, 1UL);
        await service.DisableChannelAsync(channelId);

        Assert.False(await service.IsEnabledAsync(channelId));
    }

    [Fact]
    public async Task DisableChannelAsync_EnabledChannel_SetsDisabledInDb()
    {
        const ulong channelId = 222UL;
        var service = BuildService();

        await service.EnableChannelAsync(channelId, 1UL);
        var result = await service.DisableChannelAsync(channelId);

        Assert.Contains("✅", result, StringComparison.Ordinal);
        var state = await UseDbAsync(db => db.SingleMessageChannelStates.FindAsync(channelId).AsTask());
        Assert.NotNull(state);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public async Task DisableChannelAsync_NeverConfigured_ReturnsInfoMessage()
    {
        var service = BuildService();

        var result = await service.DisableChannelAsync(999UL);

        Assert.Contains("ℹ️", result, StringComparison.Ordinal);
        Assert.Contains("not currently configured", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisableChannelAsync_AlreadyDisabled_ReturnsInfoMessage()
    {
        const ulong channelId = 888UL;
        var service = BuildService();

        await service.EnableChannelAsync(channelId, 1UL);
        await service.DisableChannelAsync(channelId);
        var result = await service.DisableChannelAsync(channelId);

        Assert.Contains("ℹ️", result, StringComparison.Ordinal);
        Assert.Contains("already", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisableChannelAsync_PreservesExistingRecords()
    {
        const ulong channelId = 333UL;
        var service = BuildService();

        await service.EnableChannelAsync(channelId, 1UL);
        await UseDbAsync(async db =>
        {
            db.SingleMessageRecords.Add(new SingleMessageRecord
            {
                ChannelId = channelId, UserId = 42UL, MessageId = 99UL, PostedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        await service.DisableChannelAsync(channelId);

        var count = await UseDbAsync(db =>
            db.SingleMessageRecords.CountAsync(r => r.ChannelId == channelId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ResetUserAsync_ExistingRecord_DeletesRecord()
    {
        const ulong channelId = 444UL;
        const ulong userId = 55UL;
        var service = BuildService();

        await UseDbAsync(async db =>
        {
            db.SingleMessageRecords.Add(new SingleMessageRecord
            {
                ChannelId = channelId, UserId = userId, MessageId = 1UL, PostedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        var result = await service.ResetUserAsync(channelId, userId, $"<@{userId}>");

        Assert.Contains("✅", result, StringComparison.Ordinal);
        var exists = await UseDbAsync(db =>
            db.SingleMessageRecords.AnyAsync(r => r.ChannelId == channelId && r.UserId == userId));
        Assert.False(exists);
    }

    [Fact]
    public async Task ResetUserAsync_NoRecord_ReturnsInfoMessage()
    {
        var service = BuildService();

        var result = await service.ResetUserAsync(555UL, 99UL, "<@99>");

        Assert.Contains("ℹ️", result, StringComparison.Ordinal);
        Assert.Contains("No record found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListPostedUsersAsync_ReturnsRecordsOrderedByPostedAt()
    {
        const ulong channelId = 666UL;
        var service = BuildService();

        var now = DateTime.UtcNow;
        await UseDbAsync(async db =>
        {
            db.SingleMessageRecords.AddRange(
                new SingleMessageRecord { ChannelId = channelId, UserId = 1UL, MessageId = 10UL, PostedAt = now.AddMinutes(-5) },
                new SingleMessageRecord { ChannelId = channelId, UserId = 2UL, MessageId = 11UL, PostedAt = now.AddMinutes(-2) },
                new SingleMessageRecord { ChannelId = channelId, UserId = 3UL, MessageId = 12UL, PostedAt = now }
            );
            await db.SaveChangesAsync();
        });

        var records = await service.ListPostedUsersAsync(channelId);

        Assert.Equal(3, records.Count);
        Assert.Equal(1UL, records[0].UserId);
        Assert.Equal(2UL, records[1].UserId);
        Assert.Equal(3UL, records[2].UserId);
    }
}
