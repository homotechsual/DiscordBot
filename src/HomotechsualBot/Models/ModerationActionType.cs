namespace DiscordBot.Models;

public enum ModerationActionType
{
    Ban,
    Unban,
    Kick,
    Mute,
    Unmute,
    Warn,
    PurgeUser,
    ClearMessages,
    LockChannel,
    UnlockChannel,
    SlowModeSet,
    SlowModeCleared,
    SingleMessageEnforced,
    SpamDetected
}
