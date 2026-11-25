using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HKSS.Core;
using HKSS.Unity;
using SilksongRandomizer;
using SilksongRandomizer.Patches;
using UnityEngine;

namespace HKSS.Silksong;

/// <summary>
/// Bridges the ported Silksong randomizer (<see cref="RandomizerPlugin"/> and
/// <see cref="SilksongRandomizer.Archipelago"/>) onto <see cref="HKSS.Core"/> and
/// <see cref="HKSS.Unity.ApManager"/>.
/// </summary>
internal sealed class SsClientHost : MonoBehaviour
{
    private const float AutoConnectHoldTimeoutSeconds = 12f;

    private static ManualLogSource _log = null!;
    private static bool _randomizerActive;
    private SilksongRandomizer.Archipelago? _ap;
    private float _holdStartTime = -1f;
    private bool _holdingAutoLoad;

    internal static bool IsRandomizerActive => _randomizerActive;

    internal static void Install(GameObject host, Harmony harmony, ManualLogSource log, ConfigFile config)
    {
        _log = log;
        RandomizerPlugin plugin = host.AddComponent<RandomizerPlugin>();
        plugin.InitializeHost(log, config, harmony);
        host.AddComponent<SsClientHost>();
    }

    private void Awake()
    {
        ApManager.FullHostInstalled = true;
        ApManager.OnConnected += HandleApConnected;
        ApManager.OnDisconnected += HandleApDisconnected;
        ApManager.LocalItemGrantHandler = HandleLocalItemGrant;
    }

    private void Start()
    {
        ApManager? ap = ApManager.Instance;
        if (ap != null && ap.Settings.AutoConnect && ap.Settings.IsConfigured)
        {
            AutoLoad.HoldAutoLoad = true;
            _holdingAutoLoad = true;
            _holdStartTime = Time.unscaledTime;
            _log.LogInfo("[SS] Holding AutoLoad briefly until Archipelago connects.");
        }
    }

    private void OnDestroy()
    {
        ApManager.OnConnected -= HandleApConnected;
        ApManager.OnDisconnected -= HandleApDisconnected;
        if (ApManager.LocalItemGrantHandler == HandleLocalItemGrant)
        {
            ApManager.LocalItemGrantHandler = null;
        }
        ReleaseAutoLoadHold();
    }

    private void HandleApDisconnected()
    {
        _randomizerActive = false;
        if (SaveState.Instance == null)
        {
            ThinSlice.SuppressCheckHooks = false;
        }
        _ap?.Disconnect();
    }

    private void Update()
    {
        if (!_holdingAutoLoad) return;

        ApManager? ap = ApManager.Instance;
        if (_randomizerActive)
        {
            ReleaseAutoLoadHold();
            return;
        }

        if (ap != null && !ap.IsAutoConnectPending && ap.Client.State == ApConnectionState.Failed)
        {
            _log.LogWarning("[SS] Archipelago auto-connect failed; releasing AutoLoad hold.");
            ReleaseAutoLoadHold();
            return;
        }

        if (_holdStartTime >= 0f && Time.unscaledTime - _holdStartTime > AutoConnectHoldTimeoutSeconds)
        {
            _log.LogWarning("[SS] Timed out waiting for Archipelago auto-connect; releasing AutoLoad hold.");
            ReleaseAutoLoadHold();
        }
    }

    private void ReleaseAutoLoadHold()
    {
        if (!_holdingAutoLoad) return;
        _holdingAutoLoad = false;
        AutoLoad.HoldAutoLoad = false;
    }

    private void HandleApConnected(ApClient client)
    {
        try
        {
            SilksongRandomizer.Archipelago ap = RandomizerPlugin.Instance != null
                ? RandomizerPlugin.Instance.GetOrCreateArchipelago()
                : new SilksongRandomizer.Archipelago(IdSpace.GameName);
            _ap = ap;

            bool attached = ap.AttachToSharedSession(client);
            _randomizerActive = attached;
            ThinSlice.SuppressCheckHooks = attached;
            if (!attached)
            {
                _log.LogInfo($"[SS] Silksong randomizer inactive ({ap.LastError}); falling back to thin-slice mode.");
                ReleaseAutoLoadHold();
                return;
            }

            _log.LogInfo($"[SS] Attached Silksong randomizer to shared session (goal={ap.Goal}, start={ap.StartingLocation}, crest={ap.StartingCrest}).");

            // If gameplay already started before connecting (e.g., manual F6 connect), bind or resync SaveState now.
            GameManager? gm = GameManager.SilentInstance;
            if (gm != null && gm.IsGameplayScene() && PlayerData.instance != null)
            {
                if (SaveState.Instance == null)
                {
                    SavePatches.NewGame();
                }
                else
                {
                    ap.Resynchronize();
                }
            }

            ReleaseAutoLoadHold();
        }
        catch (Exception ex)
        {
            _randomizerActive = false;
            _log.LogError($"[SS] Failed to attach Silksong randomizer to shared session: {ex}");
            ReleaseAutoLoadHold();
        }
    }

    private static bool HandleLocalItemGrant(ApEvent evt, RoutedItem routed)
    {
        // When SilksongRandomizer is attached to the shared session, its own receipt-tracked
        // ItemGrants pipeline handles all Silksong items; return true so ThinSlice does not double-grant.
        return _randomizerActive;
    }
}
