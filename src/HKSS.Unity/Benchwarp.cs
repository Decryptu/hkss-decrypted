using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GlobalEnums;
using HKSS.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HKSS.Unity;

/// <summary>
/// Unified bench discovery tracker and Benchwarp manager for both Hollow Knight and Silksong.
/// - F7 toggles the Benchwarp panel.
/// - F8 warps immediately to the last rested bench.
/// - Persists discovered benches per run in <c>saves/&lt;game&gt;/benches.json</c>.
/// </summary>
internal sealed class Benchwarp : MonoBehaviour
{
    internal static Benchwarp? Instance { get; private set; }
    internal static bool IsPanelOpen { get; private set; }
    internal static event Action<BenchEntry>? OnBenchDiscovered;

    private BenchStore _store = null!;
    private string _savePath = string.Empty;
    private bool _warping;
    private bool _panelOpen;
    private float _nextPollTime;
    private string _lastObservedKey = string.Empty;
    private string _searchFilter = string.Empty;
    private Vector2 _scrollPos;

    private GUIStyle? _panelStyle;
    private GUIStyle? _titleStyle;
    private GUIStyle? _areaStyle;
    private GUIStyle? _labelStyle;
    private Texture2D? _bgTex;

    internal BenchStore Store => _store;

    private void Awake()
    {
        Instance = this;
        _store = new BenchStore(HkssPlugin.ThisGame);
        _savePath = Path.Combine(HkssPlugin.Run.SaveDir, "benches.json");
        try
        {
            _store.LoadFromFile(_savePath);
            if (_store.Count > 0)
            {
                HkssPlugin.Log.LogInfo($"[Benchwarp] Loaded {_store.Count} discovered bench(es).");
            }
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[Benchwarp] Failed to load benches.json: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        SetPanelOpen(false);
        if (Instance == this) Instance = null;
        if (_bgTex != null) Destroy(_bgTex);
    }

    private void Update()
    {
        PollCurrentBench();

        Keyboard? kb = Keyboard.current;
        if (kb != null)
        {
            if (PlayerOptions.Pressed(Shortcut.Benchwarp))
            {
                SetPanelOpen(!_panelOpen);
            }
            else if (PlayerOptions.Pressed(Shortcut.LastBench) && !ApOverlay.IsPanelOpen && !_panelOpen)
            {
                TryWarpToLastBench();
            }
            else if (_panelOpen && PlayerOptions.Capturing == null && kb.escapeKey.wasPressedThisFrame)
            {
                SetPanelOpen(false);
            }
        }
    }

    internal void SetPanelOpen(bool open)
    {
        if (_panelOpen == open) return;
        _panelOpen = open;
        IsPanelOpen = open;
        OverlayCursor.SetOpen(OverlayCursor.Panel.Benchwarp, open);
    }

    internal int MergeExternalBenches(IEnumerable<BenchEntry> benches)
    {
        int added = _store.Merge(benches);
        if (added > 0)
        {
            SaveBenches();
            HkssPlugin.Log.LogInfo($"[Benchwarp] Synced {added} remote bench(es).");
        }
        return added;
    }

    private void PollCurrentBench()
    {
        if (Time.unscaledTime < _nextPollTime) return;
        _nextPollTime = Time.unscaledTime + 0.5f;

        GameManager? gm = GameManager._instance;
        PlayerData? pd = PlayerData.instance;
        if (gm == null || pd == null || !gm.IsGameplayScene()) return;

        string scene = pd.respawnScene ?? string.Empty;
        string marker = pd.respawnMarkerName ?? string.Empty;
        int respawnType = pd.respawnType;

        if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(marker)) return;
        if (BenchCatalog.IsIgnoredMarker(scene, marker)) return;

        // Only record genuine bench respawns (atBench == true or respawnType == 1).
        if (!pd.atBench && respawnType != 1) return;

        string key = BenchEntry.BuildKey(scene, marker);
        if (string.Equals(key, _lastObservedKey, StringComparison.Ordinal)) return;
        _lastObservedKey = key;

        string mapZoneName = pd.mapZone.ToString();
        bool added = _store.RecordBench(scene, marker, respawnType: 1, mapZoneName: mapZoneName, setAsLast: true);
        SaveBenches();

        if (added && _store.LastBench != null)
        {
            HkssPlugin.Log.LogInfo($"[Benchwarp] Discovered bench: {_store.LastBench.AreaName} — {_store.LastBench.DisplayName} ({scene}/{marker}).");
            OnBenchDiscovered?.Invoke(_store.LastBench);
        }
    }

    private void SaveBenches()
    {
        try
        {
            _store.SaveToFile(_savePath);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[Benchwarp] Failed to save benches.json: {ex.Message}");
        }
    }

    internal bool TryWarpToLastBench()
    {
        PlayerData? pd = PlayerData.instance;
        if (pd != null
            && !string.IsNullOrWhiteSpace(pd.respawnScene)
            && !string.IsNullOrWhiteSpace(pd.respawnMarkerName)
            && !BenchCatalog.IsIgnoredMarker(pd.respawnScene, pd.respawnMarkerName))
        {
            var current = new BenchEntry
            {
                SceneName = pd.respawnScene,
                MarkerName = pd.respawnMarkerName,
                RespawnType = pd.respawnType > 0 ? pd.respawnType : 1,
                DisplayName = "Last Bench",
            };
            return TryWarpToBench(current);
        }

        if (_store.LastBench != null)
        {
            return TryWarpToBench(_store.LastBench);
        }

        ApManager.Instance?.PushToast("No bench discovered yet.", ToastKind.Error);
        return false;
    }

    internal bool TryRecoverToBench()
    {
        if (!PlayerOptions.BenchRecovery.Value || _store.LastBench is not BenchEntry bench)
        {
            ApManager.Instance?.PushToast("Rest at a bench before using recovery.", ToastKind.Error);
            return false;
        }
        return TryWarpToBench(bench, recoverInputLock: true);
    }

    internal bool TryWarpToBench(BenchEntry bench, bool recoverInputLock = false)
    {
        if (bench == null || string.IsNullOrWhiteSpace(bench.SceneName) || string.IsNullOrWhiteSpace(bench.MarkerName))
            return false;
        if (_warping) return false;

        if (!TravelGuard.CanLeave(out string reason, recoverInputLock))
        {
            ApManager.Instance?.PushToast($"Benchwarp refused: {reason}", ToastKind.Error);
            HkssPlugin.Log.LogInfo($"[Benchwarp] Refused: {reason}");
            return false;
        }

        SetPanelOpen(false);
        _warping = true;
        StartCoroutine(WarpToBenchRoutine(bench));
        return true;
    }

    private IEnumerator WarpToBenchRoutine(BenchEntry bench)
    {
        GameManager? gm = GameManager._instance;
        HeroController? hero = HeroController.instance;
        PlayerData? pd = PlayerData.instance;

        if (gm == null || hero == null || pd == null)
        {
            _warping = false;
            yield break;
        }

        HkssPlugin.Log.LogInfo($"[Benchwarp] Warping to {bench.AreaName} — {bench.DisplayName} ({bench.SceneName}/{bench.MarkerName}).");

        pd.respawnScene = bench.SceneName;
        pd.respawnMarkerName = bench.MarkerName;
        pd.respawnType = bench.RespawnType > 0 ? bench.RespawnType : 1;
        pd.atBench = false;

        if (!string.IsNullOrWhiteSpace(bench.MapZoneName)
            && Enum.TryParse(bench.MapZoneName, ignoreCase: true, out MapZone parsedZone))
        {
            pd.mapZone = parsedZone;
        }

#if HOLLOW_KNIGHT
        hero.RelinquishControl();
        hero.AffectedByGravity(false);
        hero.transitionState = HeroTransitionState.EXITING_SCENE;

        if (hero.cState.onConveyor || hero.cState.onConveyorV || hero.cState.inConveyorZone)
        {
            hero.GetComponent<ConveyorMovementHero>()?.StopConveyorMove();
            hero.cState.inConveyorZone = false;
            hero.cState.onConveyor = false;
            hero.cState.onConveyorV = false;
        }

        hero.cState.nearBench = false;
        hero.cState.superDashing = false;
        hero.cState.spellQuake = false;

        gm.cameraCtrl.FadeOut(CameraFadeType.LEVEL_TRANSITION);
        yield return new WaitForSecondsRealtime(0.35f);

        gm.ReadyForRespawn(isFirstLevelForPlayer: false);
        while (gm.IsInSceneTransition) yield return null;

        Time.timeScale = 1f;
        gm.FadeSceneIn();
#elif SILKSONG
        pd.tempRespawnScene = string.Empty;
        pd.tempRespawnMarker = string.Empty;
        pd.tempRespawnType = 0;
        DeliveryQuestItem.BreakAll();
        pd.travelling = false;

        gm.RespawningHero = true;
        gm.BeginSceneTransition(new GameManager.SceneLoadInfo
        {
            SceneName = bench.SceneName,
            EntryGateName = bench.MarkerName,
        });
        while (gm.IsInSceneTransition) yield return null;
#endif

        _warping = false;
    }

    private void EnsureStyles()
    {
        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bgTex.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.11f, 0.92f));
            _bgTex.Apply();
        }

        _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(14, 14, 12, 12),
            normal = { background = _bgTex, textColor = Color.white },
        };

        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = AccentColor() },
        };

        _areaStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.95f, 0.85f, 0.45f, 1f) },
        };

        _labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            normal = { textColor = new Color(0.86f, 0.89f, 0.95f, 1f) },
        };
    }

    private void OnGUI()
    {
        if (!_panelOpen) return;
        EnsureStyles();

        float w = 360f;
        float h = Mathf.Min(540f, Screen.height - 80f);
        var rect = new Rect(18f, 48f, w, h);

        GUILayout.BeginArea(rect, _panelStyle);
        DrawBenchwarpContents(showHeader: true);
        GUILayout.EndArea();
    }

    internal void DrawBenchwarpContents(bool showHeader)
    {
        EnsureStyles();

        if (showHeader)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Benchwarp ({_store.Count}) [{PlayerOptions.GetLabel(Shortcut.Benchwarp)}]", _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(26f)))
                SetPanelOpen(false);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"Last Bench [{PlayerOptions.GetLabel(Shortcut.LastBench)}]", GUILayout.Height(28f)))
            TryWarpToLastBench();
        if (GUILayout.Button($"Town Gate [{PlayerOptions.GetLabel(Shortcut.TownWarp)}]", GUILayout.Height(28f)))
        {
            SetPanelOpen(false);
            TownWarp.Instance?.TryWarpToTown();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Filter:", _labelStyle, GUILayout.Width(42f));
        _searchFilter = GUILayout.TextField(_searchFilter ?? string.Empty);
        if (!string.IsNullOrEmpty(_searchFilter) && GUILayout.Button("Clear", GUILayout.Width(48f)))
            _searchFilter = string.Empty;
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);

        var groups = _store.GetGroupedByArea(_searchFilter);
        if (groups.Count == 0)
        {
            GUILayout.Label(
                _store.Count == 0
                    ? "Sit on any bench to unlock it here."
                    : "No discovered benches match that filter.",
                _labelStyle);
            return;
        }

        _scrollPos = GUILayout.BeginScrollView(_scrollPos);
        foreach (var (area, benches) in groups)
        {
            GUILayout.Label(area, _areaStyle);
            foreach (BenchEntry bench in benches)
            {
                bool isLast = _store.LastBench != null && _store.LastBench.Equals(bench);
                string label = isLast ? $"★ {bench.DisplayName}" : bench.DisplayName;
                if (GUILayout.Button(label, GUILayout.Height(24f)))
                {
                    TryWarpToBench(bench);
                }
            }
            GUILayout.Space(4f);
        }
        GUILayout.EndScrollView();
    }

    private static Color AccentColor()
    {
#if HOLLOW_KNIGHT
        return new Color(0.55f, 0.80f, 1.0f, 1f);
#elif SILKSONG
        return new Color(1.0f, 0.45f, 0.42f, 1f);
#endif
    }
}
