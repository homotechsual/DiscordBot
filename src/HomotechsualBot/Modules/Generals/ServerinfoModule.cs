using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Attributes;
using System.Reflection;
using System.Threading.Tasks;

namespace DiscordBot.Modules.Generals;

public class ServerinfoModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("serverinfo", "View current server information")]
    [Cooldown(5)]
    public async Task ServerInfoAsync()
    {
        // Get guild from GuildId if Context.Guild is null (cache issue)
        SocketGuild? guild = Context.Guild;
        
        if (guild == null && Context.Interaction.GuildId.HasValue)
        {
            guild = Context.Client.GetGuild(Context.Interaction.GuildId.Value);
        }
        
        if (guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true);
            return;
        }

        var owner = guild.Owner;
        var textChannels = guild.TextChannels.Count;
        var voiceChannels = guild.VoiceChannels.Count;
        var members = guild.MemberCount;
        var botVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        var embed = new EmbedBuilder()
            .WithTitle($"📌 Server Information: {guild.Name}")
            .WithThumbnailUrl(guild.IconUrl)
            .AddField("👑 Server Owner", owner?.Username ?? "Unknown", true)
            .AddField("🆔 Server ID", guild.Id, true)
            .AddField("👥 Members", members, true)
            .AddField("💬 Text Channels", textChannels, true)
            .AddField("🎤 Voice Channels", voiceChannels, true)
            .AddField("📅 Created At", guild.CreatedAt.ToString("dd/MM/yyyy"), true)
            .AddField("🤖 Bot Version", botVersion, true)
            .WithColor(Color.Blue)
            .Build();

        await RespondAsync(embed: embed);
    }

}
