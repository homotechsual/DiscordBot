using Discord;
using DiscordBot.Modules.Moderations;
using Xunit;

namespace HomotechsualBot.Tests;

public sealed class MoveMessagesModuleTests
{
    [Fact]
    public async Task ApplyReactionsAsync_AllSuccessful_ReplaysAllReactions()
    {
        var applied = new List<string>();
        var reactions = new IEmote[]
        {
            new Emoji("👍"),
            new Emoji("✅"),
            new Emoji("🎯")
        };

        var count = await MoveMessagesModule.ApplyReactionsAsync(
            emote =>
            {
                applied.Add(emote.ToString() ?? string.Empty);
                return Task.CompletedTask;
            },
            reactions);

        Assert.Equal(3, count);
        Assert.Equal(new[] { "👍", "✅", "🎯" }, applied);
    }

    [Fact]
    public async Task ApplyReactionsAsync_WhenOneFails_ContinuesRemainingReactions()
    {
        var applied = new List<string>();
        var reactions = new IEmote[]
        {
            new Emoji("👍"),
            new Emoji("✅"),
            new Emoji("🎯")
        };

        var count = await MoveMessagesModule.ApplyReactionsAsync(
            emote =>
            {
                var value = emote.ToString() ?? string.Empty;
                if (value == "✅")
                {
                    throw new InvalidOperationException("simulated failure");
                }

                applied.Add(value);
                return Task.CompletedTask;
            },
            reactions);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "👍", "🎯" }, applied);
    }
}
