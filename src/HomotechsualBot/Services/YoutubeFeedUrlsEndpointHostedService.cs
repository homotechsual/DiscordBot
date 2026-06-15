using DiscordBot.Core;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

internal sealed class YoutubeFeedUrlsEndpointHostedService : BackgroundService
{
    private const string EndpointPath = "/observability/youtube-feed-urls";

    private readonly int _port;
    private readonly IServiceProvider _serviceProvider;
    private readonly BotConfig _botConfig;
    private readonly ILogger<YoutubeFeedUrlsEndpointHostedService> _logger;
    private WebApplication? _app;

    public YoutubeFeedUrlsEndpointHostedService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        BotConfig botConfig,
        ILogger<YoutubeFeedUrlsEndpointHostedService> logger)
    {
        _port = configuration.GetValue<int>("Metrics:FeedUrlsPort", 9192);
        _serviceProvider = serviceProvider;
        _botConfig = botConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ApplicationName = typeof(YoutubeFeedUrlsEndpointHostedService).Assembly.FullName
        });

        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        _app = app;

        app.MapGet(EndpointPath, async (CancellationToken cancellationToken) =>
        {
            var payload = await BuildPayloadAsync(cancellationToken);
            return Results.Json(payload);
        });

        app.MapGet("/", () => Results.NotFound());

        BotMetrics.YoutubeFeedUrlsEndpointUp.Set(1);
        _logger.LogInformation("YouTube feed URL endpoint started on port {Port} at {Path}.", _port, EndpointPath);

        try
        {
            await app.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            BotMetrics.YoutubeFeedUrlsEndpointUp.Set(0);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        BotMetrics.YoutubeFeedUrlsEndpointUp.Set(0);

        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
            _app = null;
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task<YoutubeFeedUrlsPayload> BuildPayloadAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();

        var settings = await db.YoutubeMonitorSettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var channels = await db.YoutubeTrackedChannels
            .AsNoTracking()
            .OrderBy(x => x.ChannelName)
            .ToListAsync(cancellationToken);

        var enabledChannels = channels.Where(x => x.IsEnabled).ToList();
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.Ordinal);

        foreach (var channel in enabledChannels)
        {
            var original = channel.ChannelId?.Trim();
            if (string.IsNullOrWhiteSpace(original))
            {
                continue;
            }

            if (!YoutubeChannelReferenceParser.TryNormalize(original, out var normalized) || string.IsNullOrWhiteSpace(normalized))
            {
                unresolved.Add(original);
                continue;
            }

            if (normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
                    uri.AbsolutePath.Contains("/feeds/videos.xml", StringComparison.OrdinalIgnoreCase))
                {
                    urls.Add(normalized);
                    continue;
                }

                unresolved.Add(original);
                continue;
            }

            if (normalized.StartsWith("UC", StringComparison.OrdinalIgnoreCase) && normalized.Length >= 20)
            {
                urls.Add($"https://www.youtube.com/feeds/videos.xml?channel_id={normalized}");
                continue;
            }

            unresolved.Add(original);
        }

        BotMetrics.YoutubeTrackedChannels.Set(enabledChannels.Count);
        BotMetrics.YoutubeFeedUrlsExposed.Set(urls.Count);
        BotMetrics.YoutubeUnresolvedReferences.Set(unresolved.Count);

        return new YoutubeFeedUrlsPayload(
            Service: "homotechsual-bot",
            GeneratedAtUtc: DateTime.UtcNow,
            YoutubeMonitorEnabled: settings?.Enabled ?? _botConfig.YoutubeMonitor.Enabled,
            ConfiguredChannelCount: channels.Count,
            EnabledChannelCount: enabledChannels.Count,
            FeedUrls: urls.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            UnresolvedReferences: unresolved.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    private sealed record YoutubeFeedUrlsPayload(
        string Service,
        DateTime GeneratedAtUtc,
        bool YoutubeMonitorEnabled,
        int ConfiguredChannelCount,
        int EnabledChannelCount,
        string[] FeedUrls,
        string[] UnresolvedReferences);
}
