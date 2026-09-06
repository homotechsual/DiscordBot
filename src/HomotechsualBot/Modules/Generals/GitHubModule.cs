using Discord;
using Discord.Interactions;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Modules.Generals;

[Group("github", "Manage GitHub repository monitoring")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public class GitHubModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly HomotechsualBotContext _db;

    public GitHubModule(HomotechsualBotContext db)
    {
        _db = db;
    }

    [SlashCommand("enable", "Enable or disable GitHub monitoring globally")]
    public async Task EnableAsync([Summary("enabled", "Whether GitHub monitoring should be enabled")] bool enabled)
    {
        await AcknowledgeAsync();

        var settings = await GetOrCreateSettingsAsync();
        settings.Enabled = enabled;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync(enabled ? "✅ GitHub monitoring enabled." : "⛔ GitHub monitoring disabled.");
    }

    [SlashCommand("set-default-channel", "Set the fallback channel for GitHub notifications")]
    public async Task SetDefaultChannelAsync([Summary("channel", "Channel to post into")][ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel channel)
    {
        await AcknowledgeAsync();

        var settings = await GetOrCreateSettingsAsync();
        settings.DefaultChannelId = channel.Id;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync($"✅ GitHub notifications will default to <#{channel.Id}>.");
    }

    [SlashCommand("set-role", "Set the role mentioned on GitHub notifications (0 to clear)")]
    public async Task SetRoleAsync([Summary("role", "Role to mention, or omit to clear")] IRole? role = null)
    {
        await AcknowledgeAsync();

        var settings = await GetOrCreateSettingsAsync();
        settings.RoleId = role?.Id ?? 0;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync(role == null
            ? "✅ GitHub notification role mention cleared."
            : $"✅ GitHub notifications will mention <@&{role.Id}>.");
    }

    [SlashCommand("set-interval", "Set the GitHub polling interval in minutes")]
    public async Task SetIntervalAsync([Summary("minutes", "Polling interval in minutes (minimum 5)")] int minutes)
    {
        await AcknowledgeAsync();

        if (minutes < 5 || minutes > 1440)
        {
            await FollowupAsync("❌ Interval must be between 5 and 1440 minutes.");
            return;
        }

        var settings = await GetOrCreateSettingsAsync();
        settings.PollIntervalMinutes = minutes;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync($"✅ GitHub polling interval set to {minutes} minute(s).");
    }

    [SlashCommand("add-repo", "Track a GitHub repository")]
    public async Task AddRepositoryAsync(
        [Summary("repository", "Repository in owner/name form, or a GitHub URL")] string repository,
        [Summary("channel", "Channel for this repository's notifications")][ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel? channel = null)
    {
        await AcknowledgeAsync();

        if (!TryParseRepository(repository, out var owner, out var name))
        {
            await FollowupAsync("❌ Provide the repository as `owner/name` or a GitHub repository URL.");
            return;
        }

        var tracked = await FindRepositoryAsync(owner, name);
        if (tracked == null)
        {
            tracked = new GitHubTrackedRepository
            {
                Owner = owner,
                Name = name,
                ChannelId = channel?.Id ?? 0,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.GitHubTrackedRepositories.Add(tracked);
        }
        else
        {
            tracked.ChannelId = channel?.Id ?? tracked.ChannelId;
            tracked.IsEnabled = true;
            tracked.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        var channelInfo = tracked.ChannelId == 0 ? "the default channel" : $"<#{tracked.ChannelId}>";
        await FollowupAsync($"✅ Tracking `{tracked.FullName}` in {channelInfo}. Issues, pull requests, and releases are on by default; Actions is off.");
    }

    [SlashCommand("remove-repo", "Stop tracking a GitHub repository")]
    public async Task RemoveRepositoryAsync([Summary("repository", "Repository in owner/name form, or a GitHub URL")] string repository)
    {
        await AcknowledgeAsync();

        if (!TryParseRepository(repository, out var owner, out var name))
        {
            await FollowupAsync("❌ Provide the repository as `owner/name` or a GitHub repository URL.");
            return;
        }

        var tracked = await FindRepositoryAsync(owner, name);
        if (tracked == null)
        {
            await FollowupAsync($"ℹ `{owner}/{name}` is not tracked.");
            return;
        }

        _db.GitHubTrackedRepositories.Remove(tracked);
        await _db.SaveChangesAsync();

        await FollowupAsync($"✅ Stopped tracking `{owner}/{name}`.");
    }

    [SlashCommand("set-category-channel", "Route one GitHub notification category to a channel")]
    public async Task SetCategoryChannelAsync(
        [Summary("repository", "Repository in owner/name form, or a GitHub URL")] string repository,
        [Summary("category", "Notification category")]
        [Choice("Issues", "issues")]
        [Choice("Pull requests", "pulls")]
        [Choice("Actions", "actions")]
        [Choice("Releases", "releases")] string category,
        [Summary("channel", "Channel override; omit to use the repository/default channel")][ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel? channel = null)
    {
        await AcknowledgeAsync();

        if (!TryParseRepository(repository, out var owner, out var name))
        {
            await FollowupAsync("❌ Provide the repository as `owner/name` or a GitHub repository URL.");
            return;
        }

        var tracked = await FindRepositoryAsync(owner, name);
        if (tracked == null)
        {
            await FollowupAsync($"ℹ `{owner}/{name}` is not tracked. Add it with `/github add-repo` first.");
            return;
        }

        var channelId = channel?.Id ?? 0;
        switch (category)
        {
            case "issues":
                tracked.IssuesChannelId = channelId;
                break;
            case "pulls":
                tracked.PullRequestsChannelId = channelId;
                break;
            case "actions":
                tracked.ActionsChannelId = channelId;
                break;
            case "releases":
                tracked.ReleasesChannelId = channelId;
                break;
            default:
                await FollowupAsync("❌ Unknown category.");
                return;
        }

        tracked.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync(channel == null
            ? $"✅ `{category}` for `{tracked.FullName}` will use the repository/default channel."
            : $"✅ `{category}` for `{tracked.FullName}` will be sent to <#{channel.Id}>.");
    }

    [SlashCommand("toggle", "Enable or disable a notification category for a repository")]
    public async Task ToggleCategoryAsync(
        [Summary("repository", "Repository in owner/name form, or a GitHub URL")] string repository,
        [Summary("category", "Notification category")]
        [Choice("Issues", "issues")]
        [Choice("Pull requests", "pulls")]
        [Choice("Actions", "actions")]
        [Choice("Releases", "releases")] string category,
        [Summary("enabled", "Whether this category should be enabled")] bool enabled,
        [Summary("channel", "Optional channel override for this category")][ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel? channel = null)
    {
        await AcknowledgeAsync();

        if (!TryParseRepository(repository, out var owner, out var name))
        {
            await FollowupAsync("❌ Provide the repository as `owner/name` or a GitHub repository URL.");
            return;
        }

        var tracked = await FindRepositoryAsync(owner, name);
        if (tracked == null)
        {
            await FollowupAsync($"ℹ `{owner}/{name}` is not tracked. Add it with `/github add-repo` first.");
            return;
        }

        switch (category)
        {
            case "issues":
                tracked.IssuesEnabled = enabled;
                tracked.IssuesChannelId = channel?.Id ?? tracked.IssuesChannelId;
                break;
            case "pulls":
                tracked.PullRequestsEnabled = enabled;
                tracked.PullRequestsChannelId = channel?.Id ?? tracked.PullRequestsChannelId;
                break;
            case "actions":
                tracked.ActionsEnabled = enabled;
                tracked.ActionsChannelId = channel?.Id ?? tracked.ActionsChannelId;
                break;
            case "releases":
                tracked.ReleasesEnabled = enabled;
                tracked.ReleasesChannelId = channel?.Id ?? tracked.ReleasesChannelId;
                break;
            default:
                await FollowupAsync("❌ Unknown category.");
                return;
        }

        tracked.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var state = enabled ? "enabled" : "disabled";
        var channelInfo = channel == null ? string.Empty : $" routed to <#{channel.Id}>";
        await FollowupAsync($"✅ `{category}` {state} for `{tracked.FullName}`{channelInfo}.");
    }

    [SlashCommand("set-repo-enabled", "Enable or disable all notifications for a repository")]
    public async Task SetRepositoryEnabledAsync(
        [Summary("repository", "Repository in owner/name form, or a GitHub URL")] string repository,
        [Summary("enabled", "Whether the repository should be polled")] bool enabled)
    {
        await AcknowledgeAsync();

        if (!TryParseRepository(repository, out var owner, out var name))
        {
            await FollowupAsync("❌ Provide the repository as `owner/name` or a GitHub repository URL.");
            return;
        }

        var tracked = await FindRepositoryAsync(owner, name);
        if (tracked == null)
        {
            await FollowupAsync($"ℹ `{owner}/{name}` is not tracked.");
            return;
        }

        tracked.IsEnabled = enabled;
        tracked.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FollowupAsync(enabled
            ? $"✅ `{tracked.FullName}` will be polled."
            : $"⛔ `{tracked.FullName}` will no longer be polled.");
    }

    [SlashCommand("list", "List the current GitHub monitor configuration")]
    public async Task ListAsync()
    {
        await AcknowledgeAsync();

        var settings = await GetOrCreateSettingsAsync();
        var repositories = await _db.GitHubTrackedRepositories
            .AsNoTracking()
            .OrderBy(x => x.Owner).ThenBy(x => x.Name)
            .ToListAsync();

        var embed = new EmbedBuilder()
            .WithTitle("GitHub monitor")
            .WithColor(Color.Blue)
            .AddField("Enabled", settings.Enabled ? "Yes" : "No", true)
            .AddField("Default channel", settings.DefaultChannelId == 0 ? "Not set" : $"<#{settings.DefaultChannelId}>", true)
            .AddField("Poll interval", $"{settings.PollIntervalMinutes} minute(s)", true)
            .AddField("Mention role", settings.RoleId == 0 ? "None" : $"<@&{settings.RoleId}>", true);

        if (repositories.Count == 0)
        {
            embed.AddField("Tracked repositories", "None configured", false);
        }
        else
        {
            var lines = repositories.Select(repository =>
            {
                var categories = string.Join(", ", new[]
                {
                    DescribeCategory("issues", repository.IssuesEnabled, repository.IssuesChannelId),
                    DescribeCategory("pulls", repository.PullRequestsEnabled, repository.PullRequestsChannelId),
                    DescribeCategory("actions", repository.ActionsEnabled, repository.ActionsChannelId),
                    DescribeCategory("releases", repository.ReleasesEnabled, repository.ReleasesChannelId)
                });

                var repoChannel = repository.ChannelId == 0 ? "default" : $"<#{repository.ChannelId}>";
                var status = repository.IsEnabled ? "Enabled" : "Disabled";
                return $"• `{repository.FullName}` - {status} -> {repoChannel}\n  {categories}";
            });

            embed.AddField("Tracked repositories", Truncate(string.Join("\n", lines), 1024), false);
        }

        await FollowupAsync(embed: embed.Build());
    }

    private static string DescribeCategory(string label, bool enabled, ulong channelId)
    {
        var marker = enabled ? "✅" : "❌";
        var suffix = enabled && channelId != 0 ? $"→<#{channelId}>" : string.Empty;
        return $"{marker}{label}{suffix}";
    }

    private Task<GitHubTrackedRepository?> FindRepositoryAsync(string owner, string name) =>
        _db.GitHubTrackedRepositories.FirstOrDefaultAsync(x =>
            x.Owner.ToLower() == owner.ToLower() && x.Name.ToLower() == name.ToLower());

    private static bool TryParseRepository(string input, out string owner, out string name)
    {
        owner = string.Empty;
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var value = input.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
        {
            value = uri.AbsolutePath.Trim('/');
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        owner = segments[0].Trim();
        name = segments[1].Trim();

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return owner.Length > 0 && name.Length > 0;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    private Task AcknowledgeAsync() =>
        Context.Interaction.HasResponded ? Task.CompletedTask : DeferAsync(ephemeral: true);

    private async Task<GitHubMonitorSettings> GetOrCreateSettingsAsync()
    {
        var settings = await _db.GitHubMonitorSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (settings != null)
            return settings;

        settings = new GitHubMonitorSettings
        {
            Enabled = false,
            DefaultChannelId = 0,
            RoleId = 0,
            PollIntervalMinutes = 10,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.GitHubMonitorSettings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }
}
