using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;

[Group("singlemessage", "Manage single-message-per-user channel enforcement")]
[RequireUserPermission(GuildPermission.ManageChannels)]
public class SingleMessageModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly SingleMessageService _service;

    public SingleMessageModule(SingleMessageService service)
    {
        _service = service;
    }

    [SlashCommand("enable", "Enable single-message enforcement for a channel")]
    public async Task EnableAsync(
        [Summary("channel", "Channel to enable (defaults to current)")] SocketTextChannel? channel = null,
        [Summary("scan_history", "Pre-populate from last 100 messages (default: true)")] bool scanHistory = true)
    {
        await DeferAsync(ephemeral: true);

        var target = channel ?? (SocketTextChannel)Context.Channel;
        var result = await _service.EnableChannelAsync(target.Id, Context.Guild.Id, scanHistory);
        await FollowupAsync(result, ephemeral: true);
    }

    [SlashCommand("disable", "Disable single-message enforcement for a channel")]
    public async Task DisableAsync(
        [Summary("channel", "Channel to disable (defaults to current)")] SocketTextChannel? channel = null)
    {
        var target = channel ?? (SocketTextChannel)Context.Channel;
        var result = await _service.DisableChannelAsync(target.Id);
        await RespondAsync(result, ephemeral: true);
    }

    [SlashCommand("reset-user", "Allow a user to post again in a single-message channel")]
    public async Task ResetUserAsync(
        [Summary("user", "User to reset")] SocketGuildUser user,
        [Summary("channel", "Channel to reset in (defaults to current)")] SocketTextChannel? channel = null)
    {
        var target = channel ?? (SocketTextChannel)Context.Channel;
        var result = await _service.ResetUserAsync(target.Id, user.Id, user.Mention);
        await RespondAsync(result, ephemeral: true);
    }

    [SlashCommand("list", "List users who have posted in a single-message channel")]
    public async Task ListAsync(
        [Summary("channel", "Channel to list (defaults to current)")] SocketTextChannel? channel = null)
    {
        var target = channel ?? (SocketTextChannel)Context.Channel;

        var isEnabled = await _service.IsEnabledAsync(target.Id);
        var records = await _service.ListPostedUsersAsync(target.Id);

        if (!isEnabled && records.Count == 0)
        {
            await RespondAsync(
                $"ℹ️ <#{target.Id}> does not have single-message enforcement configured. Use `/singlemessage enable` to set it up.",
                ephemeral: true);
            return;
        }

        var statusLine = isEnabled ? "🟢 Enforcement active" : "🔴 Enforcement disabled";

        if (records.Count == 0)
        {
            await RespondAsync($"{statusLine} — no posts recorded in <#{target.Id}> yet.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"Posted users in #{target.Name}")
            .WithDescription(statusLine)
            .WithColor(isEnabled ? Color.Green : Color.Red)
            .WithFooter($"{records.Count} user(s) total");

        foreach (var record in records.Take(25))
        {
            var messageLink = $"https://discord.com/channels/{Context.Guild.Id}/{record.ChannelId}/{record.MessageId}";
            var guildUser = Context.Guild.GetUser(record.UserId);
            var username = guildUser?.Username ?? "Unknown User";
            embed.AddField(
                $"{username} ({record.UserId})",
                $"<@{record.UserId}> — [View message]({messageLink}) — <t:{new DateTimeOffset(record.PostedAt).ToUnixTimeSeconds()}:R>");
        }

        if (records.Count > 25)
            embed.WithDescription($"{statusLine}\nShowing first 25 of {records.Count} users.");

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}
