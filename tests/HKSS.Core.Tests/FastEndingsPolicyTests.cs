using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class FastEndingsPolicyTests
{
    [Theory]
    [InlineData(null, FastEndingsMode.Credits)]
    [InlineData("", FastEndingsMode.Credits)]
    [InlineData("credits", FastEndingsMode.Credits)]
    [InlineData("CREDITS", FastEndingsMode.Credits)]
    [InlineData("all", FastEndingsMode.All)]
    [InlineData("endings", FastEndingsMode.All)]
    [InlineData("off", FastEndingsMode.Off)]
    [InlineData("vanilla", FastEndingsMode.Off)]
    [InlineData("unknown_value", FastEndingsMode.Credits)]
    public void ParseHandlesAllValuesAndDefaultsToCredits(string? raw, FastEndingsMode expected)
    {
        Assert.Equal(expected, FastEndingsPolicy.Parse(raw));
    }

    [Fact]
    public void CycleAndKeysRoundTrip()
    {
        Assert.Equal(FastEndingsMode.All, FastEndingsPolicy.Next(FastEndingsMode.Credits));
        Assert.Equal(FastEndingsMode.Off, FastEndingsPolicy.Next(FastEndingsMode.All));
        Assert.Equal(FastEndingsMode.Credits, FastEndingsPolicy.Next(FastEndingsMode.Off));

        foreach (FastEndingsMode mode in new[] { FastEndingsMode.Off, FastEndingsMode.Credits, FastEndingsMode.All })
        {
            Assert.Equal(mode, FastEndingsPolicy.Parse(FastEndingsPolicy.ToKey(mode)));
        }
    }

    [Theory]
    [InlineData("Cinematic_Ending_A")]
    [InlineData("Cinematic_Ending_B")]
    [InlineData("Cinematic_Ending_C")]
    [InlineData("Cinematic_Ending_D")]
    [InlineData("Cinematic_Ending_E")]
    [InlineData("Cinematic_MrMushroom")]
    [InlineData("GG_End_Sequence")]
    public void EndingCinematicsAreManualSkipInCreditsModeAndAutoSkipInAllMode(string scene)
    {
        Assert.True(FastEndingsPolicy.IsEndingCinematicScene(scene));
        Assert.True(FastEndingsPolicy.IsReloadAfterEndingScene(scene));

        Assert.False(FastEndingsPolicy.ShouldUnlockManualSkip(FastEndingsMode.Off, scene));
        Assert.False(FastEndingsPolicy.ShouldAutoSkip(FastEndingsMode.Off, scene));

        Assert.True(FastEndingsPolicy.ShouldUnlockManualSkip(FastEndingsMode.Credits, scene));
        Assert.False(FastEndingsPolicy.ShouldAutoSkip(FastEndingsMode.Credits, scene));

        Assert.True(FastEndingsPolicy.ShouldUnlockManualSkip(FastEndingsMode.All, scene));
        Assert.True(FastEndingsPolicy.ShouldAutoSkip(FastEndingsMode.All, scene));
    }

    [Theory]
    [InlineData("End_Credits")]
    [InlineData("End_Credits_Scroll")]
    [InlineData("End_Game_Completion")]
    [InlineData("PermaDeath_Unlock")]
    [InlineData("BetaEnd")]
    public void CreditsAndCompletionAutoSkipInCreditsAndAllModes(string scene)
    {
        Assert.True(FastEndingsPolicy.IsCreditsOrCompletionScene(scene));
        Assert.True(FastEndingsPolicy.IsReloadAfterEndingScene(scene));

        Assert.False(FastEndingsPolicy.ShouldUnlockManualSkip(FastEndingsMode.Off, scene));
        Assert.False(FastEndingsPolicy.ShouldAutoSkip(FastEndingsMode.Off, scene));

        Assert.True(FastEndingsPolicy.ShouldUnlockManualSkip(FastEndingsMode.Credits, scene));
        Assert.True(FastEndingsPolicy.ShouldAutoSkip(FastEndingsMode.Credits, scene));

        Assert.True(FastEndingsPolicy.ShouldUnlockManualSkip(FastEndingsMode.All, scene));
        Assert.True(FastEndingsPolicy.ShouldAutoSkip(FastEndingsMode.All, scene));
    }

    [Theory]
    [InlineData("PermaDeath")]
    [InlineData("Opening_Sequence")]
    [InlineData("Opening_Sequence_Act3")]
    [InlineData("Menu_Credits")]
    [InlineData("Menu_Title")]
    [InlineData("Town")]
    [InlineData("Cradle_03")]
    [InlineData("Abyss_Cocoon")]
    [InlineData("Room_Final_Boss_Core")]
    public void ProtectedAndGameplayScenesAreNeverSkipped(string scene)
    {
        Assert.False(FastEndingsPolicy.IsReloadAfterEndingScene(scene));
        foreach (FastEndingsMode mode in new[] { FastEndingsMode.Off, FastEndingsMode.Credits, FastEndingsMode.All })
        {
            Assert.False(FastEndingsPolicy.ShouldUnlockManualSkip(mode, scene));
            Assert.False(FastEndingsPolicy.ShouldAutoSkip(mode, scene));
        }
    }
}
