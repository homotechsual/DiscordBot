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
                    "Set `CROSS_CHANNEL_SPAM_ENABLED=true` in the GitHub secret and redeploy to activate.");
            }
        }
        else
        {
            embed.AddField("Fingerprint", "*(empty — message would be ignored)*");
            embed.AddField("Result", "Empty content and no attachments produce no fingerprint. This message would never trigger detection.");
        }

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}
