using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace Modding
{
    /// <summary>
    /// Minimal base class matching the Hollow Knight Modding API surface used by ported mods.
    /// </summary>
    public abstract class Mod
    {
        public static ManualLogSource? Logger { get; set; }

        public string Name { get; }

        protected Mod(string? name = null)
        {
            Name = name ?? GetType().Name;
        }

        public virtual string GetVersion() => "0.1.0";
        public virtual int LoadPriority() => 0;
        public virtual List<(string, string)> GetPreloadNames() => new();
        public virtual void Initialize() { }
        public virtual void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects) => Initialize();

        public void Log(object? message) => Logger?.LogInfo($"[{Name}] {message}");
        public void LogDebug(object? message) => Logger?.LogDebug($"[{Name}] {message}");
        public void LogFine(object? message) => Logger?.LogDebug($"[{Name}] {message}");
        public void LogWarn(object? message) => Logger?.LogWarning($"[{Name}] {message}");
        public void LogError(object? message) => Logger?.LogError($"[{Name}] {message}");
    }

    public interface ILocalSettings<T>
    {
        void OnLoadLocal(T s);
        T OnSaveLocal();
    }

    public interface IGlobalSettings<T>
    {
        void OnLoadGlobal(T s);
        T OnSaveGlobal();
    }

    public interface IMenuMod
    {
        bool ToggleButtonInsideMenu { get; }
        List<MenuEntry> GetMenuData(MenuEntry? toggleButtonEntry);

        public struct MenuEntry
        {
            public string Name;
            public string? Description;
            public string[] Values;
            public Action<int> Saver;
            public Func<int> Loader;

            public MenuEntry(string name, string[] values, string? description, Action<int> saver, Func<int> loader)
            {
                Name = name;
                Values = values;
                Description = description;
                Saver = saver;
                Loader = loader;
            }
        }
    }

    public struct ModToggleDelegates
    {
        public Action<bool> SetModEnabled;
        public Func<bool> GetModEnabled;
        public Action ApplyChange;
        public Action RefreshMenu;
    }

    public interface ICustomMenuMod
    {
        bool ToggleButtonInsideMenu { get; }
        MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates);
    }
}
