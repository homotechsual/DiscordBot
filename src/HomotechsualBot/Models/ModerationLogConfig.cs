namespace DiscordBot.Models;

public class ModerationLogConfig
{
    public ulong ForumChannelId { get; set; }
    public ulong ModeratorRoleId { get; set; }
    public bool EventAuditEnabled { get; set; }
    public ulong EventAuditChannelId { get; set; }
    public bool LogMessageDeletes { get; set; } = true;
    public bool LogMemberJoins { get; set; } = true;
    public bool LogMemberLeaves { get; set; } = true;
    public List<ulong> IgnoredUserIds { get; set; } = [];
    public int AuditLogLookbackSeconds { get; set; } = 20;
}
