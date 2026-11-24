using System;
using System.Diagnostics;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

[BepInPlugin("decryptu.hkss", "HKSS Decrypted", "0.1.0")]
public sealed class HkssPlugin : BaseUnityPlugin
{
#if HOLLOW_KNIGHT
    public const GameId ThisGame = GameId.HollowKnight;
#elif SILKSONG
    public const GameId ThisGame = GameId.Silksong;
#endif

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static RunContext Run { get; private set; } = null!;
    internal static SignalChannel Signals { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        try
        {
            Run = RunContext.Create(ThisGame, Environment.GetEnvironmentVariable);
            Signals = new SignalChannel(Run);
            Signals.Send(Signal.Booting);

            var harmony = new Harmony("decryptu.hkss");
            SaveIsolation.Install(harmony);
            FastBoot.Install(harmony);
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
