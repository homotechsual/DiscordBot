using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;

namespace DiscordBot.Modules.Moderations;

public class WarningsModule : InteractionModuleBase<SocketInteractionContext>
{
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

        if (!WarnStorage.Warnings.ContainsKey(user.Id) || WarnStorage.Warnings[user.Id].Count == 0)
        {
            await RespondAsync($"✅ {user.Username} has no warnings.");
            return;
        }

        var warns = WarnStorage.Warnings[user.Id];
        var list = string.Join("\n", warns.Select((w, i) => $"{i + 1}. {w}"));

        var embed = new EmbedBuilder()
            .WithTitle($"⚠️ Warnings for {user.Username}")
            .WithDescription(list)
            .WithColor(Color.Orange)
            .Build();

        await RespondAsync(embed: embed);
    }
}
