using System;
using GlobalEnums;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Enforces opt-in Double Steel Soul across both games of a combo run.
/// Tracks the run's Steel Soul mode and defeat state in <c>state.json</c>, detects permadeath
/// in either game, marks the shared run defeated even while the other game is closed, and
/// surfaces a defeat overlay in both games.
/// </summary>
internal sealed class DoubleSteelSoul : MonoBehaviour
{
    private static bool _enabled;
    private static bool _defeated;
    private static string _defeatedGame = string.Empty;
    private static string _defeatedScene = string.Empty;
    private static bool _overlayVisible;

    private GUIStyle? _scrimStyle;
    private GUIStyle? _cardStyle;
    private GUIStyle? _titleStyle;
    private GUIStyle? _subtitleStyle;
    private GUIStyle? _bodyStyle;
    private GUIStyle? _buttonStyle;
    private Texture2D? _scrimTex;
    private Texture2D? _cardTex;
    private Texture2D? _btnTex;

    internal static bool IsEnabled
    {
        get
        {
            if (_enabled || _defeated) return true;
            if (ApManager.Instance?.Client?.DoubleSteelSoul == true) return true;
            return HkssPlugin.State?.IsDoubleSteelSoul() == true;
        }
    }

    internal static bool IsDefeated => _defeated || HkssPlugin.State?.IsSteelSoulDefeated() == true;

    internal static string DefeatedWorldName => FormatWorldName(_defeatedGame);

    internal static string DefeatedScene => _defeatedScene;

    internal static void RefreshFromDisk()
    {
        if (HkssPlugin.State == null) return;
        RunState state = HkssPlugin.State.Load();
        _enabled = state.DoubleSteelSoul || state.SteelSoulDefeated;
        _defeated = state.SteelSoulDefeated;
        _defeatedGame = state.SteelSoulDefeatedGame ?? string.Empty;
        _defeatedScene = state.SteelSoulDefeatedScene ?? string.Empty;
    }

    internal static void Install(Harmony harmony)
    {
        RefreshFromDisk();

        var playerDead = AccessTools.Method(typeof(GameManager), nameof(GameManager.PlayerDead), new[] { typeof(float) });
        if (playerDead != null)
        {
            harmony.Patch(playerDead, prefix: new HarmonyMethod(typeof(DoubleSteelSoul), nameof(BeforePlayerDead)));
        }
        else
        {
            HkssPlugin.Log.LogWarning("[DoubleSteelSoul] GameManager.PlayerDead not found; permadeath detection may be delayed.");
        }
    }

    internal static void SyncFromSlotData(bool enabled)
    {
        RefreshFromDisk();
        if (enabled)
        {
            _enabled = true;
            HkssPlugin.State.SetDoubleSteelSoul(true);
            EnsureActivePlayerDataPermadeath();
        }
    }

    internal static void ShowDefeatOverlay()
    {
        RefreshFromDisk();
        if (!_defeated) return;
        if (_overlayVisible) return;
        _overlayVisible = true;
        OverlayCursor.SetOpen(OverlayCursor.Panel.SteelSoul, true);
    }

    internal static string FormatWorldName(string gameKey) => gameKey switch
    {
        "hollow_knight" => "Hallownest",
        "silksong" => "Pharloom",
        _ => "this run",
    };

    private static void BeforePlayerDead(GameManager __instance)
    {
        if (__instance == null) return;
        PlayerData? pd = __instance.playerData;
        if (pd == null) return;

        if (IsPermaDead(pd))
        {
            RecordDefeat(__instance.sceneName);
        }
    }

    private static void RecordDefeat(string? sceneName)
    {
        if (!IsEnabled && !IsPermaDead(PlayerData.instance)) return;

        string scene = sceneName ?? string.Empty;
        bool newlyDefeated = HkssPlugin.State.MarkSteelSoulDefeated(HkssPlugin.ThisGame, scene);
        RefreshFromDisk();
        RunStatsTracker.Flush();

        if (newlyDefeated)
        {
            string world = ItemRouter.WorldName(HkssPlugin.ThisGame);
            string where = string.IsNullOrWhiteSpace(scene) ? world : $"{world} ({scene})";
            HkssPlugin.Log.LogWarning($"[DoubleSteelSoul] Steel Soul run defeated in {where}.");
            ApManager.Instance?.PushToast($"Steel Soul defeated in {world} — run ended.", ToastKind.Error);
        }
    }

    private static bool IsPermaDead(PlayerData? pd)
    {
        if (pd == null) return false;
#if HOLLOW_KNIGHT
        return pd.permadeathMode == 2;
#elif SILKSONG
        return pd.permadeathMode == PermadeathModes.Dead;
#endif
    }

    private static void EnsureActivePlayerDataPermadeath()
    {
        PlayerData? pd = PlayerData.instance;
        if (pd == null) return;

        if (_defeated)
        {
#if HOLLOW_KNIGHT
            if (pd.permadeathMode != 2) pd.permadeathMode = 2;
#elif SILKSONG
            if (pd.permadeathMode != PermadeathModes.Dead) pd.permadeathMode = PermadeathModes.Dead;
#endif
            return;
        }

        if (!_enabled) return;

#if HOLLOW_KNIGHT
        if (pd.permadeathMode == 0)
        {
            pd.permadeathMode = 1;
            HkssPlugin.Log.LogInfo("[DoubleSteelSoul] Enabled Steel Soul on active Hollow Knight save.");
        }
#elif SILKSONG
        if (pd.permadeathMode == PermadeathModes.Off)
        {
            pd.permadeathMode = PermadeathModes.On;
            HkssPlugin.Log.LogInfo("[DoubleSteelSoul] Enabled Steel Soul on active Silksong save.");
        }
#endif
    }

    private void OnEnable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;

    private void OnDisable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;

    private void OnDestroy()
    {
        if (_overlayVisible)
        {
            _overlayVisible = false;
            OverlayCursor.SetOpen(OverlayCursor.Panel.SteelSoul, false);
        }
        if (_scrimTex != null) Destroy(_scrimTex);
        if (_cardTex != null) Destroy(_cardTex);
        if (_btnTex != null) Destroy(_btnTex);
    }

    private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
    {
        string sceneName = to.name ?? string.Empty;
        if (string.Equals(sceneName, "PermaDeath", StringComparison.Ordinal))
        {
            RecordDefeat(from.name);
            return;
        }

        if (IsDefeated && string.Equals(sceneName, "Menu_Title", StringComparison.Ordinal))
        {
            ShowDefeatOverlay();
            return;
        }

        GameManager? gm = GameManager._instance;
        if (gm != null && gm.IsGameplayScene())
        {
            EnsureActivePlayerDataPermadeath();
            if (IsDefeated)
            {
                ShowDefeatOverlay();
            }
        }
    }

    private void Update()
    {
        if (!_defeated && _enabled)
        {
            PlayerData? pd = PlayerData.instance;
            if (pd != null && IsPermaDead(pd))
            {
                RecordDefeat(GameManager._instance?.sceneName);
            }
        }

        if (_defeated && !_overlayVisible)
        {
            GameManager? gm = GameManager._instance;
            string scene = gm?.sceneName ?? string.Empty;
            // Let the game's own PermaDeath cutscene play before surfacing our overlay on Menu_Title.
            if (!string.Equals(scene, "PermaDeath", StringComparison.Ordinal) && AutoLoad.IsReady)
            {
                ShowDefeatOverlay();
            }
        }
    }

    private void EnsureStyles()
    {
        if (_scrimTex == null)
        {
            _scrimTex = MakeSolid(new Color(0.03f, 0.02f, 0.04f, 0.90f));
            _cardTex = MakeSolid(new Color(0.08f, 0.06f, 0.09f, 0.98f));
            _btnTex = MakeSolid(new Color(0.32f, 0.11f, 0.13f, 0.95f));
        }

        _scrimStyle ??= new GUIStyle { normal = { background = _scrimTex } };
        _cardStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(28, 28, 24, 24),
            normal = { background = _cardTex, textColor = Color.white },
        };
        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.95f, 0.42f, 0.40f, 1f) },
        };
        _subtitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.96f, 0.85f, 0.58f, 1f) },
        };
        _bodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            wordWrap = true,
            normal = { textColor = new Color(0.90f, 0.91f, 0.95f, 1f) },
        };
        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { background = _btnTex, textColor = Color.white },
            hover = { background = _btnTex, textColor = new Color(1f, 0.92f, 0.72f, 1f) },
        };
    }

    private void OnGUI()
    {
        if (!_overlayVisible) return;

        EnsureStyles();
        GUI.depth = -100;
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, _scrimStyle);

        float w = Mathf.Min(500f, Screen.width - 40f);
        float h = 250f;
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        GUILayout.BeginArea(rect, _cardStyle);
        GUILayout.Label("DOUBLE STEEL SOUL — DEFEATED", _titleStyle);
        GUILayout.Space(8f);

        string where = string.IsNullOrWhiteSpace(_defeatedScene)
            ? $"Fell in {DefeatedWorldName}"
            : $"Fell in {DefeatedWorldName} ({_defeatedScene})";
        GUILayout.Label(where, _subtitleStyle);
        GUILayout.Space(12f);

        GUILayout.Label(
            "A Steel Soul death in either world ends the combo run. Both Hallownest and Pharloom saves are defeated.\n\nReturn to the HKSS Launcher to reset the run with fresh saves in both games.",
            _bodyStyle);
        GUILayout.FlexibleSpace();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Quit to Launcher", _buttonStyle, GUILayout.Width(200f), GUILayout.Height(36f)))
        {
            Application.Quit();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private static Texture2D MakeSolid(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
