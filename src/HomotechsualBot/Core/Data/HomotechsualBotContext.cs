using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Core.Data;

public class HomotechsualBotContext : DbContext
{
    public HomotechsualBotContext(DbContextOptions<HomotechsualBotContext> options) : base(options)
    {
    }

    public DbSet<FeedPostState> FeedPostStates => Set<FeedPostState>();
    public DbSet<YoutubeMonitorSettings> YoutubeMonitorSettings => Set<YoutubeMonitorSettings>();
    public DbSet<YoutubeTrackedChannel> YoutubeTrackedChannels => Set<YoutubeTrackedChannel>();
    public DbSet<SingleMessageChannelState> SingleMessageChannelStates => Set<SingleMessageChannelState>();
    public DbSet<SingleMessageRecord> SingleMessageRecords => Set<SingleMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FeedPostState>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.FeedType, x.SourceId }).IsUnique();
            entity.Property(x => x.FeedType).IsRequired();
            entity.Property(x => x.SourceId).IsRequired();
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<YoutubeMonitorSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Enabled).HasDefaultValue(false);
            entity.Property(x => x.PollIntervalMinutes).HasDefaultValue(15);
            entity.Property(x => x.DefaultPostTitleTemplate).HasDefaultValue("[{ChannelName}] {VideoTitle}");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<YoutubeTrackedChannel>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ChannelId).IsUnique();
            entity.Property(x => x.ChannelId).IsRequired();
            entity.Property(x => x.ChannelName).IsRequired();
            entity.Property(x => x.KeywordFilters).IsRequired(false);
            entity.Property(x => x.IsEnabled).HasDefaultValue(true);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<SingleMessageChannelState>(entity =>
        {
            entity.HasKey(x => x.ChannelId);
            entity.Property(x => x.ChannelId).ValueGeneratedNever();
            entity.Property(x => x.IsEnabled).HasDefaultValue(false);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<SingleMessageRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
            entity.HasOne(x => x.Channel)
                .WithMany()
                .HasForeignKey(x => x.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.PostedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}

