using Discord;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using DiscordBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using Xunit;

namespace HomotechsualBot.Tests.Services;

public class ModerationLogServiceTests
{
    private static ModerationLogService BuildService(ulong forumChannelId = 0)
    {
        var services = new ServiceCollection();
        services.AddDbContext<HomotechsualBotContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        return new ModerationLogService(
            null!,
            new ModerationLogConfig { ForumChannelId = forumChannelId },
            NullLogger<ModerationLogService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task LogActionAsync_WhenForumChannelIdIsZero_DoesNotThrow()
    {
        var service = BuildService(forumChannelId: 0);
        var entry = new ModerationLogEntry(
            ModerationActionType.Ban,
            null,
            123456789UL,
            null,
            "test reason",
            DateTimeOffset.UtcNow);

        var ex = await Record.ExceptionAsync(() => service.LogActionAsync(entry));
        Assert.Null(ex);
    }

    [Fact]
    public async Task LogSpamDetectedAsync_WhenForumChannelIdIsZero_DoesNotThrow()
    {
        var service = BuildService(forumChannelId: 0);

        var ex = await Record.ExceptionAsync(() =>
            service.LogSpamDetectedAsync(null!, [], "text|", []));
        Assert.Null(ex);
    }

    [Fact]
    public async Task AppendToThreadAsync_WhenChannelIsNull_DoesNotThrow()
    {
        var service = BuildService(forumChannelId: 0);
        var embed = new EmbedBuilder().WithTitle("test").Build();

        var ex = await Record.ExceptionAsync(() =>
            service.AppendToThreadAsync(null!, embed));
        Assert.Null(ex);
    }

    [Fact]
    public async Task LogSpamDetectedAsync_WithImageBytes_DoesNotThrow()
    {
        var service = BuildService(forumChannelId: 0);
        var imageBytes = new byte[] { 1, 2, 3, 4 };

        var ex = await Record.ExceptionAsync(() =>
            service.LogSpamDetectedAsync(null!, [], "text|", [], imageBytes, "spam.png"));
        Assert.Null(ex);
    }

    [Fact]
    public void FindTitleMatch_ExactIdTag_ReturnsExactIdMatch()
    {
        var threads = new[]
        {
            new ThreadCandidate(1UL, "Some Other Title", DateTimeOffset.UtcNow),
            new ThreadCandidate(2UL, "[123456] SomeUser", DateTimeOffset.UtcNow)
        };

        var match = ModerationLogService.FindTitleMatch(threads, 123456UL, "SomeUser");

        Assert.NotNull(match);
        Assert.Equal(2UL, match!.ThreadId);
        Assert.Equal(ThreadMatchKind.ExactId, match.Kind);
    }

    [Fact]
    public void FindTitleMatch_ExactIdTag_PreferredOverFuzzyUsernameMatch()
    {
        var threads = new[]
        {
            new ThreadCandidate(1UL, "banned SomeUser again", DateTimeOffset.UtcNow),
            new ThreadCandidate(2UL, "[123456] SomeUser", DateTimeOffset.UtcNow.AddDays(-1))
        };

        var match = ModerationLogService.FindTitleMatch(threads, 123456UL, "SomeUser");

        Assert.Equal(2UL, match!.ThreadId);
        Assert.Equal(ThreadMatchKind.ExactId, match.Kind);
    }

    [Fact]
    public void FindTitleMatch_RawIdWithoutBrackets_DoesNotCountAsExactMatch()
    {
        var threads = new[]
        {
            new ThreadCandidate(1UL, "User 123456 was banned", DateTimeOffset.UtcNow)
        };

        var match = ModerationLogService.FindTitleMatch(threads, 123456UL, "NoUsernameOverlapHere");

        Assert.Null(match);
    }

    [Fact]
    public void FindTitleMatch_NoMatches_ReturnsNull()
    {
        var threads = new[]
        {
            new ThreadCandidate(1UL, "Completely unrelated title", DateTimeOffset.UtcNow)
        };

        var match = ModerationLogService.FindTitleMatch(threads, 999UL, "SomeUser");

        Assert.Null(match);
    }

    [Fact]
    public void FindTitleMatch_SingleFuzzyMatch_ReturnsFuzzyUsernameMatch()
    {
        var threads = new[]
        {
            new ThreadCandidate(5UL, "Manual thread for SomeUser", DateTimeOffset.UtcNow)
        };

        var match = ModerationLogService.FindTitleMatch(threads, 999UL, "SomeUser");

        Assert.NotNull(match);
        Assert.Equal(5UL, match!.ThreadId);
        Assert.Equal(ThreadMatchKind.FuzzyUsername, match.Kind);
    }

    [Fact]
    public void FindTitleMatch_FuzzyMatchIsCaseInsensitive()
    {
        var threads = new[]
        {
            new ThreadCandidate(5UL, "manual thread for SOMEUSER", DateTimeOffset.UtcNow)
        };

        var match = ModerationLogService.FindTitleMatch(threads, 999UL, "SomeUser");

        Assert.NotNull(match);
        Assert.Equal(ThreadMatchKind.FuzzyUsername, match!.Kind);
    }

    [Fact]
    public void FindTitleMatch_MultipleFuzzyMatches_PicksMostRecentlyCreated()
    {
        var threads = new[]
        {
            new ThreadCandidate(1UL, "Old thread SomeUser", DateTimeOffset.UtcNow.AddDays(-10)),
            new ThreadCandidate(2UL, "Newer thread SomeUser", DateTimeOffset.UtcNow.AddDays(-1)),
            new ThreadCandidate(3UL, "Oldest thread SomeUser", DateTimeOffset.UtcNow.AddDays(-30))
        };

        var match = ModerationLogService.FindTitleMatch(threads, 999UL, "SomeUser");

        Assert.Equal(2UL, match!.ThreadId);
    }

    [Fact]
    public async Task LogActionAsync_AutoWarnAllCaps_WhenForumChannelIdIsZero_DoesNotThrow()
    {
        var service = BuildService(forumChannelId: 0);
        var entry = ModerationLogEntry.CreateAutomated(
            ModerationActionType.AutoWarnAllCaps,
            target: new TestUser(555UL, "CapsUser"),
            reason: "ALL CAPS MESSAGE HERE");

        var ex = await Record.ExceptionAsync(() => service.LogActionAsync(entry));
        Assert.Null(ex);
    }
}

internal sealed class TestUser : IUser
{
    public TestUser(ulong id, string username)
    {
        Id = id;
        Username = username;
    }

    public ulong Id { get; }
    public string Username { get; }
    public string? GlobalName => null;
    public string Discriminator => "0";
    public ushort DiscriminatorValue => 0;
    public bool IsBot => false;
    public bool IsWebhook => false;
    public string? AvatarId => null;
    public string? BannerId => null;
    public Color? AccentColor => null;
    public UserProperties? PublicFlags => null;
    public string? AvatarDecorationHash => null;
    public ulong? AvatarDecorationSkuId => null;
    public PrimaryGuild? PrimaryGuild => null;
    public string Mention => $"<@{Id}>";
    public IActivity? Activity => null;
    public UserStatus Status => UserStatus.Offline;
    public IReadOnlyCollection<ClientType> ActiveClients => Array.Empty<ClientType>();
    public IReadOnlyCollection<IActivity> Activities => Array.Empty<IActivity>();
    public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
    public string AvatarUrl => string.Empty;
    public string GetAvatarUrl(ImageFormat format = ImageFormat.Auto, ushort size = 128) => string.Empty;
    public string GetDisplayAvatarUrl(ImageFormat format = ImageFormat.Auto, ushort size = 128) => string.Empty;
    public string GetDefaultAvatarUrl() => string.Empty;
    public string? GetBannerUrl(ImageFormat format = ImageFormat.Auto, ushort size = 256) => null;
    public string? GetAvatarDecorationUrl() => null;
    public Task<IDMChannel> CreateDMChannelAsync(RequestOptions? options = null) => throw new NotSupportedException();
}
