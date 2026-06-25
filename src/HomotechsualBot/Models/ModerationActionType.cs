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
    MoveMessages,
    MoveThread,
    LockChannel,
    UnlockChannel,
    SlowModeSet,
    SlowModeCleared,
    SingleMessageEnforced,
    SpamDetected
}
