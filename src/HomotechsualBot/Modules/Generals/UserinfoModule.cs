using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Attributes;
using System.Threading.Tasks;

namespace DiscordBot.Modules.Generals;

public class UserinfoModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("userinfo", "Get user information")]
    [Cooldown(5)]
    public async Task UserInfoAsync(SocketUser? user = null)
    {
        user ??= Context.User; // if no user is provided, use the command caller

        var avatarUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();

        var embed = new EmbedBuilder()
            .WithTitle($"👤 User Information: {user.Username}")
            .WithThumbnailUrl(avatarUrl)
            .AddField("Name", user.Username, true)
            .AddField("Tag", $"#{user.Discriminator}", true)
            .AddField("ID", user.Id.ToString(), true)
            .AddField("Account Created", user.CreatedAt.ToString("dd/MM/yyyy HH:mm"), true)
            .WithColor(Color.Green)
            .Build();

        await RespondAsync(embed: embed);
    }
}
