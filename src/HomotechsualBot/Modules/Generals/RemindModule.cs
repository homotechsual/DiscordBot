using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;

namespace DiscordBot.Modules.Generals;
public class RemindModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("remind", "Remind you after a certain period of time")]
    public async Task Remind(
        [Summary("time", "Time (e.g., 10s, 5m, 2h)")] string time,
        [Summary("message", "Reminder message")] string message)
    {
        await DeferAsync(ephemeral: true); // to avoid timeout
        TimeSpan delay;

        try
        {
            delay = ParseTime(time);
        }
        catch
        {
            await FollowupAsync("Invalid time format. Example: 10s, 5m, 2h");
            return;
        }

        // create delay task
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            await Context.User.SendMessageAsync($"⏰ Reminder: {message}");
        });

        await FollowupAsync($"✅ I will remind you after {time}");
    }

    // Simple time parsing function
    private TimeSpan ParseTime(string input)
    {
        char unit = input[^1]; // last character
        int value = int.Parse(input[..^1]);

        return unit switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            'd' => TimeSpan.FromDays(value),
            'w' => TimeSpan.FromDays(value * 7),
            _ => throw new Exception("Invalid format")
        };
    }
}
