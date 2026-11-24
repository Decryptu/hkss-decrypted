using HKSS.Core;
using Xunit;

public sealed class IdSpaceTests
{
    [Fact]
    public void MapsHollowKnightIdsRoundTrip()
    {
        long combined = IdSpace.ToCombined(GameId.HollowKnight, 0x1000000);
        Assert.Equal(IdSpace.HollowKnightBlockStart, combined);
        Assert.Equal(GameId.HollowKnight, IdSpace.GameForId(combined));
        Assert.True(IdSpace.IsHollowKnight(combined));
        Assert.False(IdSpace.IsSilksong(combined));
        Assert.Equal(0x1000000, IdSpace.ToUpstream(GameId.HollowKnight, combined));

        long offset = IdSpace.ToCombined(GameId.HollowKnight, 0x10002E3);
        Assert.Equal(0x10002E3, IdSpace.ToUpstream(GameId.HollowKnight, offset));
    }

    [Fact]
    public void MapsSilksongIdsRoundTrip()
    {
        long combined = IdSpace.ToCombined(GameId.Silksong, 835_000);
        Assert.Equal(IdSpace.SilksongBlockStart, combined);
        Assert.Equal(GameId.Silksong, IdSpace.GameForId(combined));
        Assert.True(IdSpace.IsSilksong(combined));
        Assert.False(IdSpace.IsHollowKnight(combined));
        Assert.Equal(835_000, IdSpace.ToUpstream(GameId.Silksong, combined));

        long offset = IdSpace.ToCombined(GameId.Silksong, 842_107);
        Assert.Equal(842_107, IdSpace.ToUpstream(GameId.Silksong, offset));
    }

    [Fact]
    public void RejectsOutOfBlockIds()
    {
        Assert.Null(IdSpace.GameForId(IdSpace.ComboBlockStart));
        Assert.True(IdSpace.IsCombo(IdSpace.ComboBlockStart));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IdSpace.ToUpstream(GameId.HollowKnight, IdSpace.SilksongBlockStart));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IdSpace.ToUpstream(GameId.Silksong, IdSpace.HollowKnightBlockStart));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IdSpace.ToCombined(GameId.HollowKnight, 0x1000000 - 1));
    }

    [Fact]
    public void StripsGamePrefixes()
    {
        Assert.Equal("Mothwing_Cloak", IdSpace.StripPrefix("HK Mothwing_Cloak"));
        Assert.Equal("Swift Step", IdSpace.StripPrefix("SS Swift Step"));
        Assert.Equal("Town", IdSpace.StripPrefix("Town"));
    }
}
