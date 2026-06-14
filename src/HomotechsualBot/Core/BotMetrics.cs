using Prometheus;

namespace DiscordBot.Core;

/// <summary>
/// Prometheus metric definitions for HomotechsualBot.
/// All metrics include a static <c>service="hudu-bot"</c> label so they can be
/// joined with Loki log streams that use the same label value.
/// </summary>
internal static class BotMetrics
{
    private static readonly string[] ServiceLabelNames = ["service"];
    private static readonly string[] ServiceLabelValues = ["hudu-bot"];

    /// <summary>Always 1 while the bot process is running.</summary>
    public static readonly IGauge BotUp = Metrics
        .CreateGauge(
            "bot_up",
            "1 while the bot process is running.",
            new GaugeConfiguration { LabelNames = ServiceLabelNames })
        .WithLabels(ServiceLabelValues);

    /// <summary>Number of enabled YouTube tracked channels loaded on the last successful poll.</summary>
    public static readonly IGauge YoutubeTrackedChannels = Metrics
        .CreateGauge(
            "bot_youtube_tracked_channels_total",
            "Number of enabled YouTube tracked channels loaded on the last successful poll.",
            new GaugeConfiguration { LabelNames = ServiceLabelNames })
        .WithLabels(ServiceLabelValues);

    /// <summary>1 when the YouTube Data API backoff is currently active; 0 otherwise.</summary>
    public static readonly IGauge YoutubeApiBackoffActive = Metrics
        .CreateGauge(
            "bot_youtube_api_backoff_active",
            "1 when the YouTube Data API quota/rate-limit backoff is active; 0 otherwise.",
            new GaugeConfiguration { LabelNames = ServiceLabelNames })
        .WithLabels(ServiceLabelValues);

    /// <summary>Unix timestamp (seconds) of the last YouTube poll attempt.</summary>
    public static readonly IGauge YoutubeLastPollTimestamp = Metrics
        .CreateGauge(
            "bot_youtube_last_poll_timestamp_seconds",
            "Unix timestamp of the last YouTube channel poll attempt.",
            new GaugeConfiguration { LabelNames = ServiceLabelNames })
        .WithLabels(ServiceLabelValues);

    /// <summary>1 while the YouTube feed URL observability endpoint is listening; 0 otherwise.</summary>
    public static readonly IGauge YoutubeFeedUrlsEndpointUp = Metrics
        .CreateGauge(
            "bot_youtube_feed_urls_endpoint_up",
            "1 while the YouTube feed URL observability endpoint is running; 0 otherwise.",
            new GaugeConfiguration { LabelNames = ServiceLabelNames })
        .WithLabels(ServiceLabelValues);

    /// <summary>Number of YouTube Atom feed URLs currently exposed by the observability endpoint.</summary>
    public static readonly IGauge YoutubeFeedUrlsExposed = Metrics
        .CreateGauge(
            "bot_youtube_feed_urls_exposed_total",
            "Number of YouTube Atom feed URLs currently exposed by the observability endpoint.",
            new GaugeConfiguration { LabelNames = ServiceLabelNames })
        .WithLabels(ServiceLabelValues);

    /// <summary>Number of enabled YouTube references that could not be resolved to a feed URL.</summary>
    public static readonly IGauge YoutubeUnresolvedReferences = Metrics
        .CreateGauge(
            "bot_youtube_unresolved_references_total",
            "Number of enabled YouTube references that could not be resolved to a feed URL.",
            new GaugeConfiguration { LabelNames = ServiceLabelNames })
        .WithLabels(ServiceLabelValues);
}
