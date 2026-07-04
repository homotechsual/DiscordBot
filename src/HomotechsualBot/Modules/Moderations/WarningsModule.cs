using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using DiscordBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Modules.Moderations;

[Group("warnings", "View or manage member warnings")]
public class WarningsModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxSelectableWarnings = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WarningService _warningService;

    public WarningsModule(IServiceScopeFactory scopeFactory, WarningService warningService)
    {
        _scopeFactory = scopeFactory;
        _warningService = warningService;
    }

    [SlashCommand("list", "View warnings of a member")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task ListAsync(
        [Summary(description: "User to check warnings for")] SocketGuildUser? user = null)
    {
        // If no user is provided → default to the command caller
        user ??= Context.User as SocketGuildUser;

        if (user == null)
        {
            await RespondAsync("❌ User not found.", ephemeral: true);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        var warnings = await db.UserWarnings
            .Where(w => w.GuildId == Context.Guild.Id && w.UserId == user.Id)
            .OrderBy(w => w.Id)
            .ToListAsync();

        if (warnings.Count == 0)
        {
            await RespondAsync($"✅ {user.Username} has no warnings.");
            return;
        }

        var list = string.Join("\n", warnings.Select(w => $"#{w.Id} — {w.Reason} — {w.CreatedAt:yyyy-MM-dd}"));

        var embed = new EmbedBuilder()
            .WithTitle($"⚠️ Warnings for {user.Username}")
            .WithDescription(list)
            .WithColor(Color.Orange)
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("clear", "Remove one, several, or all warnings for a member")]
    [RequireUserPermission(GuildPermission.KickMembers)]
    public async Task ClearAsync(
        [Summary(description: "User to clear warnings for")] SocketGuildUser user)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        var warnings = await db.UserWarnings
            .Where(w => w.GuildId == Context.Guild.Id && w.UserId == user.Id)
            .OrderBy(w => w.Id)
            .ToListAsync();

        if (warnings.Count == 0)
        {
            await RespondAsync($"✅ {user.Username} has no warnings to clear.", ephemeral: true);
            return;
        }

        var shown = warnings.Count > MaxSelectableWarnings
            ? warnings[^MaxSelectableWarnings..]
            : warnings;

        var description = string.Join("\n", shown.Select(w => $"#{w.Id} — {w.Reason} — {w.CreatedAt:yyyy-MM-dd}"));
        var embedBuilder = new EmbedBuilder()
            .WithTitle($"🗑️ Clear warnings for {user.Username}")
            .WithDescription(description)
            .WithColor(Color.Orange);

        if (warnings.Count > MaxSelectableWarnings)
        {
            embedBuilder.WithFooter(
                $"Showing the {MaxSelectableWarnings} most recent of {warnings.Count} warnings — remove some to see older ones.");
        }

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId($"warn_clear_select:{user.Id}:{Context.Guild.Id}")
            .WithPlaceholder("Select warning(s) to remove")
            .WithMinValues(1)
            .WithMaxValues(shown.Count);

        foreach (var warning in shown)
        {
            var label = warning.Reason.Length > 80 ? warning.Reason[..80] : warning.Reason;
            selectMenu.AddOption($"#{warning.Id} — {label}", warning.Id.ToString());
        }

        var components = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .WithButton($"🗑️ Remove All ({warnings.Count})", $"warn_clear_all:{user.Id}:{Context.Guild.Id}", ButtonStyle.Danger)
            .Build();

        await RespondAsync(embed: embedBuilder.Build(), components: components, ephemeral: true);
    }

    [ComponentInteraction("warn_clear_select:*:*")]
    public async Task ClearSelectAsync(string userId, string guildId)
    {
        if (Context.User is not SocketGuildUser actor || !actor.GuildPermissions.KickMembers)
        {
            await RespondAsync("❌ You need the **Kick Members** permission to use this.", ephemeral: true);
            return;
        }

        if (!ulong.TryParse(userId, out var targetUserId) || !ulong.TryParse(guildId, out var targetGuildId))
        {
            await RespondAsync("❌ Invalid button data.", ephemeral: true);
            return;
        }

        var component = (SocketMessageComponent)Context.Interaction;
        var selectedIds = component.Data.Values.Select(int.Parse).ToList();

        try
        {
            var removedCount = await _warningService.RemoveWarningsAsync(targetGuildId, targetUserId, selectedIds);

            await component.UpdateAsync(props =>
            {
                props.Content = $"✅ Removed {removedCount} warning(s) for <@{targetUserId}>.";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = $"❌ Failed to remove warnings: {ex.Message}";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
        }
    }

    [ComponentInteraction("warn_clear_all:*:*")]
    public async Task ClearAllAsync(string userId, string guildId)
    {
        if (Context.User is not SocketGuildUser actor || !actor.GuildPermissions.KickMembers)
        {
            await RespondAsync("❌ You need the **Kick Members** permission to use this.", ephemeral: true);
            return;
        }

        if (!ulong.TryParse(userId, out var targetUserId) || !ulong.TryParse(guildId, out var targetGuildId))
        {
            await RespondAsync("❌ Invalid button data.", ephemeral: true);
            return;
        }

        var component = (SocketMessageComponent)Context.Interaction;

        try
        {
            var removedCount = await _warningService.ClearWarningsAsync(targetGuildId, targetUserId);

            await component.UpdateAsync(props =>
            {
                props.Content = $"✅ Removed all {removedCount} warning(s) for <@{targetUserId}>.";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = $"❌ Failed to remove warnings: {ex.Message}";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
        }
    }
}
