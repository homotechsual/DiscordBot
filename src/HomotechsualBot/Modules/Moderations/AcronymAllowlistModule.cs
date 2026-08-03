using System.Text;
using Discord;
using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Modules.Moderations;

[Group("acronyms", "Manage all-caps acronym allowlist")]
public class AcronymAllowlistModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AcronymAllowlistService _allowlistService;

    public AcronymAllowlistModule(AcronymAllowlistService allowlistService)
    {
        _allowlistService = allowlistService;
    }

    [SlashCommand("list", "List allowlisted acronyms")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task ListAsync(string language = "all", string search = "", bool includeDisabled = false, int limit = 50)
    {
        var rows = await _allowlistService.ListAsync(Math.Clamp(limit, 1, 200), language, search, includeDisabled);

        if (rows.Count == 0)
        {
            await RespondAsync("No acronyms matched the current filters.", ephemeral: true);
            return;
        }

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            var status = row.IsEnabled ? "enabled" : "disabled";
            sb.AppendLine($"- {row.Acronym} ({row.Language}, {status})");
        }

        await RespondAsync($"Acronyms ({rows.Count}) for language='{language}', search='{search}':\n{sb}", ephemeral: true);
    }

    [SlashCommand("add", "Add or re-enable an allowlisted acronym")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task AddAsync(string acronym, string language = "any")
    {
        var result = await _allowlistService.AddOrEnableAsync(acronym, language, Context.User.Id);
        await RespondAsync(result.Message, ephemeral: true);
    }

    [SlashCommand("remove", "Disable an allowlisted acronym")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task RemoveAsync(string acronym)
    {
        var result = await _allowlistService.DisableAsync(acronym);
        await RespondAsync(result.Message, ephemeral: true);
    }
}
