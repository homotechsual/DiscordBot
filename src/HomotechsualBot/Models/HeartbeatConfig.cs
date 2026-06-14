namespace DiscordBot.Models;

public class HeartbeatConfig
{
    public bool Enabled { get; set; }
    public string PushUrl { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; } = 60;
    public int StartupDelaySeconds { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Validates the heartbeat configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (Enabled)
        {
            if (string.IsNullOrWhiteSpace(PushUrl))
                throw new InvalidOperationException("HeartbeatConfig: When enabled, PushUrl is required and cannot be empty. Check HOMOTECHSUALBOT_Bot__Heartbeat__PushUrl environment variable.");

            if (!Uri.TryCreate(PushUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException($"HeartbeatConfig: PushUrl must be a valid HTTP URL. Current value: '{PushUrl}'.");

            if (IntervalSeconds <= 0)
                throw new InvalidOperationException($"HeartbeatConfig: IntervalSeconds must be greater than 0. Current value: {IntervalSeconds}.");

            if (TimeoutSeconds <= 0)
                throw new InvalidOperationException($"HeartbeatConfig: TimeoutSeconds must be greater than 0. Current value: {TimeoutSeconds}.");
        }

        if (StartupDelaySeconds < 0)
            throw new InvalidOperationException($"HeartbeatConfig: StartupDelaySeconds cannot be negative. Current value: {StartupDelaySeconds}.");
    }
}

