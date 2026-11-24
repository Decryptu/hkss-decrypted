// Ported from Decryptu/silksong-decrypted, src/SilksongRandomizer/SaveFolderManager.cs, commit 34857d0 (MIT)
#nullable disable
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SilksongRandomizer
{
    internal static class SaveFolderManager
    {
        private static readonly MethodInfo NativePathMethod = AccessTools.Method(
            typeof(DesktopPlatform), "GetSaveSlotPath");
        private static readonly FieldInfo OnlineSubsystemField = AccessTools.Field(
            typeof(DesktopPlatform), "onlineSubsystem");
        internal static readonly Type GamePassSubsystemType =
            typeof(DesktopPlatform).Assembly.GetType("GameCoreOnlineSubsystem");
        private static ConfigEntry<string> lastDirectory;
        private static ConfigEntry<bool> defaultNoticeDismissed;
        private static string sessionDirectory;
        private static string notice;
        private static bool needsConfirmation;

        private static bool UsesLocalGamePassSaves =>
            Platform.Current is DesktopPlatform platform &&
            GamePassSubsystemType?.IsInstanceOfType(OnlineSubsystemField?.GetValue(platform)) == true;

        internal static void Initialize(ConfigFile config)
        {
            lastDirectory = config.Bind("Saves", "Last Folder", string.Empty,
                "Last acknowledged save folder.");
            defaultNoticeDismissed = config.Bind("Saves", "Default Notice Dismissed", false,
                "Whether the offline save folder notice has been acknowledged.");
            sessionDirectory = null;
            notice = null;
            needsConfirmation = false;
        }

        internal static string GetDataPath(int slot)
        {
            if (slot < 1 || slot > 4) throw new ArgumentOutOfRangeException(nameof(slot));
            var run = HKSS.Unity.HkssPlugin.Run;
            if (run == null || string.IsNullOrEmpty(run.SaveDir))
                throw new InvalidOperationException("The HKSS run save folder is unavailable.");
            if (Platform.Current is DesktopPlatform platform && NativePathMethod != null)
            {
                Type usage = NativePathMethod.GetParameters()[1].ParameterType;
                string savePath = (string)NativePathMethod.Invoke(platform,
                    new[] { (object)slot, Enum.Parse(usage, "Primary") });
                if (!string.IsNullOrEmpty(savePath) && run.OwnsSavePath(savePath))
                {
                    return Path.Combine(Path.GetDirectoryName(savePath), "silksong_randomizer.xml");
                }
            }
            return Path.Combine(run.SaveDir, "silksong_randomizer.xml");
        }

        private static void CheckFolder()
        {
            string directory = Path.GetFullPath(Path.GetDirectoryName(GetDataPath(1)));
            if (sessionDirectory == null)
            {
                sessionDirectory = directory;
                Directory.CreateDirectory(directory);
                RandomizerPlugin.Log?.LogInfo("[Randomizer Save] HKSS isolated folder: " + directory);
            }
            needsConfirmation = false;
            notice = null;
        }

        private static void RememberFolder()
        {
            lastDirectory.Value = sessionDirectory;
            if (Path.GetFileName(sessionDirectory) == "default") defaultNoticeDismissed.Value = true;
        }

        internal static bool CanAccessSaves(out string error)
        {
            try
            {
                CheckFolder();
                error = needsConfirmation ? "Read the save folder notice in F3 before continuing." : string.Empty;
            }
            catch (Exception ex)
            {
                error = "The game save folder could not be checked. " + ex.Message;
            }
            return error.Length == 0;
        }

        internal static bool CanWriteCurrentSave(out string error) => CanAccessSaves(out error);

        internal static void DrawNotice()
        {
            if (CanAccessSaves(out string error))
            {
                if (UsesLocalGamePassSaves)
                    GUILayout.Label("Game Pass randomizer saves are stored locally and do not sync with Xbox cloud saves.");
                return;
            }
            if (!needsConfirmation)
            {
                GUILayout.Label(error);
                return;
            }
            GUILayout.Label(notice);
            if (GUILayout.Button("Continue with the game's saves"))
            {
                try
                {
                    RememberFolder();
                    needsConfirmation = false;
                }
                catch (Exception ex)
                {
                    RandomizerPlugin.Log?.LogWarning("[Randomizer Save] Could not remember the folder. " + ex.Message);
                }
            }
            GUILayout.Space(8f);
        }
    }
}
