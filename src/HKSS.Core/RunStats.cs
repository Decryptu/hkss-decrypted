using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace HKSS.Core;

/// <summary>
/// Per-game statistics for a combo run, stored in <c>state.json</c> so the other game and the
/// launcher can show a combined end-of-run summary.
/// </summary>
public sealed class GameStats
{
    /// <summary>Play time from the game's own timer, in seconds.</summary>
    [JsonProperty("play_seconds")]
    public double PlaySeconds { get; set; }

    [JsonProperty("deaths")]
    public int Deaths { get; set; }

    [JsonProperty("kills")]
    public int Kills { get; set; }

    /// <summary>The game's own completion percentage.</summary>
    [JsonProperty("completion_percent")]
    public double CompletionPercent { get; set; }

    /// <summary>Archipelago locations of this game checked in the slot (as of the last sync).</summary>
    [JsonProperty("checks_found")]
    public int ChecksFound { get; set; }

    /// <summary>Archipelago locations of this game in the slot (0 until a connection reports it).</summary>
    [JsonProperty("checks_total")]
    public int ChecksTotal { get; set; }

    /// <summary>Times the player lost health.</summary>
    [JsonProperty("hits_taken")]
    public int HitsTaken { get; set; }

    /// <summary>Masks of health lost in total.</summary>
    [JsonProperty("masks_lost")]
    public int MasksLost { get; set; }

    /// <summary>Hits landed on enemies.</summary>
    [JsonProperty("hits_landed")]
    public int HitsLanded { get; set; }

    /// <summary>Geo (Hollow Knight) or rosaries (Silksong) picked up.</summary>
    [JsonProperty("currency_earned")]
    public long CurrencyEarned { get; set; }

    [JsonProperty("jumps")]
    public int Jumps { get; set; }

    [JsonProperty("dashes")]
    public int Dashes { get; set; }

    /// <summary>Distance the player character moved, in game units.</summary>
    [JsonProperty("distance")]
    public double Distance { get; set; }

    /// <summary>Room transitions.</summary>
    [JsonProperty("rooms_entered")]
    public int RoomsEntered { get; set; }

    /// <summary>Distinct rooms (scenes) entered at least once.</summary>
    [JsonProperty("rooms_visited")]
    public List<string> RoomsVisited { get; set; } = new();

    /// <summary>Longest stretch of play time without dying, in seconds.</summary>
    [JsonProperty("longest_life_seconds")]
    public double LongestLifeSeconds { get; set; }

    /// <summary>This game's play time at the last death, in seconds.</summary>
    [JsonProperty("last_death_seconds")]
    public double LastDeathSeconds { get; set; }

    /// <summary>Deaths per map zone (the game's <c>MapZone</c> name).</summary>
    [JsonProperty("deaths_by_area")]
    public Dictionary<string, int> DeathsByArea { get; set; } = new();

    /// <summary>Play seconds per map zone (the game's <c>MapZone</c> name).</summary>
    [JsonProperty("seconds_by_area")]
    public Dictionary<string, double> SecondsByArea { get; set; } = new();

    /// <summary>Combined play time of both games when this game's goal was completed, in seconds (0 if not yet).</summary>
    [JsonProperty("goal_at_seconds")]
    public double GoalAtSeconds { get; set; }

    /// <summary>True once this game has written any stats (it has been played in this run).</summary>
    [JsonIgnore]
    public bool Played => PlaySeconds > 0 || Deaths > 0 || Kills > 0 || ChecksFound > 0;

    /// <summary>The run's current life so far, counted towards the longest life.</summary>
    [JsonIgnore]
    public double BestLifeSeconds => Math.Max(LongestLifeSeconds, Math.Max(0, PlaySeconds - LastDeathSeconds));

    /// <summary>Records a death at <paramref name="playSeconds"/> of this game's play time, updating the longest life.</summary>
    public void RecordDeath(double playSeconds, string? area)
    {
        Deaths++;
        if (playSeconds > LastDeathSeconds)
        {
            LongestLifeSeconds = Math.Max(LongestLifeSeconds, playSeconds - LastDeathSeconds);
            LastDeathSeconds = playSeconds;
        }
        if (!string.IsNullOrEmpty(area))
        {
            DeathsByArea ??= new Dictionary<string, int>();
            DeathsByArea.TryGetValue(area!, out int n);
            DeathsByArea[area!] = n + 1;
        }
    }

    public GameStats Clone()
    {
        var copy = (GameStats)MemberwiseClone();
        copy.RoomsVisited = new List<string>(RoomsVisited ?? new List<string>());
        copy.DeathsByArea = new Dictionary<string, int>(DeathsByArea ?? new Dictionary<string, int>());
        copy.SecondsByArea = new Dictionary<string, double>(SecondsByArea ?? new Dictionary<string, double>());
        return copy;
    }
}

/// <summary>A light-hearted award shown on the end-of-run screen.</summary>
public sealed class Feat
{
    [JsonProperty("title")]
    public string Title { get; }

    [JsonProperty("description")]
    public string Description { get; }

    [JsonConstructor]
    public Feat(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public override string ToString() => $"{Title}: {Description}";
}

/// <summary>A map area with a value, labelled with its game.</summary>
public readonly struct AreaRecord
{
    public GameId Game { get; }
    public string Area { get; }
    public double Value { get; }

    public AreaRecord(GameId game, string area, double value)
    {
        Game = game;
        Area = area;
        Value = value;
    }

    public string Name => RunSummary.AreaName(Game, Area);
}

/// <summary>Combined totals across both games, built from <see cref="RunState"/>.</summary>
public sealed class RunSummary
{
    public GameStats HollowKnight { get; }
    public GameStats Silksong { get; }
    public bool HkGoalCompleted { get; }
    public bool SsGoalCompleted { get; }
    public bool DoubleSteelSoul { get; }
    public bool SteelSoulDefeated { get; }
    public string SteelSoulDefeatedGame { get; }
    public string SteelSoulDefeatedScene { get; }

    /// <summary>Trips between the two games.</summary>
    public int Travels { get; }

    public RunSummary(RunState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        HollowKnight = state.GetStats(GameId.HollowKnight).Clone();
        Silksong = state.GetStats(GameId.Silksong).Clone();
        HkGoalCompleted = state.HkGoalCompleted;
        SsGoalCompleted = state.SsGoalCompleted;
        DoubleSteelSoul = state.DoubleSteelSoul;
        SteelSoulDefeated = state.SteelSoulDefeated;
        SteelSoulDefeatedGame = state.SteelSoulDefeatedGame ?? string.Empty;
        SteelSoulDefeatedScene = state.SteelSoulDefeatedScene ?? string.Empty;
        Travels = state.Travels;
    }

    public GameStats Get(GameId game) => game == GameId.HollowKnight ? HollowKnight : Silksong;

    public double TotalPlaySeconds => HollowKnight.PlaySeconds + Silksong.PlaySeconds;
    public int TotalDeaths => HollowKnight.Deaths + Silksong.Deaths;
    public int TotalKills => HollowKnight.Kills + Silksong.Kills;
    public int TotalChecksFound => HollowKnight.ChecksFound + Silksong.ChecksFound;
    public int TotalChecks => HollowKnight.ChecksTotal + Silksong.ChecksTotal;
    public int TotalHitsTaken => HollowKnight.HitsTaken + Silksong.HitsTaken;
    public int TotalMasksLost => HollowKnight.MasksLost + Silksong.MasksLost;
    public int TotalHitsLanded => HollowKnight.HitsLanded + Silksong.HitsLanded;
    public int TotalJumps => HollowKnight.Jumps + Silksong.Jumps;
    public int TotalDashes => HollowKnight.Dashes + Silksong.Dashes;
    public double TotalDistance => HollowKnight.Distance + Silksong.Distance;
    public int TotalRoomsEntered => HollowKnight.RoomsEntered + Silksong.RoomsEntered;
    public int TotalRoomsVisited => RoomsVisited(HollowKnight) + RoomsVisited(Silksong);
    public double LongestLifeSeconds => Math.Max(HollowKnight.BestLifeSeconds, Silksong.BestLifeSeconds);

    /// <summary>Time between the first and the second goal, or 0 until both are done.</summary>
    public double GoalGapSeconds =>
        HollowKnight.GoalAtSeconds > 0 && Silksong.GoalAtSeconds > 0 ? Math.Abs(HollowKnight.GoalAtSeconds - Silksong.GoalAtSeconds) : 0;

    public static int RoomsVisited(GameStats s) => s.RoomsVisited?.Count ?? 0;

    /// <summary>Checks found per hour of combined play time (0 with no play time).</summary>
    public double ChecksPerHour => TotalPlaySeconds >= 60 ? TotalChecksFound / (TotalPlaySeconds / 3600.0) : 0;

    /// <summary>Kills per death; kills alone when there are no deaths.</summary>
    public double KillsPerDeath => TotalDeaths > 0 ? (double)TotalKills / TotalDeaths : TotalKills;

    /// <summary>Share of combined play time spent in Hollow Knight, 0 to 1 (0.5 with no play time).</summary>
    public double HollowKnightShare => TotalPlaySeconds > 0 ? HollowKnight.PlaySeconds / TotalPlaySeconds : 0.5;

    /// <summary>The area with the most deaths across both games, if any death was placed.</summary>
    public AreaRecord? DeadliestArea => Top(s => s.DeathsByArea?.ToDictionary(kv => kv.Key, kv => (double)kv.Value));

    /// <summary>The area with the most play time across both games.</summary>
    public AreaRecord? FavouriteArea => Top(s => s.SecondsByArea);

    private AreaRecord? Top(Func<GameStats, Dictionary<string, double>?> pick)
    {
        AreaRecord? best = null;
        foreach (GameId game in new[] { GameId.HollowKnight, GameId.Silksong })
        {
            Dictionary<string, double>? map = pick(Get(game));
            if (map == null) continue;
            foreach (var kv in map)
            {
                if (kv.Value <= 0 || IsIgnoredArea(kv.Key)) continue;
                if (best == null || kv.Value > best.Value.Value) best = new AreaRecord(game, kv.Key, kv.Value);
            }
        }
        return best;
    }

    private static bool IsIgnoredArea(string area) => area is "" or "NONE" or "TEST_AREA";

    /// <summary>Fun awards earned so far, most notable first.</summary>
    public IReadOnlyList<Feat> Feats
    {
        get
        {
            var feats = new List<Feat>();
            bool hkDone = HkGoalCompleted, ssDone = SsGoalCompleted;
            double hours = TotalPlaySeconds / 3600.0;

            if (hkDone && ssDone && TotalDeaths == 0)
                feats.Add(new Feat("Unbroken Vessel", "Both goals without a single death."));
            else if (hkDone && ssDone && DoubleSteelSoul && !SteelSoulDefeated)
                feats.Add(new Feat("Steel in Both Worlds", "Double Steel Soul survived to the end."));
            if (hkDone && ssDone && TotalPlaySeconds > 0 && TotalPlaySeconds < 4 * 3600)
                feats.Add(new Feat("Swift Pilgrim", "Both goals in under four hours."));
            if (GoalGapSeconds > 0 && GoalGapSeconds < 15 * 60)
                feats.Add(new Feat("Twin Crowns", "Second goal within 15 minutes of the first."));
            if (TotalChecks > 0 && TotalChecksFound >= TotalChecks)
                feats.Add(new Feat("Every Stone Turned", "Every check in the slot found."));
            if (Played(HollowKnight) && Played(Silksong) && Math.Abs(HollowKnightShare - 0.5) <= 0.05)
                feats.Add(new Feat("Perfect Balance", "Time split almost evenly between both worlds."));
            if (Travels >= 20)
                feats.Add(new Feat("Weaver of Worlds", $"Travelled between the games {Travels} times."));
            else if (Travels >= 8)
                feats.Add(new Feat("Frequent Flier", $"Travelled between the games {Travels} times."));
            if (LongestLifeSeconds >= 2 * 3600)
                feats.Add(new Feat("Hard to Kill", $"Survived {FormatDuration(LongestLifeSeconds)} without dying."));
            if (TotalDeaths >= 100)
                feats.Add(new Feat("Shade Collector", $"Fell {TotalDeaths} times and kept going."));
            if (DeadliestArea is { Value: >= 15 } nemesis)
                feats.Add(new Feat("Nemesis", $"{nemesis.Name} claimed {nemesis.Value:0} lives."));
            if (TotalKills >= 2000)
                feats.Add(new Feat("Exterminator", $"{TotalKills} foes felled."));
            if (TotalDistance >= 100_000)
                feats.Add(new Feat("Long Walk Home", $"Covered {FormatDistance(TotalDistance)}."));
            if (TotalJumps >= 10_000)
                feats.Add(new Feat("Spring-Legged", $"{TotalJumps} jumps."));
            if (TotalDashes >= 5_000)
                feats.Add(new Feat("Never Walked Anywhere", $"{TotalDashes} dashes."));
            if (TotalRoomsVisited >= 500)
                feats.Add(new Feat("Cartographer", $"Explored {TotalRoomsVisited} rooms."));
            if (hours >= 1 && ChecksPerHour >= 60)
                feats.Add(new Feat("Check Fiend", $"{ChecksPerHour:0} checks per hour."));
            if (HollowKnight.CurrencyEarned + Silksong.CurrencyEarned >= 50_000)
                feats.Add(new Feat("Deep Pockets", $"{HollowKnight.CurrencyEarned:N0} geo and {Silksong.CurrencyEarned:N0} rosaries picked up."));
            if (hours >= 1 && TotalHitsTaken > 0 && TotalHitsTaken / hours < 10)
                feats.Add(new Feat("Untouchable", "Fewer than ten hits taken per hour."));
            return feats;
        }
    }

    /// <summary>A one-line title for the run, from its most striking trait.</summary>
    public string Epithet
    {
        get
        {
            if (!Played(HollowKnight) && !Played(Silksong)) return "A journey yet to begin";
            if (SteelSoulDefeated) return "Fallen, but not forgotten";
            if (HkGoalCompleted && SsGoalCompleted && TotalDeaths == 0) return "The Unbroken";
            if (TotalDeaths >= 100) return "The Undying Shade";
            if (Travels >= 20) return "Wanderer Between Worlds";
            if (LongestLifeSeconds >= 2 * 3600) return "The Steadfast";
            if (TotalKills >= 2000) return "Bane of Bugs";
            if (Played(HollowKnight) && Played(Silksong))
            {
                if (HollowKnightShare >= 0.7) return "Hallownest's Own";
                if (HollowKnightShare <= 0.3) return "Child of Pharloom";
                return "Knight and Hornet";
            }
            return Played(HollowKnight) ? "Hallownest's Own" : "Child of Pharloom";
        }
    }

    private static bool Played(GameStats s) => s.Played;

    /// <summary>Formats seconds as <c>H:MM:SS</c>.</summary>
    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        long s = (long)Math.Floor(seconds);
        return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
    }

    /// <summary>Formats checks as <c>found / total</c>, or just <c>found</c> when the total is unknown.</summary>
    public static string FormatChecks(int found, int total) => total > 0 ? $"{found} / {total}" : found.ToString();

    /// <summary>Formats a distance in game units, one unit read as a metre.</summary>
    public static string FormatDistance(double units)
    {
        if (double.IsNaN(units) || units < 0) units = 0;
        return units >= 1000
            ? (units / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km"
            : units.ToString("0", CultureInfo.InvariantCulture) + " m";
    }

    private static readonly Dictionary<string, string> HkAreaNames = new()
    {
        ["KINGS_PASS"] = "King's Pass", ["CLIFFS"] = "Howling Cliffs", ["TOWN"] = "Dirtmouth",
        ["CROSSROADS"] = "Forgotten Crossroads", ["GREEN_PATH"] = "Greenpath", ["ROYAL_GARDENS"] = "Queen's Gardens",
        ["FOG_CANYON"] = "Fog Canyon", ["WASTES"] = "Fungal Wastes", ["DEEPNEST"] = "Deepnest", ["HIVE"] = "The Hive",
        ["BONE_FOREST"] = "Bone Forest", ["PALACE_GROUNDS"] = "Palace Grounds", ["MINES"] = "Crystal Peak",
        ["RESTING_GROUNDS"] = "Resting Grounds", ["CITY"] = "City of Tears", ["DREAM_WORLD"] = "Dream",
        ["COLOSSEUM"] = "Colosseum of Fools", ["ABYSS"] = "Ancient Basin", ["ROYAL_QUARTER"] = "King's Station",
        ["WHITE_PALACE"] = "White Palace", ["SHAMAN_TEMPLE"] = "Ancestral Mound", ["WATERWAYS"] = "Royal Waterways",
        ["QUEENS_STATION"] = "Queen's Station", ["OUTSKIRTS"] = "Kingdom's Edge", ["KINGS_STATION"] = "King's Station",
        ["MAGE_TOWER"] = "Soul Sanctum", ["TRAM_UPPER"] = "Tram", ["TRAM_LOWER"] = "Tram", ["FINAL_BOSS"] = "Black Egg Temple",
        ["SOUL_SOCIETY"] = "Soul Sanctum", ["ACID_LAKE"] = "Lake of Unn", ["NOEYES_TEMPLE"] = "Stone Sanctuary",
        ["MONOMON_ARCHIVE"] = "Teacher's Archives", ["MANTIS_VILLAGE"] = "Mantis Village", ["RUINED_TRAMWAY"] = "Ruined Tramway",
        ["DISTANT_VILLAGE"] = "Distant Village", ["ABYSS_DEEP"] = "The Abyss", ["ISMAS_GROVE"] = "Isma's Grove",
        ["WYRMSKIN"] = "Cast-Off Shell", ["LURIENS_TOWER"] = "Watcher's Spire", ["LOVE_TOWER"] = "Tower of Love",
        ["GLADE"] = "Spirits' Glade", ["BLUE_LAKE"] = "Blue Lake", ["PEAK"] = "Crystal Peak", ["JONI_GRAVE"] = "Joni's Repose",
        ["OVERGROWN_MOUND"] = "Overgrown Mound", ["CRYSTAL_MOUND"] = "Crystallised Mound", ["BEASTS_DEN"] = "Beast's Den",
        ["GODS_GLORY"] = "Godhome", ["GODSEEKER_WASTE"] = "Junk Pit",
    };

    // Only names that are certain; anything else falls back to the title-cased MapZone name.
    private static readonly Dictionary<string, string> SsAreaNames = new()
    {
        ["PATH_OF_BONE"] = "The Marrow", ["GREYMOOR"] = "Greymoor", ["SHELLWOOD_THICKET"] = "Shellwood",
        ["CITY_OF_SONG"] = "Choral Chambers", ["THE_SLAB"] = "The Slab", ["DUSTPENS"] = "Sinner's Road",
        ["BELLTOWN"] = "Bellhart", ["HUNTERS_NEST"] = "Hunter's March", ["BONETOWN"] = "Bone Bottom",
        ["MOSS_CAVE"] = "Moss Grotto", ["DOCKS"] = "Deep Docks", ["WILDS"] = "Far Fields", ["WEAVER_SHRINE"] = "Weavenest",
        ["MOSSTOWN"] = "Mosshome", ["LIBRARY"] = "Whispering Vaults", ["CLOVER"] = "Verdania", ["UNDERSTORE"] = "Underworks",
        ["COG_CORE"] = "Cogwork Core", ["PEAK"] = "Mount Fay", ["WARD"] = "Whiteward", ["HANG"] = "High Halls",
        ["ARBORIUM"] = "Memorium", ["CRADLE"] = "The Cradle", ["PILGRIMS_REST"] = "Pilgrim's Rest",
        ["HALFWAY_HOUSE"] = "Halfway Home", ["JUDGE_STEPS"] = "Blasted Steps", ["CRAWLSPACE"] = "Wormways",
        ["WISP"] = "Wisp Thicket", ["SWAMP"] = "Bilewater", ["ABYSS"] = "The Abyss", ["AQUEDUCT"] = "Putrified Ducts",
    };

    /// <summary>A readable name for a game's <c>MapZone</c> value.</summary>
    public static string AreaName(GameId game, string zone)
    {
        if (string.IsNullOrEmpty(zone)) return "Unknown";
        var table = game == GameId.HollowKnight ? HkAreaNames : SsAreaNames;
        if (table.TryGetValue(zone, out string? name)) return name;
        var words = zone.ToLowerInvariant().Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1));
        return string.Join(" ", words);
    }
}
