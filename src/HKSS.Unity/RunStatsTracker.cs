using System;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Records this game's play time, completion, deaths, kills and Archipelago check counts into
/// <c>state.json</c> for the combined end-of-run summary. Deaths and kills are counted in memory
/// and flushed with the snapshot every few seconds, before travel and on quit.
/// </summary>
internal sealed class RunStatsTracker : MonoBehaviour
{
    private const float FlushInterval = 15f;

    internal static RunStatsTracker? Instance { get; private set; }

    private static int _pendingDeaths;
    private static int _pendingKills;
    private float _nextFlush;

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
#elif SILKSONG
        // The shorter overloads all forward to this one.
        var enemyDie = AccessTools.Method(typeof(HealthManager), nameof(HealthManager.Die),
            new[] { typeof(float?), typeof(AttackTypes), typeof(NailElements), typeof(GameObject), typeof(bool), typeof(float), typeof(bool), typeof(bool) });
#endif
        if (enemyDie != null)
            harmony.Patch(enemyDie,
                prefix: new HarmonyMethod(typeof(RunStatsTracker), nameof(BeforeEnemyDie)),
                postfix: new HarmonyMethod(typeof(RunStatsTracker), nameof(AfterEnemyDie)));
        else
            HkssPlugin.Log.LogWarning("Run stats: HealthManager.Die not found; kills will not be counted.");
    }

    private static void OnHeroDie(HeroController __instance, object[] __args)
    {
        if (__instance != null && __instance.cState.dead) return;
#if SILKSONG
        // Silksong's Die(nonLethal, frostDeath): non-lethal and memory-scene wake-ups are not deaths.
        if (__args.Length > 0 && __args[0] is bool nonLethal && nonLethal) return;
        if (GameManager.instance != null && GameManager.instance.IsMemoryScene()) return;
#endif
        _pendingDeaths++;
    }

    internal static void ResetPending()
    {
        _pendingDeaths = 0;
        _pendingKills = 0;
    }

    private static void BeforeEnemyDie(HealthManager __instance, out bool __state) => __state = __instance.GetIsDead();

    private static void AfterEnemyDie(HealthManager __instance, bool __state)
    {
        if (!__state && __instance.GetIsDead()) _pendingKills++;
    }

    private void Awake() => Instance = this;

    private void Update()
    {
        if (Time.unscaledTime < _nextFlush) return;
        _nextFlush = Time.unscaledTime + FlushInterval;
        Flush();
    }

    private void OnApplicationQuit() => Flush();

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
            if (!inGame && _pendingDeaths == 0 && _pendingKills == 0 && hkChecks == null && ssChecks == null) return;

            int deaths = _pendingDeaths, kills = _pendingKills;
            HkssPlugin.State.UpdateStats(state =>
            {
                GameStats mine = state.GetStats(HkssPlugin.ThisGame);
                mine.Deaths += deaths;
                mine.Kills += kills;
                if (inGame)
                {
                    mine.PlaySeconds = Math.Max(mine.PlaySeconds, gm!.PlayTime);
                    mine.CompletionPercent = pd!.completionPercentage;
                }
                if (hkChecks is { } hk) Apply(state.GetStats(GameId.HollowKnight), hk);
                if (ssChecks is { } ss) Apply(state.GetStats(GameId.Silksong), ss);
            });
            _pendingDeaths -= deaths;
            _pendingKills -= kills;
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"Run stats: could not save ({e.Message}).");
        }
    }

    private static void Apply(GameStats stats, (int Found, int Total) checks)
    {
        if (checks.Total <= 0) return;
        stats.ChecksFound = checks.Found;
        stats.ChecksTotal = checks.Total;
    }
}
