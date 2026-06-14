using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Attributes;

namespace DiscordBot.Modules.Generals;

public class HelpModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly InteractionService _interactionService;

    public HelpModule(InteractionService interactionService)
    {
        _interactionService = interactionService;
    }

    [SlashCommand("help", "Display the list of available commands")]
    [Cooldown(5)]
    public async Task HelpCommand(
        [Summary("command", "Specific command name to view details")] string? commandName = null)
    {
        try
        {
            if (string.IsNullOrEmpty(commandName))
            {
                // Display all commands
                await ShowAllCommands();
            }
            else
            {
                // Display details of a specific command
                await ShowSpecificCommand(commandName);
            }
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ An error occurred: {ex.Message}", ephemeral: true);
        }
    }

    private async Task ShowAllCommands()
    {
        var embed = new EmbedBuilder()
            .WithTitle("📋 List of Commands")
            .WithDescription("Below are all available commands:")
            .WithColor(Color.Blue)
            .WithThumbnailUrl(Context.Client.CurrentUser.GetAvatarUrl())
            .WithTimestamp(DateTimeOffset.Now)
            .WithFooter("Use /help <command name> to view details");

        var modules = _interactionService.Modules;
        var commandGroups = new Dictionary<string, List<string>>();

        foreach (var module in modules)
        {
            var moduleName = module.Name.Replace("Module", "") ?? "General";

            if (!commandGroups.ContainsKey(moduleName))
                commandGroups[moduleName] = new List<string>();

            foreach (var command in module.SlashCommands)
            {
                var commandInfo = $"`/{command.Name}` - {command.Description ?? "No description"}";
                commandGroups[moduleName].Add(commandInfo);
            }
        }

        foreach (var group in commandGroups)
        {
            if (group.Value.Any())
            {
                var commandList = string.Join("\n", group.Value);
                embed.AddField($"📁 {group.Key}", commandList, false);
            }
        }

        if (!commandGroups.Any() || !commandGroups.SelectMany(g => g.Value).Any())
        {
            embed.AddField("❌ No commands found", "Currently, there are no commands registered.", false);
        }

        await RespondAsync(embed: embed.Build());
    }

    private async Task ShowSpecificCommand(string commandName)
    {
        var command = _interactionService.SlashCommands
            .FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

        if (command == null)
        {
            var embed = new EmbedBuilder()
                .WithTitle("❌ Command not found")
                .WithDescription($"Command not found `{commandName}`")
                .WithColor(Color.Red)
                .WithTimestamp(DateTimeOffset.Now);

            await RespondAsync(embed: embed.Build(), ephemeral: true);
            return;
        }

        var detailEmbed = new EmbedBuilder()
            .WithTitle($"📖 Command details: /{command.Name}")
            .WithDescription(command.Description ?? "No description")
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.Now);

        // Add parameter information
        if (command.Parameters.Any())
        {
            var parameters = command.Parameters.Select(p =>
            {
                var required = p.IsRequired ? "**Required**" : "*Optional*";
                var defaultValue = p.DefaultValue != null ? $" (Default: `{p.DefaultValue}`)" : "";
                return $"• `{p.Name}` ({p.GetType().Name}) - {required}{defaultValue}\n  └ {p.Description ?? "No description"}";
            });

            detailEmbed.AddField("🔧 Parameters", string.Join("\n\n", parameters), false);
        }
        else
        {
            detailEmbed.AddField("🔧 Parameters", "This command has no parameters", false);
        }

        // Add usage example
        var example = $"/{command.Name}";
        if (command.Parameters.Any())
        {
            var exampleParams = command.Parameters.Take(2).Select(p =>
                p.IsRequired ? $"{p.Name}:value" : $"[{p.Name}:value]");
            example += " " + string.Join(" ", exampleParams);
        }

        detailEmbed.AddField("💡 Usage example", $"`{example}`", false);

        var backButton = new ComponentBuilder()
            .WithButton("⬅️ Back", "help_back", ButtonStyle.Secondary)
            .Build();

        await RespondAsync(embed: detailEmbed.Build(), components: backButton);
    }
}
