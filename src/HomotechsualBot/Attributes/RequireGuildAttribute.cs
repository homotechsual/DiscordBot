using Discord;
using Discord.Interactions;
using System;
using System.Threading.Tasks;

namespace DiscordBot.Attributes;

/// <summary>
/// Precondition attribute that restricts a command to a specific guild.
/// </summary>
public class RequireGuildAttribute : PreconditionAttribute
{
    private readonly ulong _guildId;

    /// <summary>
    /// Restricts the command to a specific guild by ID.
    /// </summary>
    /// <param name="guildId">The guild ID where the command is allowed</param>
    public RequireGuildAttribute(ulong guildId)
    {
        _guildId = guildId;
    }

    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services)
    {
        if (context.Guild == null)
        {
            return Task.FromResult(PreconditionResult.FromError("This command can only be used in a server."));
        }

        if (context.Guild.Id != _guildId)
        {
            return Task.FromResult(PreconditionResult.FromError("This command is not available in this server."));
        }

        return Task.FromResult(PreconditionResult.FromSuccess());
    }
}
