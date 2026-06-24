using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Interactions;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;

namespace DiscordBot.Services;

/// <summary>
/// Main bot service that manages lifecycle events, logging, and command handling.
/// </summary>
public class DiscordBotService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _services;
    private readonly BotConfig _config;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly SingleMessageService _singleMessageService;
    private readonly CrossChannelSpamDetector _spamDetector;
    private readonly CommandAccessService _commandAccessService;
    private readonly EventAuditLogService _eventAuditLogService;
    private readonly TaskCompletionSource<bool> _readyCompletionSource = new();
    private int _commandsRegistered;

    public DateTime StartTime { get; private set; }

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IServiceProvider services,
        BotConfig config,
        ILogger<DiscordBotService> logger,
        SingleMessageService singleMessageService,
        CrossChannelSpamDetector spamDetector,
        CommandAccessService commandAccessService,
        EventAuditLogService eventAuditLogService)
    {
        _client = client;
        _interactionService = interactionService;
        _services = services;
        _config = config;
        _logger = logger;
        _singleMessageService = singleMessageService;
        _spamDetector = spamDetector;
        _commandAccessService = commandAccessService;
        _eventAuditLogService = eventAuditLogService;

        // Subscribe to Discord events
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.Connected += ConnectedAsync;
        _client.Disconnected += DisconnectedAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        _client.GuildAvailable += GuildAvailableAsync;
        _client.MessageReceived += _singleMessageService.HandleMessageAsync;
        _client.MessageReceived += _spamDetector.HandleMessageAsync;
        _client.MessageDeleted += _eventAuditLogService.HandleMessageDeletedAsync;
        _client.UserLeft += _eventAuditLogService.HandleUserLeftAsync;

        // Log interaction service events
        _interactionService.Log += LogAsync;
        _interactionService.SlashCommandExecuted += SlashCommandExecutedAsync;
    }

    /// <summary>
    /// Starts the bot and connects to Discord.
    /// </summary>
    public async Task StartAsync()
    {
        StartTime = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(_config.Token))
        {
            _logger.LogError("Bot token is not configured. Please set the token using the environment variable, user secrets or appsettings.json. Exiting...");
            return;
        }

        // Load slash command modules before connecting
        _logger.LogInformation("Loading interaction service modules...");
        await _interactionService.AddModulesAsync(typeof(Program).Assembly, _services);
        
        var modules = _interactionService.Modules;
        _logger.LogInformation("Loaded {ModuleCount} modules:", modules.Count);
        foreach (var module in modules)
        {
            _logger.LogInformation("  Module: {ModuleName} with {CommandCount} slash commands, {ComponentCount} component commands", 
                module.Name, module.SlashCommands.Count, module.ComponentCommands.Count);
            foreach (var command in module.SlashCommands)
            {
                _logger.LogInformation("    - /{CommandName}: {Description}", command.Name, command.Description);
            }
            foreach (var component in module.ComponentCommands)
            {
                _logger.LogInformation("    - Component: {CustomId}", component.Name);
            }
        }

        // Login & connect
        await _client.LoginAsync(TokenType.Bot, _config.Token);
        await _client.StartAsync();

        // Wait for the Ready event to complete
        _logger.LogInformation("Waiting for bot to be ready...");
        await _readyCompletionSource.Task;
        _logger.LogInformation("Bot is ready and guilds are cached");
    }

    /// <summary>
    /// Stops the bot and logs out from Discord.
    /// </summary>
    public async Task StopAsync()
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    /// <summary>
    /// Handles log events from Discord.NET and maps them to Microsoft.Extensions.Logging levels.
    /// </summary>
    private Task LogAsync(LogMessage log)
    {
        // Discord.Net can emit benign gateway warnings (e.g., uncached MESSAGE_UPDATE channel IDs).
        // Keep visibility at Debug level instead of Warning to avoid alert fatigue.
        if (TryGetDowngradedGatewayNoise(log, out var downgradedMessage))
        {
            _logger.LogDebug("[{BotName}] {Source}: {Message}", "HomotechsualBot", log.Source, downgradedMessage);
            return Task.CompletedTask;
        }

        var logLevel = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        var message = string.IsNullOrWhiteSpace(log.Message) ? "(empty message)" : log.Message;
        _logger.Log(logLevel, log.Exception, "[{BotName}] {Source}: {Message}", "HomotechsualBot", log.Source, message);
        return Task.CompletedTask;
    }

    private static bool TryGetDowngradedGatewayNoise(LogMessage log, out string downgradedMessage)
    {
        downgradedMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(log.Source) ||
            !log.Source.Contains("Gateway", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var message = log.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message) ||
            string.Equals(message, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message, "(empty message)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message, "(null)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message, "<null>", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message, "[null]", StringComparison.OrdinalIgnoreCase) ||
            IsBenignUnknownChannelMessageUpdate(message))
        {
            downgradedMessage = string.IsNullOrWhiteSpace(message) ? "(gateway noise)" : message;
            return true;
        }

        return false;
    }

    private static bool IsBenignUnknownChannelMessageUpdate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        // Discord can emit MESSAGE_UPDATE events for channels not currently cached.
        // This does not impact slash command handling and is safe to ignore.
        return message.Contains("Unknown Channel", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("MESSAGE_UPDATE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Called when the bot has successfully connected and is ready.
    /// </summary>
    private Task ReadyAsync()
    {
        _logger.LogInformation("Bot {Username} is connected and ready!", _client.CurrentUser.Username);
        _logger.LogInformation("Client has {GuildCount} guilds in cache", _client.Guilds.Count);
        
        foreach (var guild in _client.Guilds)
        {
            _logger.LogInformation("Guild: {GuildName} ({GuildId})", guild.Name, guild.Id);
        }

        // Don't block - register commands asynchronously
        _ = Task.Run(async () =>
        {
            if (Interlocked.CompareExchange(ref _commandsRegistered, 1, 0) != 0)
            {
                _logger.LogDebug("[DiscordLifecycle] Skipping slash command re-registration on reconnect.");
                return;
            }

            try
            {
                // Wait a bit for guilds to be available
                await Task.Delay(2000);

                if (_client.ConnectionState != ConnectionState.Connected)
                {
                    Interlocked.Exchange(ref _commandsRegistered, 0);
                    _logger.LogInformation(
                        "Skipping slash command registration because client state is {State}. Will retry on next Ready.",
                        _client.ConnectionState);
                    return;
                }

                _logger.LogInformation("After delay, client has {GuildCount} guilds", _client.Guilds.Count);
                foreach (var guild in _client.Guilds)
                {
                    _logger.LogInformation("Guild now available: {GuildName} ({GuildId})", guild.Name, guild.Id);
                }

                if (_config.GuildId.HasValue)
                {
                    await _interactionService.RegisterCommandsToGuildAsync(_config.GuildId.Value);
                    _logger.LogInformation("Slash commands registered to guild {GuildId}", _config.GuildId.Value);
                }
                else
                {
                    await _interactionService.RegisterCommandsGloballyAsync();
                    _logger.LogInformation("Slash commands registered globally");
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _commandsRegistered, 0);
                if (_client.ConnectionState != ConnectionState.Connected)
                {
                    _logger.LogInformation(
                        ex,
                        "Skipped slash command registration due to transient disconnect (state: {State}). Will retry on next Ready.",
                        _client.ConnectionState);
                }
                else if (TryClassifySlashRegistrationFailure(ex, out var category, out var guidance, out var httpCode, out var discordCode))
                {
                    _logger.LogWarning(
                        ex,
                        "Slash command registration failed: {Category}. GuildId={GuildId}, HttpCode={HttpCode}, DiscordCode={DiscordCode}. {Guidance}",
                        category,
                        _config.GuildId,
                        httpCode,
                        discordCode,
                        guidance);
                }
                else
                {
                    _logger.LogError(ex, "Failed to register slash commands. Background services will still start.");
                }
            }
            finally
            {
                _readyCompletionSource.TrySetResult(true);
            }
        });
        
        return Task.CompletedTask;
    }

    private static bool TryClassifySlashRegistrationFailure(
        Exception ex,
        out string category,
        out string guidance,
        out HttpStatusCode? httpCode,
        out DiscordErrorCode? discordCode)
    {
        category = string.Empty;
        guidance = string.Empty;
        httpCode = null;
        discordCode = null;

        var httpException = FindHttpException(ex);
        if (httpException is null)
        {
            return false;
        }

        httpCode = httpException.HttpCode;
        discordCode = httpException.DiscordCode;

        if (httpException.HttpCode == HttpStatusCode.Forbidden)
        {
            category = "Missing access/permissions for guild command registration";
            guidance = "Verify the bot is in the target guild and re-authorize with both 'bot' and 'applications.commands' scopes for this application.";
            return true;
        }

        if (httpException.HttpCode == HttpStatusCode.NotFound)
        {
            category = "Target guild or endpoint not found";
            guidance = "Verify HOMOTECHSUALBOT_Bot__GuildId points to the guild where the app is installed.";
            return true;
        }

        if (httpException.HttpCode == HttpStatusCode.Unauthorized)
        {
            category = "Unauthorized when registering commands";
            guidance = "Verify bot token and application identity are correct and not rotated/out of sync.";
            return true;
        }

        return false;
    }

    private static HttpException? FindHttpException(Exception ex)
    {
        if (ex is HttpException direct)
        {
            return direct;
        }

        return ex.InnerException as HttpException;
    }

    private Task ConnectedAsync()
    {
        _logger.LogInformation("[DiscordLifecycle] Connected to Discord as {Username} (state: {State})", _client.CurrentUser?.Username ?? "unknown", _client.ConnectionState);
        return Task.CompletedTask;
    }

    private Task DisconnectedAsync(Exception? ex)
    {
        if (ex is null)
        {
            _logger.LogInformation("[DiscordLifecycle] Disconnected from Discord (state: {State})", _client.ConnectionState);
        }
        else
        {
            _logger.LogInformation(
                "[DiscordLifecycle] Disconnected from Discord (state: {State}, reason: {Reason})",
                _client.ConnectionState,
                ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a guild becomes available.
    /// </summary>
    private Task GuildAvailableAsync(SocketGuild guild)
    {
        _logger.LogInformation("Guild available: {GuildName} ({GuildId})", guild.Name, guild.Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a slash command is executed. This is the correct place to handle precondition
    /// failures because ExecuteCommandAsync returns success on dispatch even when preconditions fail.
    /// </summary>
    private async Task SlashCommandExecutedAsync(SlashCommandInfo command, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            _logger.LogError("Slash command {CommandName} failed: {Error}", command.Name, result.ErrorReason);
            var userMessage = BuildInteractionErrorMessage(result);
            if (context.Interaction is SocketInteraction socketInteraction)
                await SendInteractionErrorAsync(socketInteraction, userMessage);
        }
        else
        {
            _logger.LogInformation("Slash command {CommandName} executed successfully", command.Name);
        }
    }

    /// <summary>
    /// Handle slash command execution
    /// </summary>
    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            var receivedTime = DateTime.Now;
            var interactionCreatedAt = interaction.CreatedAt.UtcDateTime;
            var age = (receivedTime.ToUniversalTime() - interactionCreatedAt).TotalSeconds;
            
            _logger.LogInformation("[{Time}] Interaction {InteractionId} created at {CreatedAt}, age: {Age:F3}s, type: {Type}", 
                receivedTime.ToString("HH:mm:ss.fff"), interaction.Id, interactionCreatedAt.ToString("HH:mm:ss.fff"), age, interaction.Type);
            
            // Log custom ID for component interactions
            if (interaction is SocketMessageComponent component)
            {
                _logger.LogInformation("Component interaction with CustomId: '{CustomId}'", component.Data.CustomId);
            }

            if (interaction is SocketSlashCommand slashCommand &&
                _commandAccessService.TryGetBlockReason(slashCommand.CommandName, interaction.Channel.Id, out var blockReason))
            {
                await interaction.RespondAsync($"❌ {blockReason}", ephemeral: true);
                return;
            }
            
            var ctx = new SocketInteractionContext(_client, interaction);
            
            var beforeExecute = DateTime.Now;
            _logger.LogInformation("[{Time}] About to execute command (processing delay: {Delay}ms)", 
                beforeExecute.ToString("HH:mm:ss.fff"), (beforeExecute - receivedTime).TotalMilliseconds);
            
            var result = await _interactionService.ExecuteCommandAsync(ctx, _services);
            
            // Slash command results (including precondition failures) are handled in
            // SlashCommandExecutedAsync via the SlashCommandExecuted event.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling interaction");

            if (interaction.Type == InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync()
                    .ContinueWith(async msg => await (await msg).DeleteAsync());
        }
    }

    private async Task SendInteractionErrorAsync(SocketInteraction interaction, string message)
    {
        try
        {
            if (interaction.HasResponded)
                await interaction.FollowupAsync(message, ephemeral: true);
            else
                await interaction.RespondAsync(message, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send interaction error message to user.");
        }
    }

    private static string BuildInteractionErrorMessage(IResult result)
    {
        if (result.Error == InteractionCommandError.UnmetPrecondition)
        {
            var reason = (result.ErrorReason ?? string.Empty).Trim();

            // Cooldown errors carry the retry time in the reason — surface them cleanly.
            if (reason.Contains("too quickly", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("cooldown", StringComparison.OrdinalIgnoreCase))
            {
                return $"⏱️ {reason}";
            }

            var normalized = reason.ToLower(CultureInfo.InvariantCulture);

            if (normalized.Contains("permission") || normalized.Contains("manage") || normalized.Contains("administrator"))
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    return "❌ This command couldn't run because of missing permissions for you or the bot in this channel/server.";
                }

                return
                    "❌ This command couldn't run because of missing permissions for you or the bot in this channel/server." +
                    $"\nℹ️ Details: {reason}";
            }

            return $"❌ Command requirements not met: {reason}";
        }

        if (result.Error == InteractionCommandError.UnknownCommand)
            return "❌ I couldn't find that command. Please try again in a moment.";

        if (result.Error == InteractionCommandError.BadArgs)
            return "❌ Invalid command arguments. Please check the command options and try again.";

        if (result.Error == InteractionCommandError.Exception)
            return "❌ Something went wrong while running that command.";

        return $"❌ Command failed: {result.ErrorReason}";
    }
}

