using System;
using System.Diagnostics;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

[BepInPlugin("decryptu.hkss", "HKSS Decrypted", PluginVersion)]
public sealed class HkssPlugin : BaseUnityPlugin
{
    public const string PluginVersion = "0.1.14";
#if HOLLOW_KNIGHT
    public const GameId ThisGame = GameId.HollowKnight;
#elif SILKSONG
    public const GameId ThisGame = GameId.Silksong;
#endif

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static BepInEx.Configuration.ConfigFile? PluginConfig { get; private set; }
    internal static RunContext Run { get; private set; } = null!;
    internal static RunStateStore State { get; private set; } = null!;
    internal static SignalChannel Signals { get; private set; } = null!;
    private Harmony _harmony = null!;

    private void Awake()
    {
        Log = Logger;
        PluginConfig = Config;
        try
        {
            Run = RunContext.Create(ThisGame, Environment.GetEnvironmentVariable);
            PlayerOptions.Load(Run.RunDir);
            State = new RunStateStore(Run);
            Signals = new SignalChannel(Run);
            Signals.Send(Signal.Booting);

            _harmony = new Harmony("decryptu.hkss");
            ArchipelagoJsonCompatibility.Install(_harmony);
#if HOLLOW_KNIGHT
            HKSS.HollowKnight.HkClientHost.InstallEarly(_harmony, Log);
#endif
            SaveIsolation.Install(_harmony);
            FastBoot.Install(_harmony);
            TownGate.Install(_harmony);
            ThinSlice.Install(_harmony);
            OverlayCursor.Install(_harmony);
            RunStatsTracker.Install(_harmony);
#if HOLLOW_KNIGHT
            _harmony.CreateClassProcessor(typeof(HKSS.HollowKnight.HkAutoQuill)).Patch();
#endif
        }
        catch (Exception e)
        {
            Abort("Startup failed, so the game was closed before it could touch any save.", e);
            return;
        }

        // The game boots behind the launcher's travel screen, without focus.
        Application.runInBackground = true;
        gameObject.AddComponent<AutoLoad>();
        gameObject.AddComponent<Travel>();
        gameObject.AddComponent<TownGate>();
        gameObject.AddComponent<TownWarp>();
        gameObject.AddComponent<Benchwarp>();
        gameObject.AddComponent<ApManager>();
        gameObject.AddComponent<ApOverlay>();
        gameObject.AddComponent<RunStatsTracker>();
#if HOLLOW_KNIGHT
        gameObject.AddComponent<HKSS.HollowKnight.HkClientHost>();
#elif SILKSONG
        HKSS.Silksong.SsClientHost.Install(gameObject, _harmony, Log, Config);
#endif
        Log.LogInfo($"Run folder {Run.RunDir}");
    }

    /// <summary>
    /// Ends the process at once. A normal quit finishes the frame, which is long enough for the game
    /// to open and write its usual save folder.
    /// </summary>
    internal static void Abort(string message, Exception? cause = null)
    {
        try
        {
            Log.LogFatal(cause == null ? message : $"{message}\n{cause}");
            Signals?.Send(Signal.Error, detail: message);
        }
        finally
        {
            Process.GetCurrentProcess().Kill();
        }
    }
}
