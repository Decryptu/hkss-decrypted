// Ported from ArchipelagoMW/Archipelago.HollowKnight, SlotDataModel/SlotData.cs, SlotDataModel/SlotOptions.cs & Enums.cs, commit 373cc00 (MIT)
using System.Collections.Generic;
using Newtonsoft.Json;

namespace HKSS.HollowKnight;

public enum GoalsLookup
{
    Any = 0,
    HollowKnight = 1,
    SealedSiblings = 2,
    Radiance = 3,
    Godhome = 4,
    GodhomeFlower = 5,
    GrubHunt = 6,
    MAX = GrubHunt,
}

public enum DeathLinkShadeHandling
{
    Vanilla = 0,
    Shadeless = 1,
    Shade = 2,
}

public enum WhitePalaceOption
{
    Exclude = 0,
    KingFragment = 1,
    NoPathOfPain = 2,
    Include = 3,
}

public enum EntranceRandoType
{
    None = 0,
    MapArea = 1,
    FullArea = 2,
    Room = 3,
    ConnectedArea = 4,
    Doors = 5,
}

public enum ShuffleEntrancesMode
{
    Coupled = 0,
    Decoupled = 1,
}

public sealed class HkSlotData
{
    [JsonProperty("seed")]
    public int Seed { get; set; }

    [JsonProperty("options")]
    public HkSlotOptions Options { get; set; } = new();

    [JsonProperty("location_costs")]
    public Dictionary<string, Dictionary<string, int>> LocationCosts { get; set; } = new();

    [JsonProperty("notch_costs")]
    public List<int> NotchCosts { get; set; } = new();

    [JsonProperty("grub_count")]
    public int? GrubsRequired { get; set; }

    [JsonProperty("is_race")]
    public bool DisableLocalSpoilerLogs { get; set; }

    [JsonProperty("entrance_pairs")]
    public Dictionary<string, string>? EntrancePairs { get; set; }
}

public sealed class HkSlotOptions
{
    public bool RandomizeDreamers { get; set; }
    public bool RandomizeSkills { get; set; }
    public bool RandomizeFocus { get; set; }
    public bool RandomizeSwim { get; set; }
    public bool RandomizeCharms { get; set; }
    public bool RandomizeKeys { get; set; }
    public bool RandomizeMaskShards { get; set; }
    public bool RandomizeVesselFragments { get; set; }
    public bool RandomizeCharmNotches { get; set; }
    public bool RandomizePaleOre { get; set; }
    public bool RandomizeGeoChests { get; set; }
    public bool RandomizeJunkPitChests { get; set; }
    public bool RandomizeRancidEggs { get; set; }
    public bool RandomizeRelics { get; set; }
    public bool RandomizeWhisperingRoots { get; set; }
    public bool RandomizeBossEssence { get; set; }
    public bool RandomizeGrubs { get; set; }
    public bool RandomizeMimics { get; set; }
    public bool RandomizeMaps { get; set; }
    public bool RandomizeStags { get; set; }
    public bool RandomizeLifebloodCocoons { get; set; }
    public bool RandomizeGrimmkinFlames { get; set; }
    public bool RandomizeJournalEntries { get; set; }
    public bool RandomizeNail { get; set; }
    public bool RandomizeGeoRocks { get; set; }
    public bool RandomizeBossGeo { get; set; }
    public bool RandomizeSoulTotems { get; set; }
    public bool RandomizeLoreTablets { get; set; }
    public bool RandomizeLevers { get; set; }
    public bool RandomizeBreakableWalls { get; set; }
    public bool RandomizeGrass { get; set; }
    public bool Slopeballs { get; set; }
    public bool RemoveSpellUpgrades { get; set; }
    public bool RandomizeElevatorPass { get; set; }
    public string? StartLocationName { get; set; }
    public int MinimumGrubPrice { get; set; }
    public int MaximumGrubPrice { get; set; }
    public int MinimumEssencePrice { get; set; }
    public int MaximumEssencePrice { get; set; }
    public int MinimumEggPrice { get; set; }
    public int MaximumEggPrice { get; set; }
    public int RandomCharmCosts { get; set; }
    public int EggShopSlots { get; set; }
    public GoalsLookup Goal { get; set; }
    public bool DeathLink { get; set; }
    public DeathLinkShadeHandling DeathLinkShade { get; set; }
    public bool DeathLinkBreaksFragileCharms { get; set; }
    public WhitePalaceOption WhitePalace { get; set; }
    public bool ExtraPlatforms { get; set; }
    public int StartingGeo { get; set; }
    public bool SplitMantisClaw { get; set; }
    public bool SplitMothwingCloak { get; set; }
    public bool SplitCrystalHeart { get; set; }
    public bool AddUnshuffledLocations { get; set; }
    public EntranceRandoType EntranceRandoType { get; set; }
    public ShuffleEntrancesMode ShuffleEntrancesMode { get; set; }

    // Logic & skip options (from worlds/hk slot_data["options"])
    public bool AcidSkips { get; set; }
    public bool BackgroundObjectPogos { get; set; }
    public bool ComplexSkips { get; set; }
    public bool DamageBoosts { get; set; }
    public bool DangerousSkips { get; set; }
    public bool DarkRooms { get; set; }
    public bool DifficultSkips { get; set; }
    public bool EnemyPogos { get; set; }
    public bool FireballSkips { get; set; }
    public bool InfectionSkips { get; set; }
    public bool ObscureSkips { get; set; }
    public bool PreciseMovement { get; set; }
    public bool ProficientCombat { get; set; }
    public bool ShadeSkips { get; set; }
    public bool SpikeTunnels { get; set; }

    public Dictionary<string, int> ToLogicOptions() => new()
    {
        ["AcidSkips"] = AcidSkips ? 1 : 0,
        ["BackgroundObjectPogos"] = BackgroundObjectPogos ? 1 : 0,
        ["ComplexSkips"] = ComplexSkips ? 1 : 0,
        ["DamageBoosts"] = DamageBoosts ? 1 : 0,
        ["DangerousSkips"] = DangerousSkips ? 1 : 0,
        ["DarkRooms"] = DarkRooms ? 1 : 0,
        ["DifficultSkips"] = DifficultSkips ? 1 : 0,
        ["EnemyPogos"] = EnemyPogos ? 1 : 0,
        ["FireballSkips"] = FireballSkips ? 1 : 0,
        ["InfectionSkips"] = InfectionSkips ? 1 : 0,
        ["ObscureSkips"] = ObscureSkips ? 1 : 0,
        ["PreciseMovement"] = PreciseMovement ? 1 : 0,
        ["ProficientCombat"] = ProficientCombat ? 1 : 0,
        ["ShadeSkips"] = ShadeSkips ? 1 : 0,
        ["SpikeTunnels"] = SpikeTunnels ? 1 : 0,
        ["RandomizeElevatorPass"] = RandomizeElevatorPass ? 1 : 0,
        ["RemoveSpellUpgrades"] = RemoveSpellUpgrades ? 1 : 0,
        ["RandomizeFocus"] = RandomizeFocus ? 1 : 0,
        ["RandomizeSwim"] = RandomizeSwim ? 1 : 0,
        ["RandomizeNail"] = RandomizeNail ? 1 : 0,
    };
}
