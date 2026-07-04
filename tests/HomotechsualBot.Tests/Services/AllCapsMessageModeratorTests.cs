using DiscordBot.Services;
using Xunit;

namespace HomotechsualBot.Tests.Services;

public class AllCapsMessageModeratorTests
{
    [Fact]
    public void Evaluate_AllUppercaseAboveThreshold_Triggers()
    {
        var result = AllCapsMessageModerator.Evaluate("THIS IS DEFINITELY SHOUTING", minLetters: 8, minUppercaseRatio: 0.7);

        Assert.True(result.ShouldTrigger);
    }

    [Fact]
    public void Evaluate_NormalSentence_DoesNotTrigger()
    {
        var result = AllCapsMessageModerator.Evaluate("This is a normal sentence.", minLetters: 8, minUppercaseRatio: 0.7);

        Assert.False(result.ShouldTrigger);
    }

    [Fact]
    public void Evaluate_ShortAcronym_DoesNotTriggerDueToMinLetters()
    {
        var result = AllCapsMessageModerator.Evaluate("LOL", minLetters: 8, minUppercaseRatio: 0.7);

        Assert.False(result.ShouldTrigger);
    }

    [Fact]
    public void Evaluate_EmptyMessage_DoesNotTrigger()
    {
        var result = AllCapsMessageModerator.Evaluate("", minLetters: 8, minUppercaseRatio: 0.7);

        Assert.False(result.ShouldTrigger);
        Assert.Equal(0, result.LetterCount);
    }

    [Fact]
    public void Evaluate_UrlOnly_StripsUrlAndDoesNotTrigger()
    {
        var result = AllCapsMessageModerator.Evaluate("HTTPS://EXAMPLE.COM/SOME/LONG/PATH", minLetters: 8, minUppercaseRatio: 0.7);

        Assert.False(result.ShouldTrigger);
    }

    [Fact]
    public void Evaluate_MentionsAndEmojiStripped_MixedCaseRemainderDoesNotTrigger()
    {
        var result = AllCapsMessageModerator.Evaluate(
            "<@123456789012345> <:SOMEEMOJI:987654321098765> hey there",
            minLetters: 8,
            minUppercaseRatio: 0.7);

        Assert.False(result.ShouldTrigger);
    }

    [Fact]
    public void Evaluate_CodeBlockStripped_DoesNotCountTowardsRatio()
    {
        var result = AllCapsMessageModerator.Evaluate(
            "normal text ```SOME CODE IN ALL CAPS HERE THAT WOULD OTHERWISE TRIGGER```",
            minLetters: 8,
            minUppercaseRatio: 0.7);

        Assert.False(result.ShouldTrigger);
    }

    [Fact]
    public void Evaluate_RatioExactlyAtThreshold_Triggers()
    {
        // "AAAAAAAaaa" = 10 letters, 7 uppercase -> ratio exactly 0.7
        var result = AllCapsMessageModerator.Evaluate("AAAAAAAaaa", minLetters: 8, minUppercaseRatio: 0.7);

        Assert.True(result.ShouldTrigger);
        Assert.Equal(10, result.LetterCount);
        Assert.Equal(7, result.UppercaseCount);
    }
}
