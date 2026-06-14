using System;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json.Linq;
using DiscordBot.Models;

namespace DiscordBot.Commands.Fun;

public class FunModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HttpClient _http = new HttpClient();
    private static readonly Random _rand = new Random();
    private readonly BotConfig _config;

    public FunModule(BotConfig config)
    {
        _config = config;
    }

    private async Task<bool> CheckChannelAllowed()
    {
        if (_config.AllowedFunChannels == null || !_config.AllowedFunChannels.Any())
        {
            return true; // If no channels configured, allow all
        }

        if (!_config.AllowedFunChannels.Contains(Context.Channel.Id))
        {
            await RespondAsync("❌ Fun commands are not allowed in this channel.", ephemeral: true);
            return false;
        }

        return true;
    }

    // Meme command
    [SlashCommand("meme", "Get a random meme from the API")]
    public async Task MemeAsync()
    {
        if (!await CheckChannelAllowed()) return;

        var json = await _http.GetStringAsync("https://meme-api.com/gimme");
        var obj = JObject.Parse(json);

        string? title = obj["title"]?.ToString();
        string? url = obj["url"]?.ToString();
        string? postLink = obj["postLink"]?.ToString();

        if (title == null || url == null || postLink == null)
        {
            await RespondAsync("❌ Cannot fetch meme from API.");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithUrl(postLink)
            .WithImageUrl(url)
            .WithColor(Color.Green)
            .Build();

        await RespondAsync(embed: embed);
    }

    // 8ball command
    [SlashCommand("8ball", "Ask the magic 8-ball 🎱")]
    public async Task EightBallAsync([Summary("question", "Enter your question for the 8-ball")] string question)
    {
        if (!await CheckChannelAllowed()) return;

        string[] responses = {
            "Yes ✅", "No ❌", "Maybe 🤔", "Definitely 😎",
            "No way 😱", "Ask again later ⏳"
        };

        string answer = responses[_rand.Next(responses.Length)];
        await RespondAsync($"🎱 {answer}");
    }

    // Roll command
    [SlashCommand("roll", "Roll a dice (default 1-6)")]
    public async Task RollAsync([Summary("max", "Maximum value (default 6)")] int max = 6)
    {
        if (!await CheckChannelAllowed()) return;

        if (max < 2) max = 6; // avoid invalid input

        int result = _rand.Next(1, max + 1);
        await RespondAsync($"🎲 You rolled: **{result}** (1-{max})");
    }

    // Joke command
    [SlashCommand("joke", "Tell a random joke")]
    public async Task JokeAsync()
    {
        if (!await CheckChannelAllowed()) return;

        var json = await _http.GetStringAsync("https://v2.jokeapi.dev/joke/Any");
        var obj = JObject.Parse(json);

        string? joke;
        if (obj["type"]?.ToString() == "twopart")
        {
            joke = $"{obj["setup"]}\n{obj["delivery"]}";
        }
        else
        {
            joke = obj["joke"]?.ToString();
        }

        await RespondAsync($"😂 {joke}");
    }

    [SlashCommand("say", "The bot will repeat the content you entered")]
    public async Task SayCommand(
    [Summary("text", "The content the bot will send")] string text)
    {
        if (!await CheckChannelAllowed()) return;

        await RespondAsync(text);
    }

}
