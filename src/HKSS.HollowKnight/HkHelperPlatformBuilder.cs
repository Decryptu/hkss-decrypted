// Ported from ArchipelagoMW/Archipelago.HollowKnight, IC/RM/HelperPlatformBuilder.cs, commit 373cc00 (MIT)
using System.Collections.Generic;
using ItemChanger;
using SD = ItemChanger.Util.SceneDataUtil;

namespace HKSS.HollowKnight;

internal static class HkHelperPlatformBuilder
{
    public static readonly IBool HasWalljump = new PDBool(nameof(PlayerData.hasWalljump));
    public static readonly IBool HasWalljumpLeft = new PDBool(nameof(ItemChanger.Modules.SplitClaw.hasWalljumpLeft));
    public static readonly IBool HasWalljumpRight = new PDBool(nameof(ItemChanger.Modules.SplitClaw.hasWalljumpRight));
    public static readonly IBool HasDoubleJump = new PDBool(nameof(PlayerData.hasDoubleJump));

    public static readonly IBool LacksLeftClaw = new Negation(new Disjunction(HasWalljump, HasWalljumpLeft));
    public static readonly IBool LacksLeftVertical = new Negation(new Disjunction(HasWalljump, HasDoubleJump, HasWalljumpLeft));
    public static readonly IBool LacksRightClaw = new Negation(new Disjunction(HasWalljump, HasWalljumpRight));
    public static readonly IBool LacksRightVertical = new Negation(new Disjunction(HasWalljump, HasDoubleJump, HasWalljumpRight));
    public static readonly IBool LacksAnyClaw = new Negation(new Disjunction(HasWalljump, HasWalljumpLeft, HasWalljumpRight));
    public static readonly IBool LacksAnyVertical = new Negation(new Disjunction(HasWalljump, HasDoubleJump, HasWalljumpLeft, HasWalljumpRight));

    public static void AddStartLocationRequiredPlatforms(HkSlotOptions options)
    {
        string startLocationName = options.StartLocationName ?? HkStartLocationNames.KingsPass;

        switch (startLocationName)
        {
            case HkStartLocationNames.Hive:
                ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Hive_03, X = 58.5f, Y = 134f, Test = LacksRightClaw });
                ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Hive_03, X = 58.5f, Y = 138.5f, Test = LacksAnyVertical });
                break;

            case HkStartLocationNames.FarGreenpath:
                ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Fungus1_13, X = 45f, Y = 16.5f, Test = LacksLeftClaw });
                ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Fungus1_13, X = 64f, Y = 16.5f, Test = LacksRightClaw });
                SD.Save(SceneNames.Fungus1_13, "Vine Platform (1)");
                SD.Save(SceneNames.Fungus1_13, "Vine Platform (2)");
                break;

            case HkStartLocationNames.LowerGreenpath:
                if (options.RandomizeNail)
                {
                    SD.Save(SceneNames.Fungus1_13, "Vine Platform");
                }
                break;
        }
    }

    public static void AddConveniencePlatforms(HkSlotData data)
    {
        HkSlotOptions options = data.Options;
        if (!options.ExtraPlatforms) return;

        var targetNames = data.EntrancePairs != null
            ? new HashSet<string>(data.EntrancePairs.Values)
            : new HashSet<string>();
        string startLocationName = options.StartLocationName ?? HkStartLocationNames.KingsPass;

        // Platforms to climb out from basin wanderer's journal
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_02, X = 128.3f, Y = 7f, Test = LacksLeftClaw });
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_02, X = 128.3f, Y = 11f, Test = LacksLeftClaw });

        // Platform to climb up to tram in basin from left with no items
        if (!targetNames.Contains($"{SceneNames.Abyss_03}[bot1]"))
        {
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_03, X = 34f, Y = 7f, Test = LacksRightVertical });
        }

        // Platform to climb out of Abyss with only wings
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_06_Core, X = 88.6f, Y = 263f, Test = LacksLeftClaw });

        // Platforms to climb back up from pale ore with no items
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_17, X = 164.7f, Y = 30f, Test = LacksRightVertical });
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_17, X = 99.5f, Y = 12.5f, Test = LacksRightVertical });
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_17, X = 117.7f, Y = 18.8f, Test = LacksRightClaw });
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_17, X = 114.3f, Y = 23f, Test = LacksAnyVertical });
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_17, X = 117.7f, Y = 7f, Test = LacksAnyClaw });
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_17, X = 117.7f, Y = 10.8f, Test = LacksAnyClaw });

        // Platform to remove softlock with wings at simple key in basin
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Abyss_20, X = 26.5f, Y = 13f, Test = LacksAnyClaw });

        // Platform for returning to Gorb landing
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Cliffs_02, X = 32.3f, Y = 27.7f, Test = LacksAnyVertical });

        // Platform to return from Deepnest mimic grub room
        if (!targetNames.Contains($"{SceneNames.Deepnest_01b}[right2]")
            && !targetNames.Contains($"{SceneNames.Deepnest_02}[left1]")
            && !targetNames.Contains($"{SceneNames.Deepnest_02}[right1]"))
        {
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Deepnest_01b, X = 48.3f, Y = 40f, Test = LacksAnyVertical });
        }

        // Platforms to return from the Deepnest_02 geo rocks without vertical
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Deepnest_02, X = 26f, Y = 12f, Test = LacksAnyClaw });
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Deepnest_02, X = 26f, Y = 16f, Test = LacksAnyClaw });

        // Platform to escape the Deepnest mimic room when mimics may not be present
        if (options.RandomizeMimics)
        {
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Deepnest_36, X = 26f, Y = 11f, Test = LacksLeftVertical });
        }

        // Platforms to climb back up from Mantis Lords with only wings
        if (!targetNames.Contains($"{SceneNames.Fungus2_15}[left1]")
            && !targetNames.Contains($"{SceneNames.Fungus2_25}[top1]")
            && !targetNames.Contains($"{SceneNames.Fungus2_25}[top2]"))
        {
            for (int i = 0; i < 2; i++)
            {
                ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Fungus2_15, X = 48f + 2 * i, Y = 15f + 10 * i, Test = LacksRightClaw });
            }
        }

        // Platforms to prevent softlock on lever on the way to love key
        for (int i = 0; i < 2; i++)
        {
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Fungus3_05, X = 65.7f, Y = 11f + 4.5f * i, Test = LacksRightClaw });
        }

        if (startLocationName != HkStartLocationNames.Hive)
        {
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Hive_03, X = 58.5f, Y = 134f, Test = LacksAnyVertical });
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Hive_03, X = 58.5f, Y = 138.5f, Test = LacksAnyVertical });
        }

        // Platforms in Colosseum to prevent bench softlock
        if (!targetNames.Contains($"{SceneNames.Room_Colosseum_02}[top2]")
            && !targetNames.Contains($"{SceneNames.Room_Colosseum_Spectate}[right1]"))
        {
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Room_Colosseum_02, X = 43.5f, Y = 45f, Test = LacksAnyClaw });
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Room_Colosseum_02, X = 43.5f, Y = 49.5f, Test = LacksAnyClaw });
        }

        // Platform to escape from the geo rock above Lemm
        ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Ruins1_05c, X = 26.6f, Y = 73.2f, Test = LacksAnyVertical });

        // Platforms to climb back up to King's Pass with no items
        if (!targetNames.Contains($"{SceneNames.Town}[right1]") && startLocationName == HkStartLocationNames.KingsPass)
        {
            for (int i = 0; i < 6; i++)
            {
                ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Town, X = 20f - 2 * (i % 2), Y = 5f * i + 15f, Test = LacksLeftClaw });
            }
        }

        // Platforms to prevent itemless softlock when checking left waterways
        if (!targetNames.Contains($"{SceneNames.Waterways_04}[left1]")
            && !targetNames.Contains($"{SceneNames.Waterways_04}[left2]")
            && !targetNames.Contains($"{SceneNames.Waterways_04b}[left1]")
            && !targetNames.Contains($"{SceneNames.Waterways_09}[left1]")
            && startLocationName != HkStartLocationNames.WestWaterways)
        {
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Waterways_04, X = 148f, Y = 23.1f, Test = LacksAnyVertical });
            ItemChangerMod.AddDeployer(new SmallPlatform { SceneName = SceneNames.Waterways_04, X = 139f, Y = 32f, Test = LacksAnyVertical });
            ItemChangerMod.AddDeployer(new SmallPlatform
            {
                SceneName = SceneNames.Waterways_04,
                X = 107f,
                Y = 10f,
                Test = options.RandomizeSwim ? new PDBool("canSwim") : null,
            });
            ItemChangerMod.AddDeployer(new SmallPlatform
            {
                SceneName = SceneNames.Waterways_04,
                X = 107f,
                Y = 15f,
                Test = options.RandomizeSwim ? new PDBool("canSwim") : null,
            });
        }
    }
}
