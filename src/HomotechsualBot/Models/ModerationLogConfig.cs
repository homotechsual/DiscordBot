namespace DiscordBot.Models;

public class ModerationLogConfig
{
    public ulong ForumChannelId { get; set; }
    public string ModeratorRoleId { get; set; } = "0";
    public bool EventAuditEnabled { get; set; }
    public ulong EventAuditChannelId { get; set; }
    public bool LogMessageDeletes { get; set; } = true;
    public bool LogMemberJoins { get; set; } = true;
    public bool LogMemberLeaves { get; set; } = true;
    public List<ulong> IgnoredUserIds { get; set; } = [];
    public int AuditLogLookbackSeconds { get; set; } = 20;

    public IReadOnlyList<ulong> GetModeratorRoleIds()
    {
        if (string.IsNullOrWhiteSpace(ModeratorRoleId))
            throw new InvalidOperationException("ModerationLog:ModeratorRoleId must contain a role ID, a comma-separated list of role IDs, or 0 to disable.");

        var ids = new List<ulong>();
        foreach (var value in ModeratorRoleId.Split(','))
        {
            if (!ulong.TryParse(value.Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id))
                throw new InvalidOperationException("ModerationLog:ModeratorRoleId contains an invalid role ID. Use unsigned numeric IDs separated by commas.");

            if (id != 0 && !ids.Contains(id))
                ids.Add(id);
        }

        return ids;
    }
}
