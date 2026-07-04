using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Modules.Moderations;

public class WarningsModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public WarningsModule(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    [SlashCommand("warnings", "View warnings of a member")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task WarningsAsync(
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
            .OrderBy(w => w.CreatedAt)
            .ToListAsync();

        if (warnings.Count == 0)
        {
            await RespondAsync($"✅ {user.Username} has no warnings.");
            return;
        }

        var list = string.Join("\n", warnings.Select((w, i) => $"{i + 1}. {w.Reason}"));

        var embed = new EmbedBuilder()
            .WithTitle($"⚠️ Warnings for {user.Username}")
            .WithDescription(list)
            .WithColor(Color.Orange)
            .Build();

        await RespondAsync(embed: embed);
    }
}
