using DiscordBot.Extensions;
using DiscordBot.Services;
using DiscordBot.Core.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

// Create logs directory
var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logsDir);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {SourceContext:l} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(logsDir, "homotechsualbot-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 50 * 1024 * 1024,   // 50 MB per file
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext:l} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

Log.Information("🤖 HomotechsualBot starting...");
Log.Information("Starting at {StartTime:o}", DateTime.UtcNow);

try
{
    var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables("HOMOTECHSUALBOT_");
                    config.AddUserSecrets<Program>(optional: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddDiscordBot(context.Configuration);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                    logging.AddFilter("System.Net.Http.HttpClient.Default", LogLevel.Warning);
                    logging.AddSerilog();
                })
                .Build();

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        await db.Database.MigrateAsync();
    }

    var botService = host.Services.GetRequiredService<DiscordBotService>();

    await botService.StartAsync();
    await host.StartAsync();
    await host.WaitForShutdownAsync();

    Log.Information("Stopping bot service...");
    await botService.StopAsync();
    Log.Information("Stopping host...");
    await host.StopAsync();
}
catch (TaskCanceledException ex)
{
    Log.Fatal(ex, "⚠️ TASK CANCELLED during bot startup");
}
catch (Exception ex)
{
    Log.Fatal(ex, "❌ Application terminated unexpectedly");
}
finally
{
    Log.Information("Closing Serilog...");
    await Log.CloseAndFlushAsync();
}

