using Discord;

namespace DiscordBot.Models;

public record ModerationLogEntry(
    ModerationActionType ActionType,
    IUser? Target,
    ulong TargetId,
    IUser? Moderator,
    string? Reason,
    DateTimeOffset Timestamp)
{
    public static ModerationLogEntry Create(
        ModerationActionType actionType,
        IUser target,
        IUser moderator,
        string? reason = null) =>
        new(actionType, target, target.Id, moderator, reason, DateTimeOffset.UtcNow);

    public static ModerationLogEntry CreateUnknownTarget(
        ModerationActionType actionType,
        ulong targetId,
        IUser moderator,
        string? reason = null) =>
        new(actionType, null, targetId, moderator, reason, DateTimeOffset.UtcNow);
}
