using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Attributes;
using DiscordBot.Services;
using System.Reflection;

namespace DiscordBot.Modules.Generals;

public class AboutModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DiscordSocketClient _client;
    private readonly DiscordBotService _botService;
    private readonly InteractionService _interactionService;

    public AboutModule(DiscordSocketClient client, DiscordBotService botService, InteractionService interactionService)
    {
        _client = client;
        _botService = botService;
        _interactionService = interactionService;
    }

    [SlashCommand("about", "About this bot")]
    [Cooldown(5)]
    public async Task AboutAsync()
    {
        var botUser = _client.CurrentUser;
        var uptime = DateTime.UtcNow - _botService.StartTime;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var moduleCount = _interactionService.Modules.Count;
        var commandCount = _interactionService.SlashCommands.Count();

        var embed = new EmbedBuilder()
            .WithTitle("🤖 About this bot")
            .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
            .AddField("Name", botUser.Username, true)
            .AddField("Version", version, true)
            .AddField("Created date", botUser.CreatedAt.ToString("dd/MM/yyyy"), true)
            .AddField("Status", _client.Status.ToString(), true)
            .AddField("Framework", $"Discord.Net v{Discord.DiscordConfig.Version}", true)
            .AddField("Modules", moduleCount.ToString(), true)
            .AddField("Commands", commandCount.ToString(), true)
            .AddField("Uptime", $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s", false)
            .WithFooter($"Requested by {Context.User.Username}")
            .WithColor(Color.Blue)
            .Build();

        await RespondAsync(embed: embed);
    }
}
