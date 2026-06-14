using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public sealed record YoutubeChannelSearchResult(string ChannelId, string ChannelName);

public sealed class YoutubeChannelSearchService
{
    private const string SearchEndpoint = "search?part=snippet&type=channel&maxResults=5&q={0}&key={1}";

    private readonly HttpClient _httpClient;
    private readonly BotConfig _config;
    private readonly ILogger<YoutubeChannelSearchService> _logger;

    public YoutubeChannelSearchService(HttpClient httpClient, BotConfig config, ILogger<YoutubeChannelSearchService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HomotechsualBot/1.0");
    }

    public async Task<YoutubeChannelSearchResult?> SearchAsync(string channelName, CancellationToken cancellationToken)
    {
        var trimmed = channelName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var apiKey = _config.YoutubeMonitor.YouTubeDataApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("YouTube Data API key is not configured; cannot search for channel {ChannelName}.", trimmed);
            return null;
        }

        var requestUrl = string.Format(
            SearchEndpoint,
            Uri.EscapeDataString(trimmed),
            Uri.EscapeDataString(apiKey));

        using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "YouTube channel search failed for {ChannelName}: HTTP {StatusCode}.",
                trimmed,
                (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
        {
            return null;
        }

        var candidates = new List<YoutubeChannelSearchResult>();

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idElement) ||
                !idElement.TryGetProperty("channelId", out var channelIdElement))
            {
                continue;
            }

            var channelId = channelIdElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(channelId))
            {
                continue;
            }

            var title = item.TryGetProperty("snippet", out var snippetElement) &&
                        snippetElement.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString()?.Trim()
                : null;

            candidates.Add(new YoutubeChannelSearchResult(channelId, string.IsNullOrWhiteSpace(title) ? trimmed : title!));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var exactTitleMatch = candidates.FirstOrDefault(candidate =>
            candidate.ChannelName.Equals(trimmed, StringComparison.OrdinalIgnoreCase));

        if (exactTitleMatch != null)
        {
            return exactTitleMatch;
        }

        return candidates[0];
    }
}

