using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Attributes;

namespace DiscordBot.Modules.Generals;

public class AvatarModule : InteractionModuleBase<SocketInteractionContext>
{

    [SlashCommand("avatar", "View a user's avatar")]
    [Cooldown(5)]
    public async Task AvatarAsync(
        [Summary("user", "The user whose avatar you want to view (leave blank to view your own)")]
        SocketUser? user = null)
    {
        user ??= Context.User;

        var embed = new EmbedBuilder()
            .WithTitle($"Avatar of {user.Username}")
            .WithImageUrl(user.GetAvatarUrl(size: 1024) ?? user.GetDefaultAvatarUrl())
            .WithColor(Color.Blue)
            .Build();

        await RespondAsync(embed: embed);
    }
}

