using DiscordBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class HeartbeatMonitorService : BackgroundService
{
    private readonly HttpClient _httpClient;
    private readonly BotConfig _botConfig;
    private readonly ILogger<HeartbeatMonitorService> _logger;

    public HeartbeatMonitorService(HttpClient httpClient, BotConfig botConfig, ILogger<HeartbeatMonitorService> logger)
    {
        _httpClient = httpClient;
        _botConfig = botConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeat = _botConfig.Heartbeat;

        if (!heartbeat.Enabled)
        {
            _logger.LogInformation("Heartbeat monitor is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(heartbeat.PushUrl))
        {
            _logger.LogWarning("Heartbeat monitor is enabled but PushUrl is empty.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(15, heartbeat.IntervalSeconds));
        var startupDelay = TimeSpan.FromSeconds(Math.Max(0, heartbeat.StartupDelaySeconds));

        _logger.LogInformation("Heartbeat monitor started. Pinging every {IntervalSeconds}s.", interval.TotalSeconds);

        await PushHeartbeatAsync(stoppingToken, "startup");

        if (startupDelay > TimeSpan.Zero)
        {
            _logger.LogInformation("Waiting {StartupDelaySeconds}s startup delay before regular heartbeat loop.", startupDelay.TotalSeconds);
            await Task.Delay(startupDelay, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await PushHeartbeatAsync(stoppingToken, "interval");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var heartbeat = _botConfig.Heartbeat;
        if (heartbeat.Enabled && !string.IsNullOrWhiteSpace(heartbeat.PushUrl))
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(2, heartbeat.TimeoutSeconds)));
                using var response = await _httpClient.GetAsync(heartbeat.PushUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Sent final heartbeat ping during shutdown.");
                }
                else
                {
                    _logger.LogWarning("Final shutdown heartbeat push failed with status code {StatusCode}.", (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send final heartbeat ping during shutdown.");
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task PushHeartbeatAsync(CancellationToken stoppingToken, string phase)
    {
        var heartbeat = _botConfig.Heartbeat;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(2, heartbeat.TimeoutSeconds)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
            using var response = await _httpClient.GetAsync(heartbeat.PushUrl, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Heartbeat push failed with status code {StatusCode} during {Phase} phase.", (int)response.StatusCode, phase);
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Heartbeat push timed out after {TimeoutSeconds}s during {Phase} phase.", Math.Max(2, heartbeat.TimeoutSeconds), phase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat push failed during {Phase} phase.", phase);
        }
    }
}
