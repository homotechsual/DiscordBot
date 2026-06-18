using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Models;
using DiscordBot.Core.Data;
using DiscordBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Extensions;

/// <summary>
/// Extension methods for registering Discord bot services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Discord bot configuration, client, and related services into the DI container.
    /// </summary>
    public static IServiceCollection AddDiscordBot(this IServiceCollection services, IConfiguration configuration)
    {
        var botSection = configuration.GetSection("Bot");
        BotConfig botConfig;
        try
        {
            botConfig = botSection.Get<BotConfig>() ?? new BotConfig();
        }
        catch (Exception ex)
        {
            // Include only non-sensitive values to help diagnose invalid env var bindings.
            var diagnostics = string.Join(
                ", ",
                [
                    $"Bot:GuildId='{botSection["GuildId"] ?? "<null>"}'",
                    $"Bot:StatusMonitor:Enabled='{botSection["StatusMonitor:Enabled"] ?? "<null>"}'",
                    $"Bot:StatusMonitor:ChannelId='{botSection["StatusMonitor:ChannelId"] ?? "<null>"}'",
                    $"Bot:StatusMonitor:RoleId='{botSection["StatusMonitor:RoleId"] ?? "<null>"}'",
                    $"Bot:StatusMonitor:PollIntervalMinutes='{botSection["StatusMonitor:PollIntervalMinutes"] ?? "<null>"}'",
                    $"Bot:YoutubeMonitor:Enabled='{botSection["YoutubeMonitor:Enabled"] ?? "<null>"}'",
                    $"Bot:YoutubeMonitor:ForumChannelId='{botSection["YoutubeMonitor:ForumChannelId"] ?? "<null>"}'",
                    $"Bot:YoutubeMonitor:RoleId='{botSection["YoutubeMonitor:RoleId"] ?? "<null>"}'",
                    $"Bot:YoutubeMonitor:PollIntervalMinutes='{botSection["YoutubeMonitor:PollIntervalMinutes"] ?? "<null>"}'",
                    $"Bot:Heartbeat:Enabled='{botSection["Heartbeat:Enabled"] ?? "<null>"}'",
                    $"Bot:Heartbeat:IntervalSeconds='{botSection["Heartbeat:IntervalSeconds"] ?? "<null>"}'",
                    $"Bot:Heartbeat:StartupDelaySeconds='{botSection["Heartbeat:StartupDelaySeconds"] ?? "<null>"}'",
                    $"Bot:Heartbeat:TimeoutSeconds='{botSection["Heartbeat:TimeoutSeconds"] ?? "<null>"}'"
                ]);

            throw new InvalidOperationException(
                $"Failed to bind 'Bot' configuration. Check numeric/boolean environment values. {diagnostics}",
                ex);
        }

        // Backward-compatible token fallback for environments that only define common token keys.
        if (string.IsNullOrWhiteSpace(botConfig.Token))
        {
            botConfig.Token =
                configuration["DISCORD_TOKEN"] ??
                configuration["BOT_TOKEN"] ??
                string.Empty;
        }

        if (string.IsNullOrWhiteSpace(botConfig.Token))
        {
            var tokenSourceDiagnostics = string.Join(
                ", ",
                [
                    $"Bot:Token set='{!string.IsNullOrWhiteSpace(botSection["Token"])}'",
                    $"HOMOTECHSUALBOT_Bot__Token set='{!string.IsNullOrWhiteSpace(configuration["HOMOTECHSUALBOT_Bot__Token"])}'",
                    $"DISCORD_TOKEN set='{!string.IsNullOrWhiteSpace(configuration["DISCORD_TOKEN"])}'",
                    $"BOT_TOKEN set='{!string.IsNullOrWhiteSpace(configuration["BOT_TOKEN"])}'"
                ]);

            throw new InvalidOperationException(
                $"Bot token is missing. Set HOMOTECHSUALBOT_Bot__Token (preferred) or DISCORD_TOKEN/BOT_TOKEN. {tokenSourceDiagnostics}");
        }

        // Validate bound configuration values
        botConfig.Validate();

        services.AddSingleton(botConfig);

        var modLogConfig = configuration.GetSection("ModerationLog").Get<ModerationLogConfig>() ?? new ModerationLogConfig();
        services.AddSingleton(modLogConfig);

        var spamConfig = configuration.GetSection("CrossChannelSpam").Get<CrossChannelSpamConfig>() ?? new CrossChannelSpamConfig();
        services.AddSingleton(spamConfig);

        var moderationExemptionsConfig = configuration.GetSection("ModerationExemptions").Get<ModerationExemptionsConfig>() ?? new ModerationExemptionsConfig();
        services.AddSingleton(moderationExemptionsConfig);

        var commandAccessConfig = configuration.GetSection("CommandAccess").Get<CommandAccessConfig>() ?? new CommandAccessConfig();
        services.AddSingleton(commandAccessConfig);

        var socketConfig = new DiscordSocketConfig
        {
            // Request only non-privileged intents by default to avoid gateway close 4014.
            // This keeps slash-command bot startup resilient even when privileged intents
            // are not enabled in the Discord developer portal.
            AlwaysDownloadUsers = false,
            GatewayIntents = GatewayIntents.Guilds |
                             GatewayIntents.GuildMessages |
                             GatewayIntents.GuildMessageReactions |
                             GatewayIntents.DirectMessages |
                             GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Info
        };
        services.AddSingleton(socketConfig);
        services.AddSingleton<DiscordSocketClient>();
        services.AddHttpClient<YoutubeChannelSearchService>();

        services.AddSingleton(x =>
        {
            var client = x.GetRequiredService<DiscordSocketClient>();
            return new InteractionService(client);
        });

        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=./homotechsualbot.db";
        services.AddDbContext<HomotechsualBotContext>(options => options.UseSqlite(connectionString));

        services.AddSingleton<DiscordBotService>();
        services.AddSingleton<CommandAccessService>();
        services.AddSingleton<ModerationExemptionService>();
        services.AddSingleton<SingleMessageService>();
        services.AddSingleton<ModerationLogService>();
        services.AddSingleton<CrossChannelSpamDetector>();
        services.AddHttpClient<HeartbeatMonitorService>();
        if (botConfig.YoutubeMonitor.Enabled)
        {
            services.AddHostedService<YoutubeMonitorService>();
            services.AddHostedService<YoutubeFeedUrlsEndpointHostedService>();
        }
        services.AddHostedService<HeartbeatMonitorService>();
        services.AddHostedService<MetricsHostedService>();
        services.AddHostedService<SingleMessageHistoryBackfillHostedService>();

        return services;
    }
}

