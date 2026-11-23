using DiscordBot.Core.Data;
using DiscordBot.Models;
using DiscordBot.Modules.Generals;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomotechsualBot.Tests;

public class GitHubIssueSubscriptionTests
{
    [Fact]
    public async Task ListingFiltersUsesSqliteAndSortsChannelIdsInMemory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<HomotechsualBotContext>().UseSqlite(connection).Options;
        await using var db = new HomotechsualBotContext(options);
        await db.Database.MigrateAsync(cancellationToken);

        var repository = new GitHubTrackedRepository { Owner = "example", Name = "repo" };
        var otherRepository = new GitHubTrackedRepository { Owner = "example", Name = "other" };
        db.GitHubTrackedRepositories.AddRange(repository, otherRepository);
        await db.SaveChangesAsync(cancellationToken);
        db.GitHubIssueSubscriptions.AddRange(
            new GitHubIssueSubscription { RepositoryId = repository.Id, ChannelId = ulong.MaxValue },
            new GitHubIssueSubscription { RepositoryId = repository.Id, ChannelId = 1234567890123456789, IncludeLabels = "type:feature" },
            new GitHubIssueSubscription { RepositoryId = repository.Id, ChannelId = 42, ExcludeLabels = "type:feature" },
            new GitHubIssueSubscription { RepositoryId = otherRepository.Id, ChannelId = 1 });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var module = new GitHubModule(db);
        var subscriptions = await module.LoadIssueSubscriptionsAsync(repository.Id);

        Assert.Equal(new ulong[] { 42, 1234567890123456789, ulong.MaxValue },
            subscriptions.Select(x => x.ChannelId));
        Assert.All(subscriptions, subscription => Assert.Equal(repository.Id, subscription.RepositoryId));
        Assert.Equal("type:feature", subscriptions[0].ExcludeLabels);
        Assert.Equal("type:feature", subscriptions[1].IncludeLabels);
        Assert.Empty(await module.LoadIssueSubscriptionsAsync(0));
    }
}
