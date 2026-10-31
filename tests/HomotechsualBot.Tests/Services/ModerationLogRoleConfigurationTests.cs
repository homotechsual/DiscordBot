using System.Globalization;
using System.Text;
using Discord;
using DiscordBot.Models;
using DiscordBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomotechsualBot.Tests.Services;

public class ModerationLogRoleConfigurationTests
{
    [Theory]
    [InlineData("0", null)]
    [InlineData("\"0\"", null)]
    [InlineData("1234567890", "<@&1234567890>")]
    [InlineData("\"1234567890\"", "<@&1234567890>")]
    [InlineData("\"1234567890,2345678901\"", "<@&1234567890> <@&2345678901>")]
    [InlineData("\" 1234567890, 0,2345678901,1234567890 \"", "<@&1234567890> <@&2345678901>")]
    [InlineData("18446744073709551615", "<@&18446744073709551615>")]
    public void JsonConfiguration_PreservesLegacyValuesAndBuildsBothFooters(string value, string? expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"ModerationLog\":{{\"ModeratorRoleId\":{value}}}}}"));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var config = configuration.GetSection("ModerationLog").Get<ModerationLogConfig>()!;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new ModerationLogService(null, config, NullLogger<ModerationLogService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>());
        var entry = new ModerationLogEntry(ModerationActionType.Ban, null, 123UL, null, "test", DateTimeOffset.UtcNow);

        Assert.Equal(expected, service.BuildActionEmbed(entry).Footer?.Text);
        Assert.Equal(expected, service.BuildSpamEmbed(null, [], "test|").Footer?.Text);
    }

    [Fact]
    public void DefaultConfiguration_HasNoModeratorRoles()
    {
        Assert.Empty(new ModerationLogConfig().GetModeratorRoleIds());
    }

    [Fact]
    public void EnvironmentVariable_UsesExistingSettingNameForCommaSeparatedIds()
    {
        var prefix = $"MODROLE_TEST_{Guid.NewGuid():N}_";
        var key = $"{prefix}ModerationLog__ModeratorRoleId";
        try
        {
            Environment.SetEnvironmentVariable(key, "1234567890,2345678901");
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var config = configuration.GetSection("ModerationLog").Get<ModerationLogConfig>()!;

            Assert.Equal(new ulong[] { 1234567890, 2345678901 }, config.GetModeratorRoleIds());
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("123,")]
    [InlineData(",123")]
    [InlineData("123,,456")]
    [InlineData("123,abc")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("18446744073709551616")]
    public void InvalidConfiguration_FailsExplicitly(string value)
    {
        var config = new ModerationLogConfig { ModeratorRoleId = value };
        var exception = Assert.Throws<InvalidOperationException>(() => ModerationLogService.BuildModeratorRoleFooter(config));
        Assert.Contains("ModerationLog:ModeratorRoleId", exception.Message);
    }

    [Fact]
    public void Footer_AtDiscordLimitIsAcceptedAndAboveLimitIsRejected()
    {
        var ids = Enumerable.Range(0, 81).Select(i => (ulong.MaxValue - (ulong)i).ToString(CultureInfo.InvariantCulture)).ToArray();
        var config = new ModerationLogConfig { ModeratorRoleId = string.Join(",", ids.Append("1000000000000000000")) };
        Assert.Equal(EmbedFooterBuilder.MaxFooterTextLength, ModerationLogService.BuildModeratorRoleFooter(config).Length);

        config.ModeratorRoleId = string.Join(",", ids.Append((ulong.MaxValue - 81UL).ToString(CultureInfo.InvariantCulture)));
        var exception = Assert.Throws<InvalidOperationException>(() => ModerationLogService.BuildModeratorRoleFooter(config));
        Assert.Contains("footer limit", exception.Message);
    }
}
