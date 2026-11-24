using System;
using System.Collections;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Modding
{
    public delegate string LanguageGetProxy(string key, string sheetTitle, string orig);
    public delegate bool GetBoolProxy(string name, bool orig);
    public delegate bool SetBoolProxy(string name, bool orig);
    public delegate int GetIntProxy(string name, int orig);
    public delegate int SetIntProxy(string name, int orig);
    public delegate string BeforeSceneLoadProxy(string sceneName);

    /// <summary>
    /// Minimal Hollow Knight Modding API hook layer rebuilt on Harmony for BepInEx 5 on HK 1.5.12620.
    /// </summary>
    public static class ModHooks
    {
        public static event LanguageGetProxy? LanguageGetHook;
        public static event GetBoolProxy? GetPlayerBoolHook;
        public static event SetBoolProxy? SetPlayerBoolHook;
        public static event GetIntProxy? GetPlayerIntHook;
        public static event SetIntProxy? SetPlayerIntHook;
        public static event Action? NewGameHook;
        public static event Action? HeroUpdateHook;
        public static event Action? AfterPlayerDeadHook;
        public static event BeforeSceneLoadProxy? BeforeSceneLoadHook;
        public static event Action<int>? SavegameSaveHook;
        public static event Action<int>? SavegameLoadHook;

        private static bool _installed;
        private static ManualLogSource? _log;

        [ThreadStatic]
        internal static bool BypassLanguageHook;

        [ThreadStatic]
        internal static bool BypassPlayerDataHook;

        public static void Install(Harmony harmony, ManualLogSource? log = null)
        {
            if (_installed) return;
            _installed = true;
            _log = log;

            harmony.Patch(
                AccessTools.Method(typeof(TeamCherry.Localization.Language), nameof(TeamCherry.Localization.Language.Get), new[] { typeof(string), typeof(string) }),
                postfix: new HarmonyMethod(typeof(ModHooks), nameof(OnLanguageGetPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(PlayerData), nameof(PlayerData.GetBool), new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(ModHooks), nameof(OnGetBoolPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(PlayerData), nameof(PlayerData.SetBool), new[] { typeof(string), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(ModHooks), nameof(OnSetBoolPrefix)));

            harmony.Patch(
                AccessTools.Method(typeof(PlayerData), nameof(PlayerData.GetInt), new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(ModHooks), nameof(OnGetIntPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(PlayerData), nameof(PlayerData.SetInt), new[] { typeof(string), typeof(int) }),
                prefix: new HarmonyMethod(typeof(ModHooks), nameof(OnSetIntPrefix)));

            harmony.Patch(
                AccessTools.Method(typeof(HeroController), "Update"),
                postfix: new HarmonyMethod(typeof(ModHooks), nameof(OnHeroUpdatePostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(GameManager), nameof(GameManager.PlayerDead), new[] { typeof(float) }),
                postfix: new HarmonyMethod(typeof(ModHooks), nameof(OnPlayerDeadPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(GameManager), nameof(GameManager.SaveGame), new[] { typeof(int), typeof(Action<bool>) }),
                prefix: new HarmonyMethod(typeof(ModHooks), nameof(OnSaveGamePrefix)));

            harmony.Patch(
                AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadGame), new[] { typeof(int), typeof(Action<bool>) }),
                postfix: new HarmonyMethod(typeof(ModHooks), nameof(OnLoadGamePostfix)));

            _log?.LogInfo("[ModApi] Installed HK ModHooks layer.");
        }

        public static void OnNewGame()
        {
            InvokeAction(NewGameHook, nameof(NewGameHook));
        }

        public static string InvokeBeforeSceneLoad(string sceneName)
        {
            var handlers = BeforeSceneLoadHook;
            if (handlers == null) return sceneName;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    sceneName = ((BeforeSceneLoadProxy)d)(sceneName);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] BeforeSceneLoadHook error: {ex}");
                }
            }
            return sceneName;
        }

        private static void OnLanguageGetPostfix(string key, string sheetTitle, ref string __result)
        {
            if (BypassLanguageHook) return;
            var handlers = LanguageGetHook;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    __result = ((LanguageGetProxy)d)(key, sheetTitle, __result);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] LanguageGetHook({sheetTitle}.{key}) error: {ex}");
                }
            }
        }

        private static void OnGetBoolPostfix(string boolName, ref bool __result)
        {
            if (BypassPlayerDataHook) return;
            var handlers = GetPlayerBoolHook;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    __result = ((GetBoolProxy)d)(boolName, __result);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] GetPlayerBoolHook({boolName}) error: {ex}");
                }
            }
        }

        private static void OnSetBoolPrefix(string boolName, ref bool value)
        {
            if (BypassPlayerDataHook) return;
            var handlers = SetPlayerBoolHook;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    value = ((SetBoolProxy)d)(boolName, value);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] SetPlayerBoolHook({boolName}) error: {ex}");
                }
            }
        }

        private static void OnGetIntPostfix(string intName, ref int __result)
        {
            if (BypassPlayerDataHook) return;
            var handlers = GetPlayerIntHook;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    __result = ((GetIntProxy)d)(intName, __result);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] GetPlayerIntHook({intName}) error: {ex}");
                }
            }
        }

        private static void OnSetIntPrefix(string intName, ref int value)
        {
            if (BypassPlayerDataHook) return;
            var handlers = SetPlayerIntHook;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    value = ((SetIntProxy)d)(intName, value);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] SetPlayerIntHook({intName}) error: {ex}");
                }
            }
        }

        private static void OnHeroUpdatePostfix()
        {
            InvokeAction(HeroUpdateHook, nameof(HeroUpdateHook));
        }

        private static void OnPlayerDeadPostfix(ref IEnumerator __result)
        {
            __result = WrapPlayerDead(__result);
        }

        private static IEnumerator WrapPlayerDead(IEnumerator inner)
        {
            while (inner.MoveNext())
            {
                yield return inner.Current;
            }
            InvokeAction(AfterPlayerDeadHook, nameof(AfterPlayerDeadHook));
        }

        private static void OnSaveGamePrefix(int saveSlot)
        {
            var handlers = SavegameSaveHook;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)d)(saveSlot);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] SavegameSaveHook error: {ex}");
                }
            }
        }

        private static void OnLoadGamePostfix(int saveSlot)
        {
            var handlers = SavegameLoadHook;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)d)(saveSlot);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] SavegameLoadHook error: {ex}");
                }
            }
        }

        private static void InvokeAction(Action? handlers, string name)
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[ModApi] {name} error: {ex}");
                }
            }
        }
    }
}

namespace Language
{
    /// <summary>
    /// Compatibility forwarder to TeamCherry.Localization.Language introduced in HK 1.5.12620.
    /// </summary>
    public static class Language
    {
        public static string Get(string key) => TeamCherry.Localization.Language.Get(key);

        public static string Get(string key, string sheetTitle) => TeamCherry.Localization.Language.Get(key, sheetTitle);

        public static string GetInternal(string key, string sheetTitle)
        {
            bool prev = Modding.ModHooks.BypassLanguageHook;
            Modding.ModHooks.BypassLanguageHook = true;
            try
            {
                return TeamCherry.Localization.Language.Get(key, sheetTitle);
            }
            finally
            {
                Modding.ModHooks.BypassLanguageHook = prev;
            }
        }

        public static bool Has(string key, string sheetTitle) => TeamCherry.Localization.Language.Has(key, sheetTitle);
    }
}

/// <summary>
/// Provides unhooked PlayerData accessors matching the HK Modding API's *Internal methods.
/// </summary>
public static class PlayerDataInternalExtensions
{
    public static bool GetBoolInternal(this PlayerData pd, string boolName)
    {
        bool prev = Modding.ModHooks.BypassPlayerDataHook;
        Modding.ModHooks.BypassPlayerDataHook = true;
        try { return pd.GetBool(boolName); }
        finally { Modding.ModHooks.BypassPlayerDataHook = prev; }
    }

    public static void SetBoolInternal(this PlayerData pd, string boolName, bool value)
    {
        bool prev = Modding.ModHooks.BypassPlayerDataHook;
        Modding.ModHooks.BypassPlayerDataHook = true;
        try { pd.SetBool(boolName, value); }
        finally { Modding.ModHooks.BypassPlayerDataHook = prev; }
    }

    public static int GetIntInternal(this PlayerData pd, string intName)
    {
        bool prev = Modding.ModHooks.BypassPlayerDataHook;
        Modding.ModHooks.BypassPlayerDataHook = true;
        try { return pd.GetInt(intName); }
        finally { Modding.ModHooks.BypassPlayerDataHook = prev; }
    }

    public static void SetIntInternal(this PlayerData pd, string intName, int value)
    {
        bool prev = Modding.ModHooks.BypassPlayerDataHook;
        Modding.ModHooks.BypassPlayerDataHook = true;
        try { pd.SetInt(intName, value); }
        finally { Modding.ModHooks.BypassPlayerDataHook = prev; }
    }
}
