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
        [Summary("channel", "Channel to enable (defaults to current)")] SocketTextChannel? channel = null)
    {
        var target = channel ?? (SocketTextChannel)Context.Channel;
        var result = await _service.EnableChannelAsync(target.Id, Context.Guild.Id);
        await RespondAsync(result, ephemeral: true);
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

        if (!_service.IsRegisteredChannel(target.Id))
        {
            await RespondAsync($"❌ <#{target.Id}> is not registered as a single-message channel.", ephemeral: true);
            return;
        }

        var records = await _service.ListPostedUsersAsync(target.Id);

        if (records.Count == 0)
        {
            await RespondAsync($"ℹ️ No posts recorded in <#{target.Id}> yet.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"Posted users in #{target.Name}")
            .WithColor(Color.Blue)
            .WithFooter($"{records.Count} user(s) total");

        foreach (var record in records.Take(25))
        {
            var messageLink = $"https://discord.com/channels/{Context.Guild.Id}/{record.ChannelId}/{record.MessageId}";
            embed.AddField(
                $"<@{record.UserId}>",
                $"[View message]({messageLink}) — <t:{new DateTimeOffset(record.PostedAt).ToUnixTimeSeconds()}:R>");
        }

        if (records.Count > 25)
            embed.WithDescription($"Showing first 25 of {records.Count} users.");

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}
