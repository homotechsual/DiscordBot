using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class SingleMessageHistoryBackfillHostedService : BackgroundService
{
    private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(10);

    private readonly SingleMessageService _singleMessageService;
    private readonly ILogger<SingleMessageHistoryBackfillHostedService> _logger;

    public SingleMessageHistoryBackfillHostedService(
        SingleMessageService singleMessageService,
        ILogger<SingleMessageHistoryBackfillHostedService> logger)
    {
        _singleMessageService = singleMessageService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Single-message history backfill worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _singleMessageService.ProcessHistoryBackfillOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Single-message history backfill worker iteration failed.");
            }

            await Task.Delay(LoopDelay, stoppingToken);
        }

        _logger.LogInformation("Single-message history backfill worker stopped.");
    }
}
