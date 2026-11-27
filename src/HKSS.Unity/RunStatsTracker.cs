using System;
using System.Collections.Generic;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Records this game's play time, completion, deaths, kills, Archipelago check counts and the
/// lighter end-of-run numbers (hits, currency, jumps, dashes, distance, rooms, areas) into
/// <c>state.json</c> for the combined end-of-run summary. Counters are kept in memory and flushed
/// with the snapshot every few seconds, before travel and on quit.
/// </summary>
internal sealed class RunStatsTracker : MonoBehaviour
{
    private const float FlushInterval = 15f;

    /// <summary>A move longer than this in one frame is a teleport (respawn, warp), not travel.</summary>
    private const float MaxStepPerFrame = 4f;

    internal static RunStatsTracker? Instance { get; private set; }

    private sealed class Pending
    {
        public readonly List<(double PlaySeconds, string Area)> Deaths = new();
        public int Kills, HitsTaken, MasksLost, HitsLanded, Jumps, Dashes, RoomsEntered;
        public long Currency;
        public double Distance;
        public readonly HashSet<string> Rooms = new();
        public readonly Dictionary<string, double> AreaSeconds = new();

        public bool IsEmpty => Deaths.Count == 0 && Kills == 0 && HitsTaken == 0 && HitsLanded == 0 && Jumps == 0
            && Dashes == 0 && RoomsEntered == 0 && Currency == 0 && Distance == 0 && Rooms.Count == 0 && AreaSeconds.Count == 0;
    }

    private static Pending _pending = new();
    private float _nextFlush;

    private string _lastScene = string.Empty;
    private Vector3 _lastPos;
    private bool _hasLastPos;
    private float _lastPlayTime = -1f;
    private GlobalEnums.MapZone _lastMapZone = (GlobalEnums.MapZone)(-1);
    private string _lastMapZoneName = string.Empty;

    internal static void Install(Harmony harmony)
    {
        var die = AccessTools.Method(typeof(HeroController), "Die");
        if (die != null)
            harmony.Patch(die, postfix: new HarmonyMethod(typeof(RunStatsTracker), nameof(OnHeroDie)));
        else
            HkssPlugin.Log.LogWarning("Run stats: HeroController.Die not found; deaths will not be counted.");

#if HOLLOW_KNIGHT
        var enemyDie = AccessTools.Method(typeof(HealthManager), nameof(HealthManager.Die),
            new[] { typeof(float?), typeof(AttackTypes), typeof(bool) });
        var jump = AccessTools.Method(typeof(HeroController), "HeroJump", Type.EmptyTypes);
        var dash = AccessTools.Method(typeof(HeroController), "HeroDash", Type.EmptyTypes);
#elif SILKSONG
        // The shorter overloads all forward to this one.
        var enemyDie = AccessTools.Method(typeof(HealthManager), nameof(HealthManager.Die),
            new[] { typeof(float?), typeof(AttackTypes), typeof(NailElements), typeof(GameObject), typeof(bool), typeof(float), typeof(bool), typeof(bool) });
        var jump = AccessTools.Method(typeof(HeroController), "HeroJump", new[] { typeof(bool) });
        var dash = AccessTools.Method(typeof(HeroController), "HeroDash", new[] { typeof(bool) });
#endif
        if (enemyDie != null)
            harmony.Patch(enemyDie,
                prefix: new HarmonyMethod(typeof(RunStatsTracker), nameof(BeforeEnemyDie)),
                postfix: new HarmonyMethod(typeof(RunStatsTracker), nameof(AfterEnemyDie)));
        else
            HkssPlugin.Log.LogWarning("Run stats: HealthManager.Die not found; kills will not be counted.");

        // The rest are light-hearted extras: a missing method only drops that one number.
        TryPatch(harmony, AccessTools.Method(typeof(HealthManager), "TakeDamage"), "hits landed", postfix: nameof(OnEnemyHit));
        TryPatch(harmony, AccessTools.Method(typeof(PlayerData), "TakeHealth"), "hits taken", prefix: nameof(OnTakeHealth));
        TryPatch(harmony, AccessTools.Method(typeof(PlayerData), "AddGeo", new[] { typeof(int) }), "currency",
            prefix: nameof(BeforeAddGeo), postfix: nameof(AfterAddGeo));
        TryPatch(harmony, jump, "jumps", postfix: nameof(OnJump));
        TryPatch(harmony, AccessTools.Method(typeof(HeroController), "DoWallJump", Type.EmptyTypes), "wall jumps", postfix: nameof(OnJump));
        TryPatch(harmony, AccessTools.Method(typeof(HeroController), "DoDoubleJump", Type.EmptyTypes), "double jumps", postfix: nameof(OnJump));
        TryPatch(harmony, dash, "dashes", postfix: nameof(OnDash));
    }

    private static void TryPatch(Harmony harmony, System.Reflection.MethodInfo? target, string what,
        string? prefix = null, string? postfix = null)
    {
        if (target == null)
        {
            HkssPlugin.Log.LogWarning($"Run stats: hook for {what} not found; it will not be counted.");
            return;
        }
        try
        {
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(RunStatsTracker), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(RunStatsTracker), postfix));
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"Run stats: could not hook {what} ({e.Message}).");
        }
    }

    private static void OnHeroDie(HeroController __instance, object[] __args)
    {
        if (__instance != null && __instance.cState.dead) return;
#if SILKSONG
        // Silksong's Die(nonLethal, frostDeath): non-lethal and memory-scene wake-ups are not deaths.
        if (__args.Length > 0 && __args[0] is bool nonLethal && nonLethal) return;
        if (GameManager.instance != null && GameManager.instance.IsMemoryScene()) return;
#endif
        GameManager? gm = GameManager.instance;
        PlayerData? pd = PlayerData.instance;
        _pending.Deaths.Add((gm != null ? gm.PlayTime : 0, pd != null ? pd.mapZone.ToString() : string.Empty));
    }

    internal static void ResetPending() => _pending = new Pending();

    private static void BeforeEnemyDie(HealthManager __instance, out bool __state) => __state = __instance.GetIsDead();

    private static void AfterEnemyDie(HealthManager __instance, bool __state)
    {
        if (!__state && __instance.GetIsDead()) _pending.Kills++;
    }

    private static void OnEnemyHit() => _pending.HitsLanded++;

    private static void OnTakeHealth(PlayerData __instance, object[] __args)
    {
        if (__instance != PlayerData.instance || __args.Length == 0 || __args[0] is not int amount || amount <= 0) return;
        _pending.HitsTaken++;
        _pending.MasksLost += amount;
    }

    private static void BeforeAddGeo(PlayerData __instance, out int __state) => __state = __instance.geo;

    private static void AfterAddGeo(PlayerData __instance, int __state)
    {
        if (__instance != PlayerData.instance) return;
        int gained = __instance.geo - __state;
        if (gained > 0) _pending.Currency += gained;
    }

    private static void OnJump() => _pending.Jumps++;

    private static void OnDash() => _pending.Dashes++;

    private void Awake() => Instance = this;

    private void Update()
    {
        Sample();
        if (Time.unscaledTime < _nextFlush) return;
        _nextFlush = Time.unscaledTime + FlushInterval;
        Flush();
    }

    /// <summary>Per-frame bookkeeping: rooms entered, distance moved and time per area.</summary>
    private void Sample()
    {
        GameManager? gm = GameManager.instance;
        HeroController? hero = HeroController.instance;
        PlayerData? pd = PlayerData.instance;
        if (gm == null || hero == null || pd == null || !gm.IsGameplayScene())
        {
            _hasLastPos = false;
            _lastPlayTime = -1f;
            return;
        }

        string scene = gm.sceneName ?? string.Empty;
        if (scene.Length > 0 && scene != _lastScene)
        {
            _lastScene = scene;
            _pending.RoomsEntered++;
            _pending.Rooms.Add(scene);
            _hasLastPos = false;
        }

        Vector3 pos = hero.transform.position;
        if (_hasLastPos && !gm.isPaused)
        {
            float step = Vector2.Distance(pos, _lastPos);
            if (step < MaxStepPerFrame) _pending.Distance += step;
        }
        _lastPos = pos;
        _hasLastPos = true;

        float playTime = gm.PlayTime;
        if (_lastPlayTime >= 0f)
        {
            float dt = playTime - _lastPlayTime;
            if (dt > 0f && dt < 1f)
            {
                GlobalEnums.MapZone zone = pd.mapZone;
                if (zone != _lastMapZone)
                {
                    _lastMapZone = zone;
                    _lastMapZoneName = zone.ToString();
                }
                _pending.AreaSeconds.TryGetValue(_lastMapZoneName, out double t);
                _pending.AreaSeconds[_lastMapZoneName] = t + dt;
            }
        }
        _lastPlayTime = playTime;
    }

    private void OnApplicationQuit() => Flush();

    /// <summary>Counts one trip to the other game.</summary>
    internal static void RecordTravel()
    {
        try
        {
            HkssPlugin.State.UpdateStats(state => state.Travels++);
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"Run stats: could not save travel ({e.Message}).");
        }
    }

    /// <summary>Writes the current snapshot and pending counters. Safe to call at any time.</summary>
    internal static void Flush()
    {
        try
        {
            GameManager? gm = GameManager.instance;
            PlayerData? pd = PlayerData.instance;
            bool inGame = gm != null && pd != null && HeroController.instance != null;
            ApClient? client = ApManager.Instance?.Client;
            (int Found, int Total)? hkChecks = client?.GetCheckCounts(GameId.HollowKnight);
            (int Found, int Total)? ssChecks = client?.GetCheckCounts(GameId.Silksong);
            if (!inGame && _pending.IsEmpty && hkChecks == null && ssChecks == null) return;

            // Swap first: hooks that fire while saving land in the next batch.
            Pending p = _pending;
            _pending = new Pending();
            try
            {
                HkssPlugin.State.UpdateStats(state =>
                {
                    GameStats mine = state.GetStats(HkssPlugin.ThisGame);
                    if (inGame) mine.PlaySeconds = Math.Max(mine.PlaySeconds, gm!.PlayTime);
                    Apply(mine, p);
                    if (inGame) mine.CompletionPercent = pd!.completionPercentage;
                    if (hkChecks is { } hk) Apply(state.GetStats(GameId.HollowKnight), hk);
                    if (ssChecks is { } ss) Apply(state.GetStats(GameId.Silksong), ss);
                });
            }
            catch
            {
                Merge(p);
                throw;
            }
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"Run stats: could not save ({e.Message}).");
        }
    }

    private static void Apply(GameStats mine, Pending p)
    {
        foreach (var (seconds, area) in p.Deaths) mine.RecordDeath(seconds, area);
        mine.Kills += p.Kills;
        mine.HitsTaken += p.HitsTaken;
        mine.MasksLost += p.MasksLost;
        mine.HitsLanded += p.HitsLanded;
        mine.Jumps += p.Jumps;
        mine.Dashes += p.Dashes;
        mine.RoomsEntered += p.RoomsEntered;
        mine.CurrencyEarned += p.Currency;
        mine.Distance += p.Distance;
        mine.RoomsVisited ??= new List<string>();
        foreach (string room in p.Rooms)
            if (!mine.RoomsVisited.Contains(room)) mine.RoomsVisited.Add(room);
        mine.SecondsByArea ??= new Dictionary<string, double>();
        foreach (var kv in p.AreaSeconds)
        {
            mine.SecondsByArea.TryGetValue(kv.Key, out double t);
            mine.SecondsByArea[kv.Key] = t + kv.Value;
        }
    }

    /// <summary>Puts an unsaved batch back so the next flush retries it.</summary>
    private static void Merge(Pending p)
    {
        Pending cur = _pending;
        cur.Deaths.InsertRange(0, p.Deaths);
        cur.Kills += p.Kills;
        cur.HitsTaken += p.HitsTaken;
        cur.MasksLost += p.MasksLost;
        cur.HitsLanded += p.HitsLanded;
        cur.Jumps += p.Jumps;
        cur.Dashes += p.Dashes;
        cur.RoomsEntered += p.RoomsEntered;
        cur.Currency += p.Currency;
        cur.Distance += p.Distance;
        cur.Rooms.UnionWith(p.Rooms);
        foreach (var kv in p.AreaSeconds)
        {
            cur.AreaSeconds.TryGetValue(kv.Key, out double t);
            cur.AreaSeconds[kv.Key] = t + kv.Value;
        }
    }

    private static void Apply(GameStats stats, (int Found, int Total) checks)
    {
        if (checks.Total <= 0) return;
        stats.ChecksFound = checks.Found;
        stats.ChecksTotal = checks.Total;
    }
}
