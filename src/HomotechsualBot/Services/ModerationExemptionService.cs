using Discord;
using Discord.WebSocket;
using DiscordBot.Models;

namespace DiscordBot.Services;

public class ModerationExemptionService
{
    private readonly HashSet<ulong> _exemptUserIds;
    private readonly HashSet<ulong> _exemptRoleIds;

    public ModerationExemptionService(ModerationExemptionsConfig config)
    {
        _exemptUserIds = (config.ExemptUserIds ?? []).ToHashSet();
        _exemptRoleIds = (config.ExemptRoleIds ?? []).ToHashSet();
    }

    public bool IsExempt(ulong userId) => _exemptUserIds.Contains(userId);

    public bool IsExempt(IUser user) => IsExempt(user.Id);

    public bool IsExempt(SocketGuildUser? user)
    {
        if (user is null)
            return false;

        if (IsExempt(user.Id))
            return true;

        return user.Roles.Any(role => _exemptRoleIds.Contains(role.Id));
    }
}
