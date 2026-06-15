using Discord;
using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;

[Group("spam", "Cross-channel spam detection tools")]
[RequireUserPermission(GuildPermission.ManageMessages)]
public class SpamTestModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly CrossChannelSpamDetector _detector;

    public SpamTestModule(CrossChannelSpamDetector detector)
    {
        _detector = detector;
    }

    [SlashCommand("test", "Dry-run: show how the spam detector would handle a given message")]
    public async Task TestAsync(
        [Summary("content", "Message text to test")] string content,
        [Summary("attachments", "Comma-separated attachment filenames to simulate (optional)")] string? attachments = null)
    {
        var filenames = attachments?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        var status = _detector.GetStatus();
        var result = _detector.Simulate(content, filenames);

        var embed = new EmbedBuilder()
            .WithTitle("Spam Detection Dry-Run")
            .WithColor(status.Enabled ? Color.Orange : Color.LightGrey)
            .WithFooter("No actions taken — this is a read-only test");

        embed.AddField("Status",
            status.Enabled ? "✅ Enabled" : "❌ Disabled",
            inline: true);
        embed.AddField("Time Window",
            $"{status.TimeWindowSeconds}s",
            inline: true);
        embed.AddField("Min. Channel Count",
            status.MinimumChannelCount.ToString(),
            inline: true);

        if (result.WouldBeTracked)
        {
            embed.AddField("Fingerprint", $"`{result.Fingerprint}`");

            if (status.Enabled)
            {
                embed.AddField("Trigger Condition",
                    $"This exact message posted in **{status.MinimumChannelCount}+** different channels within **{status.TimeWindowSeconds}s** would trigger detection.");
                embed.AddField("Enforcement (if triggered)",
                    "• All copies deleted\n• Author timed out for 28 days\n• Alert posted to mod log with Ban / Dismiss buttons");
            }
            else
            {
                embed.AddField("⚠️ Detection Disabled",
                    "Set `CROSS_CHANNEL_SPAM_ENABLED=true` in config/secrets and redeploy to activate.");
            }
        }
        else
        {
            embed.AddField("Fingerprint", "*(empty — message would be ignored)*");
            embed.AddField("Result", "Empty content and no attachments produce no fingerprint. This message would never trigger detection.");
        }

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("live-test", "Post identical test messages to channels and verify real detector behavior")]
    public async Task LiveTestAsync(
        [Summary("channel1", "First channel to post to")] ITextChannel channel1,
        [Summary("channel2", "Second channel to post to")] ITextChannel channel2,
        [Summary("content", "Message text to post in each channel")] string content = "",
        [Summary("channel3", "Optional third channel")] ITextChannel? channel3 = null,
        [Summary("channel4", "Optional fourth channel")] ITextChannel? channel4 = null,
        [Summary("attachment", "Optional image/file to include in each test post")] IAttachment? attachment = null)
    {
        await DeferAsync(ephemeral: true);

        if (string.IsNullOrWhiteSpace(content) && attachment is null)
        {
            await FollowupAsync("❌ Provide either a text message or an attachment (or both).", ephemeral: true);
            return;
        }

        var channels = new[] { channel1, channel2, channel3, channel4 }
            .OfType<ITextChannel>()
            .DistinctBy(c => c.Id)
            .ToList();

        var status = _detector.GetStatus();
        if (!status.Enabled)
        {
            await FollowupAsync(
                "❌ Cross-channel spam detection is disabled. Enable `CrossChannelSpam:Enabled` (or `CROSS_CHANNEL_SPAM_ENABLED=true`) before running a live test.",
                ephemeral: true);
            return;
        }

        if (channels.Count < 2)
        {
            await FollowupAsync("❌ Choose at least 2 distinct channels.", ephemeral: true);
            return;
        }

        if (channels.Count < status.MinimumChannelCount)
        {
            await FollowupAsync(
                $"⚠️ Detector requires {status.MinimumChannelCount} channels, but only {channels.Count} were provided. Add more channels for a guaranteed trigger.",
                ephemeral: true);
        }

        var result = await _detector.RunLiveSelfTestAsync(Context.Guild, channels, content, attachment);

        var embed = new EmbedBuilder()
            .WithTitle("Spam Detection Live Test")
            .WithColor(result.Detected ? Color.Green : Color.Orange)
            .AddField("Detected", result.Detected ? "✅ Yes" : "❌ No", inline: true)
            .AddField("Posted Channels", result.PostedChannels.ToString(), inline: true)
            .AddField("Matched Channels", result.MatchedChannels.ToString(), inline: true)
            .AddField("Cleanup", result.CleanupErrors == 0 ? "✅ Test messages cleaned up" : $"⚠️ Cleanup errors: {result.CleanupErrors}", inline: true)
            .AddField("Fingerprint", string.IsNullOrEmpty(result.Fingerprint) ? "(empty)" : $"`{result.Fingerprint}`")
            .AddField("Message", result.Message)
            .WithFooter("This self-test bypasses punitive enforcement and only verifies detection matching.");

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }
}
