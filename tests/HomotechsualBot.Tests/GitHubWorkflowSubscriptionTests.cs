using System.Net;
using System.Reflection;
using System.Text;
using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using DiscordBot.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomotechsualBot.Tests;

public class GitHubWorkflowSubscriptionTests
{
    [Theory]
    [InlineData("deploy-next.yml", true)]
    [InlineData("deploy-next.yaml", true)]
    [InlineData("12345", true)]
    [InlineData("deploy-next", false)]
    [InlineData("../deploy.yml", false)]
    [InlineData("0", false)]
    public void ValidatesWorkflowIdentifier(string value, bool expected) =>
        Assert.Equal(expected, GitHubWorkflowSubscription.IsValidWorkflow(value));

    [Fact]
    public async Task MigrationPreservesExistingFeedState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<HomotechsualBotContext>().UseSqlite(connection).Options;
        await using var db = new HomotechsualBotContext(options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(migrations[^2], TestContext.Current.CancellationToken);
        db.FeedPostStates.Add(new FeedPostState { FeedType = "existing", SourceId = "source", LastPostedItemId = "42" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await migrator.MigrateAsync(null, TestContext.Current.CancellationToken);
        Assert.Equal("42", (await db.FeedPostStates.SingleAsync(x => x.FeedType == "existing", TestContext.Current.CancellationToken)).LastPostedItemId);
        Assert.Empty(await db.GitHubWorkflowSubscriptions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscriptionsUseIndependentBaselinesAndRetryFailedDelivery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<HomotechsualBotContext>().UseSqlite(connection).Options;
        await using var db = new HomotechsualBotContext(options);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var repository = new GitHubTrackedRepository { Owner = "example", Name = "repo" };
        db.GitHubTrackedRepositories.Add(repository);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var first = new GitHubWorkflowSubscription { RepositoryId = repository.Id, Workflow = "deploy-next.yml", ChannelId = 1 };
        var second = new GitHubWorkflowSubscription { RepositoryId = repository.Id, Workflow = "deploy-next.yml", ChannelId = 2 };
        db.GitHubWorkflowSubscriptions.AddRange(first, second);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new WorkflowHandler();
        using var http = new HttpClient(handler);
        using var socket = new DiscordSocketClient();
        using var service = new GitHubMonitorService(socket, new BotConfig(), new EmptyServiceProvider(),
            NullLogger<GitHubMonitorService>.Instance, http);
        var channel1 = DispatchProxy.Create<IMessageChannel, TestChannel>();
        var channel2 = DispatchProxy.Create<IMessageChannel, TestChannel>();
        var proxy1 = (TestChannel)channel1;
        var proxy2 = (TestChannel)channel2;
        var settings = new GitHubMonitorSettings();

        await Poll(service, db, repository, settings, channel1, first);
        await Poll(service, db, repository, settings, channel2, second);
        Assert.Equal(0, proxy1.Posts);
        Assert.Equal(0, proxy2.Posts);
        Assert.Contains("/actions/workflows/deploy-next.yml/runs?status=completed", handler.LastUri);
        Assert.Equal(2, await db.FeedPostStates.CountAsync(x => x.FeedType == "GitHub:Workflow", TestContext.Current.CancellationToken));

        foreach (var state in await db.FeedPostStates.ToListAsync(TestContext.Current.CancellationToken))
            state.LastCheckedAt = DateTime.UtcNow.AddMinutes(-2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        handler.UpdatedAt = DateTime.UtcNow.AddMinutes(-1);
        proxy1.Fail = true;
        await Poll(service, db, repository, settings, channel1, first);
        await Poll(service, db, repository, settings, channel2, second);
        Assert.Equal(0, proxy1.Posts);
        Assert.Equal(1, proxy2.Posts);

        proxy1.Fail = false;
        await Poll(service, db, repository, settings, channel1, first);
        await Poll(service, db, repository, settings, channel2, second);
        Assert.Equal(1, proxy1.Posts);
        Assert.Equal(1, proxy2.Posts);

        db.GitHubTrackedRepositories.Remove(repository);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await db.GitHubWorkflowSubscriptions.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task Poll(GitHubMonitorService service, HomotechsualBotContext db,
        GitHubTrackedRepository repository, GitHubMonitorSettings settings, IMessageChannel channel,
        GitHubWorkflowSubscription subscription)
    {
        var method = typeof(GitHubMonitorService).GetMethod("PollCategoryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(service, [db, repository, settings, GitHubEventCategory.Actions, channel, TestContext.Current.CancellationToken, subscription])!;
        await task;
    }

    public class TestChannel : DispatchProxy
    {
        public int Posts { get; private set; }
        public bool Fail { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "get_Id")
                return 1UL;
            if (method?.Name == "SendMessageAsync")
            {
                if (Fail)
                    return Task.FromException<IUserMessage>(new InvalidOperationException("Delivery failed"));
                Posts++;
                return Task.FromResult<IUserMessage>(null!);
            }
            throw new NotSupportedException(method?.Name);
        }
    }

    private sealed class WorkflowHandler : HttpMessageHandler
    {
        public string LastUri { get; private set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow.AddMinutes(-5);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri!.ToString();
            var json = $$"""{"workflow_runs":[{"id":123,"name":"deploy-next","run_number":1,"html_url":"https://github.com/example/repo/actions/runs/123","conclusion":"success","updated_at":"{{UpdatedAt:O}}"}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
