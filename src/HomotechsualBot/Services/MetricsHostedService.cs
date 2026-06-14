using DiscordBot.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace DiscordBot.Services;

/// <summary>
/// Starts a lightweight Prometheus <c>/metrics</c> HTTP endpoint so that
/// the observability stack can scrape bot health and channel-count gauges
/// without needing direct access to the bot's SQLite database.
/// </summary>
internal sealed class MetricsHostedService : BackgroundService
{
    private readonly int _port;
    private readonly ILogger<MetricsHostedService> _logger;
    private IMetricServer? _server;

    public MetricsHostedService(IConfiguration configuration, ILogger<MetricsHostedService> logger)
    {
        _port = configuration.GetValue<int>("Metrics:Port", 9092);
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _server = new MetricServer(port: _port);
        _server.Start();

        BotMetrics.BotUp.Set(1);

        _logger.LogInformation("Prometheus metrics endpoint started on port {Port}.", _port);

        return Task.Delay(Timeout.Infinite, stoppingToken)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        BotMetrics.BotUp.Set(0);

        if (_server is not null)
        {
            await _server.StopAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
