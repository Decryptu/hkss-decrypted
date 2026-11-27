using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>What each game does to put a skin on, or take it off (<c>null</c>).</summary>
internal interface ISkinApplier
{
    /// <summary>True once the player and HUD exist, so a skin can be applied.</summary>
    bool Ready { get; }

    void Apply(SkinInfo? skin);

    /// <summary>Called after each scene change while a skin is worn, for objects the scene brings.</summary>
    void OnSceneChanged();

    /// <summary>Runs after animations each frame, for sprites the game changes by itself.</summary>
    void LateUpdate();

    /// <summary>
    /// Writes this game's example skin, made from the player's own game files, into
    /// <paramref name="gameFolder"/>. It runs over several frames so the game does not stall.
    /// </summary>
    System.Collections.IEnumerator WriteExample(string gameFolder);

#if DEBUG
    /// <summary>Development only: writes a test skin made from the game's own art into the folder.</summary>
    void DevWriteTestSkin(string folder);
#endif
}

/// <summary>
/// Chooses and applies the player's skin in both games: the Knight in Hollow Knight from Custom
/// Knight skins, Hornet in Silksong from Patchwork or Stitchwork packs. Each game remembers its own
/// skin in the run's player options; with random skins on, resting at a bench picks a new one.
/// </summary>
internal sealed class Skins : MonoBehaviour
{
    internal static Skins? Instance { get; private set; }

    private readonly System.Random _rng = new();
    private ISkinApplier _applier = null!;
    private IReadOnlyList<SkinInfo> _skins = Array.Empty<SkinInfo>();
    private string _root = string.Empty;
    private string? _applied;
    private bool _wasAtBench;
    private string _status = string.Empty;
    private Vector2 _scroll;

    internal static string Folder => Instance == null ? string.Empty : SkinLibrary.GameFolder(Instance._root, HkssPlugin.ThisGame);

    private void Awake()
    {
        Instance = this;
#if HOLLOW_KNIGHT
        _applier = new HKSS.HollowKnight.HkSkins();
#elif SILKSONG
        _applier = new HKSS.Silksong.SsSkins();
#endif
        _root = SkinLibrary.RootForRun(HkssPlugin.Run.RunDir);
        try
        {
            Directory.CreateDirectory(SkinLibrary.GameFolder(_root, HkssPlugin.ThisGame));
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"[Skins] Could not create {_root}: {e.Message}");
        }
        Rescan();
    }

    private void OnEnable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;

    private void OnDisable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;

    private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
    {
        if (!string.IsNullOrEmpty(_applied)) _applier.OnSceneChanged();
    }

    internal void Rescan()
    {
        _skins = SkinLibrary.List(_root, HkssPlugin.ThisGame);
        HkssPlugin.Log.LogInfo($"[Skins] {_skins.Count} skin(s) in {Folder}.");
        _applied = null;
    }

    private static string Wanted
    {
        get => PlayerOptions.Skin.Value ?? SkinLibrary.Vanilla;
        set => PlayerOptions.Skin.Value = value;
    }

    private void Update()
    {
        PollBench();
        if (!_applier.Ready) return;
        EnsureExample();
#if DEBUG
        DevTestSkin();
#endif
        string wanted = SkinLibrary.Find(_skins, Wanted)?.Id ?? SkinLibrary.Vanilla;
        if (wanted == _applied) return;
        Apply(wanted);
    }

    // The example skins are made once per HKSS folder, on each game's first start. A player who
    // deletes one does not get it back.
    private const string ExamplesMarker = "examples.txt";
    private bool _exampleChecked;

    private void EnsureExample()
    {
        if (_exampleChecked) return;
        _exampleChecked = true;
        string marker = Path.Combine(_root, ExamplesMarker);
        string key = HkssPlugin.ThisGame.Key();
        try
        {
            if (File.Exists(marker) && Array.IndexOf(File.ReadAllLines(marker), key) >= 0) return;
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"[Skins] Could not read {marker}: {e.Message}");
            return;
        }
        StartCoroutine(WriteExample(marker, key));
    }

    private System.Collections.IEnumerator WriteExample(string marker, string key)
    {
        var timer = Stopwatch.StartNew();
        System.Collections.IEnumerator steps = _applier.WriteExample(Folder);
        while (true)
        {
            bool more;
            try
            {
                more = steps.MoveNext();
            }
            catch (Exception e)
            {
                HkssPlugin.Log.LogWarning($"[Skins] Could not write the example skin: {e}");
                yield break;
            }
            if (!more) break;
            yield return steps.Current;
        }
        try
        {
            File.AppendAllLines(marker, new[] { key });
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"[Skins] Could not update {marker}: {e.Message}");
        }
        HkssPlugin.Log.LogInfo($"[Skins] Example skin written in {timer.ElapsedMilliseconds} ms.");
        Rescan();
    }

    private void LateUpdate()
    {
        if (string.IsNullOrEmpty(_applied)) return;
        try
        {
            _applier.LateUpdate();
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogDebug($"[Skins] {e.Message}");
        }
    }

    private void Apply(string id)
    {
        SkinInfo? skin = SkinLibrary.Find(_skins, id);
        var timer = Stopwatch.StartNew();
        try
        {
            _applier.Apply(skin);
            _status = skin == null ? "Wearing the game's own look." : $"Wearing {skin.DisplayName}.";
            HkssPlugin.Log.LogInfo($"[Skins] {(skin == null ? "Vanilla" : skin.Id)} applied in {timer.ElapsedMilliseconds} ms.");
        }
        catch (Exception e)
        {
            _status = $"Could not apply {skin?.DisplayName ?? "the skin"}: {e.Message}";
            HkssPlugin.Log.LogError($"[Skins] {e}");
        }
        _applied = id;
    }

    private void PollBench()
    {
        PlayerData? pd = PlayerData.instance;
        bool atBench = pd != null && pd.atBench;
        bool rested = atBench && !_wasAtBench;
        _wasAtBench = atBench;
        if (!rested || !PlayerOptions.RandomSkinAtBench.Value || _skins.Count == 0) return;

        string pick = SkinLibrary.PickRandom(_skins, Wanted, _rng);
        if (pick == Wanted) return;
        Wanted = pick;
        string name = SkinLibrary.Find(_skins, pick)?.DisplayName ?? "the game's own look";
        ApManager.Instance?.PushToast($"Skin: {name}");
    }

#if DEBUG
    // Development only: HKSS_DEV_SKIN_TEST=1 writes a test skin from the game's own art and wears it,
    // so a dev screenshot shows whether sheets, sprites and their orientation come out right.
    // HKSS_DEV_SKIN_TEST=panel also opens the Skins tab.
    private const string DevSkinId = "HKSS Test";
    private bool _devSkinDone;

    private void DevTestSkin()
    {
        if (_devSkinDone) return;
        string? mode = Environment.GetEnvironmentVariable("HKSS_DEV_SKIN_TEST");
        if (mode != "1" && mode != "panel") return;
        _devSkinDone = true;
        string folder = Path.Combine(Folder, DevSkinId);
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            _applier.DevWriteTestSkin(folder);
            Rescan();
            Wanted = DevSkinId;
            HkssPlugin.Log.LogInfo($"[dev] skin test written to {folder}.");
            if (mode == "panel") ApOverlay.ShowSkins();
            StartCoroutine(DevCycle());
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogError($"[dev] skin test failed: {e}");
        }
    }

    // Takes the test skin off and puts it back on, so the log shows that restoring works.
    private System.Collections.IEnumerator DevCycle()
    {
        yield return new WaitForSecondsRealtime(1f);
        Wanted = SkinLibrary.Vanilla;
        yield return new WaitForSecondsRealtime(1f);
        Wanted = DevSkinId;
    }
#endif

    /// <summary>The <c>F6</c> Skins tab.</summary>
    internal void DrawTab(GUIStyle section, GUIStyle label, Color accent)
    {
        string character = HkssPlugin.ThisGame == GameId.HollowKnight ? "the Knight" : "Hornet";
        string format = HkssPlugin.ThisGame == GameId.HollowKnight ? "Custom Knight skins" : "Patchwork or Stitchwork packs";

        GUILayout.Label($"Skin for {character}", section);
        PlayerOptions.RandomSkinAtBench.Value = GUILayout.Toggle(PlayerOptions.RandomSkinAtBench.Value,
            "Random skin at each bench (both games)");
        if (_status.Length > 0) GUILayout.Label(_status, label);
        GUILayout.Space(4f);

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        string current = SkinLibrary.Find(_skins, Wanted)?.Id ?? SkinLibrary.Vanilla;
        if (SkinButton("Game's own look", current == SkinLibrary.Vanilla, accent)) Wanted = SkinLibrary.Vanilla;
        foreach (SkinInfo skin in _skins)
        {
            if (SkinButton(skin.DisplayName, current == skin.Id, accent)) Wanted = skin.Id;
        }
        if (_skins.Count == 0)
            GUILayout.Label($"No skins installed yet. Put {format} in the skins folder, one folder each.", label);
        GUILayout.EndScrollView();

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open skins folder", GUILayout.Height(24f))) OpenFolder();
        if (GUILayout.Button("Rescan", GUILayout.Height(24f))) Rescan();
        GUILayout.EndHorizontal();
        GUILayout.Label(Folder, label);
    }

    private static bool SkinButton(string text, bool selected, Color accent)
    {
        Color prev = GUI.backgroundColor;
        if (selected) GUI.backgroundColor = accent;
        bool clicked = GUILayout.Button(selected ? $"● {text}" : text, GUILayout.Height(24f));
        GUI.backgroundColor = prev;
        return clicked && !selected;
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo { FileName = Folder, UseShellExecute = true });
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"[Skins] Could not open {Folder}: {e.Message}");
        }
    }
}
