using System;
using System.Collections.Generic;
using GlobalEnums;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

internal enum ToastKind
{
    Info,
    LocalItem,
    CrossGameItem,
    CheckSent,
    Error,
}

internal sealed class ToastEntry
{
    public string Text { get; }
    public ToastKind Kind { get; }
    public float CreatedAt { get; }

    public ToastEntry(string text, ToastKind kind, float createdAt)
    {
        Text = text;
        Kind = kind;
        CreatedAt = createdAt;
    }
}

/// <summary>
/// Owns the per-game Archipelago client, routes received items via <see cref="ItemRouter"/>,
/// persists processed indices and offline checks in <c>state.json</c>, and feeds toasts to
/// <see cref="ApOverlay"/>.
/// </summary>
internal sealed class ApManager : MonoBehaviour
{
    public const float ToastDurationSeconds = 6.5f;
    private const int MaxToasts = 6;
    private const int MaxLogLines = 12;

    internal static ApManager? Instance { get; private set; }
    internal static bool DeferAutoConnect { get; set; }
    internal static bool FullHostInstalled { get; set; }
    internal static bool DeferProcessedPersistence { get; set; }
    internal static event Action<ApClient>? OnConnected;
    internal static event Action? OnDisconnected;
    internal static event Action<string, ToastKind>? OnItemNotice;
    internal static Func<ApEvent, RoutedItem, bool>? LocalItemGrantHandler { get; set; }

    internal ApSettingsStore SettingsStore { get; private set; } = null!;
    internal ApSettings Settings { get; private set; } = null!;
    internal ApClient Client { get; private set; } = null!;

    internal IReadOnlyList<ToastEntry> Toasts => _toasts;
    internal IReadOnlyList<string> RecentLogs => _recentLogs;
    internal bool IsAutoConnectPending =>
        Settings != null
        && Settings.AutoConnect
        && Settings.IsConfigured
        && (_autoConnectRetriesRemaining > 0 || !_autoConnectAttempted || Client.State == ApConnectionState.Connecting);

    private readonly Queue<ApEvent> _pendingItems = new();
    private readonly List<ToastEntry> _toasts = new();
    private readonly List<string> _recentLogs = new();
    private readonly HashSet<string> _pendingNamedChecks = new(StringComparer.Ordinal);
    private bool _autoConnectAttempted;
    private int _autoConnectRetriesRemaining = 2;
    private float _nextAutoConnectTime;
    private int _inMemoryProcessed = -1;
    private float _nextGrantRetryTime;
#if DEBUG
    private bool _devCheckFired;
#endif

    private void Awake()
    {
        Instance = this;
        SettingsStore = new ApSettingsStore(HkssPlugin.Run);
        Settings = SettingsStore.LoadWithEnvOverrides(Environment.GetEnvironmentVariable);
        Client = new ApClient(HkssPlugin.ThisGame);
    }

    private void Start()
    {
        // Defer auto-connect until GameManager is initialized so heavy boot asset loads
        // (especially in Silksong) do not starve the WebSocket handshake timeout.
        _nextAutoConnectTime = Time.unscaledTime;
    }

    private void OnDestroy()
    {
        Client?.Dispose();
        if (Instance == this) Instance = null;
    }

    internal void SaveAndConnect(ApSettings updated)
    {
        Settings = updated.Clone();
        SettingsStore.Save(Settings);
        _pendingItems.Clear();
        _pendingNamedChecks.Clear();
        _inMemoryProcessed = -1;
        _nextGrantRetryTime = 0f;
        OnDisconnected?.Invoke();
        Client.Connect(Settings);
    }

    internal void Disconnect()
    {
        OnDisconnected?.Invoke();
        Client.Disconnect();
    }

    internal void FlushProcessedItemsToDisk()
    {
        if (_inMemoryProcessed >= 0)
        {
            HkssPlugin.State.SetProcessedItems(HkssPlugin.ThisGame, _inMemoryProcessed);
        }
    }

    /// <summary>
    /// Reports a real check by its location name (with or without the <c>HK </c>/<c>SS </c> prefix).
    /// If not yet connected, queues it so it flushes as soon as the session connects.
    /// </summary>
    internal void ReportCheckByName(string locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName)) return;

        long? combinedId = Client.ResolveLocationId(locationName);
        if (combinedId.HasValue)
        {
            ReportCheckById(combinedId.Value);
            return;
        }

        if (_pendingNamedChecks.Add(locationName))
        {
            HkssPlugin.Log.LogInfo($"Queued check '{locationName}' until Archipelago connects.");
            PushToast($"Queued check: {IdSpace.StripPrefix(locationName)}", ToastKind.CheckSent);
        }
    }

    internal void ReportCheckById(long combinedLocationId)
    {
        if (IdSpace.GameForId(combinedLocationId) != HkssPlugin.ThisGame)
        {
            HkssPlugin.Log.LogWarning($"Ignored check {combinedLocationId}: does not belong to {HkssPlugin.ThisGame}.");
            return;
        }

        bool isNew = HkssPlugin.State.RecordCheckedLocation(HkssPlugin.ThisGame, combinedLocationId);
        if (Client.IsConnected)
        {
            Client.CompleteLocation(combinedLocationId);
        }
        else if (isNew)
        {
            PushToast($"Saved offline check #{combinedLocationId}", ToastKind.CheckSent);
        }
    }

    internal void PushToast(string text, ToastKind kind = ToastKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _toasts.Add(new ToastEntry(text, kind, Time.unscaledTime));
        while (_toasts.Count > MaxToasts) _toasts.RemoveAt(0);
        AppendLog(text);
    }

    private void AppendLog(string text)
    {
        _recentLogs.Add(text);
        while (_recentLogs.Count > MaxLogLines) _recentLogs.RemoveAt(0);
    }

    private void Update()
    {
        PruneExpiredToasts();
        TryAutoConnect();

        while (Client.TryDequeueEvent(out ApEvent evt))
        {
            HandleEvent(evt);
        }

        if (_pendingItems.Count > 0 && IsSafeToGrantItem())
        {
            DrainPendingItems();
        }
#if DEBUG
        TryFireDevCheck();
#endif
    }

    private static GameManager? GetGameManagerQuietly()
    {
#if SILKSONG
        return GameManager.SilentInstance;
#else
        return GameManager.UnsafeInstance;
#endif
    }

    private void TryAutoConnect()
    {
        if (DeferAutoConnect) return;
        if (_autoConnectAttempted || !Settings.AutoConnect || !Settings.IsConfigured) return;
        if (GetGameManagerQuietly() == null) return;
        if (Time.unscaledTime < _nextAutoConnectTime) return;
        if (Client.State == ApConnectionState.Connecting || Client.State == ApConnectionState.Connected) return;

        _autoConnectAttempted = true;
        HkssPlugin.Log.LogInfo($"Auto-connecting to Archipelago at {Settings.FormatAddress()} as {Settings.Slot}");
        Client.Connect(Settings);
    }

#if DEBUG
    private void TryFireDevCheck()
    {
        if (_devCheckFired || !Client.IsConnected || !IsSafeToGrantItem()) return;
        string? flag = Environment.GetEnvironmentVariable("HKSS_DEV_CHECK_ON_CONNECT");
        if (string.IsNullOrEmpty(flag)) return;

        _devCheckFired = true;
#if HOLLOW_KNIGHT
        HkssPlugin.Log.LogInfo("[DEV] Triggering HK Fury_of_the_Fallen via PlayerData.SetBool(gotCharm_6, true)");
        PlayerData.instance.SetBool("gotCharm_6", true);
#elif SILKSONG
        HkssPlugin.Log.LogInfo("[DEV] Triggering SS Mossberry: Bone Bottom check");
        ReportCheckByName("SS Mossberry: Bone Bottom");
#endif
    }
#endif

    private void HandleEvent(ApEvent evt)
    {
        switch (evt.Kind)
        {
            case ApEventKind.Connected:
                HkssPlugin.Log.LogInfo(evt.Message);
                PushToast($"Connected as {Client.ConnectedSlot}", ToastKind.Info);
                CheckWorldVersion();
                SyncSeedBinding();
                FlushQueuedChecks();
                if (HkssPlugin.ThisGame == GameId.HollowKnight && HkssPlugin.State.IsHkGoalCompleted())
                {
                    Client.ReportLocalGoalComplete();
                }
                OnConnected?.Invoke(Client);
                break;

            case ApEventKind.Disconnected:
                HkssPlugin.Log.LogInfo(evt.Message);
                PushToast(evt.Message, ToastKind.Info);
                OnDisconnected?.Invoke();
                break;

            case ApEventKind.ConnectionFailed:
                HkssPlugin.Log.LogWarning(evt.Message);
                PushToast(evt.Message, ToastKind.Error);
                if (Settings.AutoConnect && _autoConnectRetriesRemaining > 0)
                {
                    _autoConnectRetriesRemaining--;
                    _autoConnectAttempted = false;
                    _nextAutoConnectTime = Time.unscaledTime + 2.5f;
                }
                break;

            case ApEventKind.Error:
                HkssPlugin.Log.LogWarning(evt.Message);
                PushToast(evt.Message, ToastKind.Error);
                break;

            case ApEventKind.LocationSent:
                HkssPlugin.Log.LogInfo(evt.Message);
                PushToast(evt.Message, ToastKind.CheckSent);
                break;

            case ApEventKind.GoalSent:
                HkssPlugin.Log.LogInfo(evt.Message);
                PushToast(evt.Message, ToastKind.LocalItem);
                break;

            case ApEventKind.LogMessage:
                AppendLog(evt.Message);
                break;

            case ApEventKind.ItemReceived:
                _pendingItems.Enqueue(evt);
                break;
        }
    }

    private void CheckWorldVersion()
    {
        string? worldVersion = Client.WorldVersion?.TrimStart('v', 'V');
        if (string.IsNullOrEmpty(worldVersion))
        {
            string msg = $"Room did not report an apworld version (0.1.2 or earlier); mod is v{HkssPlugin.PluginVersion}.";
            HkssPlugin.Log.LogWarning(msg);
            PushToast(msg, ToastKind.Error);
            return;
        }
        if (!string.Equals(worldVersion, HkssPlugin.PluginVersion, StringComparison.OrdinalIgnoreCase))
        {
            string msg = $"Version mismatch: room uses apworld v{worldVersion}, mod is v{HkssPlugin.PluginVersion}.";
            HkssPlugin.Log.LogWarning(msg);
            PushToast(msg, ToastKind.Error);
            return;
        }
        HkssPlugin.Log.LogInfo($"Room apworld version matches mod: v{worldVersion}");
    }

    private void SyncSeedBinding()
    {
        string seed = Client.Session?.RoomState?.Seed ?? string.Empty;
        if (string.IsNullOrEmpty(seed)) return;
        string seedKey = $"{seed}|{Client.ConnectedSlot}";
        if (HkssPlugin.State.EnsureSeedBinding(seedKey))
        {
            HkssPlugin.Log.LogWarning("[AP] Connected to a different Archipelago room/seed; reset run state counters and queued checks.");
            _pendingNamedChecks.Clear();
            _inMemoryProcessed = 0;
        }
    }

    private void FlushQueuedChecks()
    {
        if (!Client.IsConnected) return;

        if (_pendingNamedChecks.Count > 0)
        {
            var resolved = new List<string>();
            foreach (string name in _pendingNamedChecks)
            {
                long? id = Client.ResolveLocationId(name);
                if (id.HasValue)
                {
                    HkssPlugin.State.RecordCheckedLocation(HkssPlugin.ThisGame, id.Value);
                    resolved.Add(name);
                }
                else
                {
                    HkssPlugin.Log.LogWarning($"Could not resolve AP location name '{name}' for {HkssPlugin.ThisGame}.");
                }
            }
            foreach (string name in resolved) _pendingNamedChecks.Remove(name);
        }

        IReadOnlyList<long> saved = HkssPlugin.State.GetCheckedLocations(HkssPlugin.ThisGame);
        if (saved.Count > 0)
            Client.SyncCheckedLocations(saved);
    }

    private void DrainPendingItems()
    {
        if (Time.unscaledTime < _nextGrantRetryTime) return;

        int processed = _inMemoryProcessed >= 0
            ? _inMemoryProcessed
            : HkssPlugin.State.GetProcessedItems(HkssPlugin.ThisGame);
        _inMemoryProcessed = processed;
        bool grantedAny = false;

        while (_pendingItems.Count > 0)
        {
            ApEvent evt = _pendingItems.Peek();
            RoutedItem routed = ItemRouter.Route(
                HkssPlugin.ThisGame,
                evt.ItemIndex,
                processed,
                evt.ItemId,
                evt.ItemName,
                evt.SenderName,
                evt.IsLiveReceipt);

            switch (routed.Action)
            {
                case RouteAction.SkipAlreadyProcessed:
                    _pendingItems.Dequeue();
                    break;

                case RouteAction.ApplyLocally:
                    try
                    {
                        bool handled = LocalItemGrantHandler != null && LocalItemGrantHandler(evt, routed);
                        if (!handled)
                        {
                            if (FullHostInstalled)
                            {
                                // Host is installed (e.g. ItemChanger/Silksong) but not ready to grant yet; retry shortly
                                // without falling back to ThinSlice or advancing the processed index.
                                _nextGrantRetryTime = Time.unscaledTime + 0.25f;
                                return;
                            }
                            ThinSlice.GrantItem(routed);
                        }
                        _pendingItems.Dequeue();
                        grantedAny = true;
                        processed = evt.ItemIndex + 1;
                        _inMemoryProcessed = processed;
                        if (!DeferProcessedPersistence)
                            HkssPlugin.State.SetProcessedItems(HkssPlugin.ThisGame, processed);
                        HkssPlugin.Log.LogInfo($"[AP] Applied local item #{evt.ItemIndex}: {routed.RawName}");
                        PushToast(routed.Message, ToastKind.LocalItem);
                        OnItemNotice?.Invoke(routed.Message, ToastKind.LocalItem);
                    }
                    catch (Exception e)
                    {
                        HkssPlugin.Log.LogError($"[AP] Failed to grant {routed.RawName}: {e}");
                        PushToast($"Grant failed: {routed.DisplayName}", ToastKind.Error);
                        _nextGrantRetryTime = Time.unscaledTime + 1.0f;
                        return;
                    }
                    break;

                case RouteAction.CrossGameNotice:
                    _pendingItems.Dequeue();
                    processed = evt.ItemIndex + 1;
                    _inMemoryProcessed = processed;
                    if (!DeferProcessedPersistence)
                        HkssPlugin.State.SetProcessedItems(HkssPlugin.ThisGame, processed);
                    HkssPlugin.Log.LogInfo($"[AP] Cross-game item #{evt.ItemIndex}: {routed.Message}");
                    PushToast(routed.Message, ToastKind.CrossGameItem);
                    OnItemNotice?.Invoke(routed.Message, ToastKind.CrossGameItem);
                    break;

                case RouteAction.SkipOtherGameBacklog:
                case RouteAction.IgnoreUnknown:
                    _pendingItems.Dequeue();
                    processed = evt.ItemIndex + 1;
                    _inMemoryProcessed = processed;
                    if (!DeferProcessedPersistence)
                        HkssPlugin.State.SetProcessedItems(HkssPlugin.ThisGame, processed);
                    break;
            }
        }

        if (grantedAny)
        {
            HkssPlugin.Log.LogInfo($"[AP] Processed items for {HkssPlugin.ThisGame} now at {processed}.");
        }
    }

    private void PruneExpiredToasts()
    {
        float cutoff = Time.unscaledTime - ToastDurationSeconds;
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            if (_toasts[i].CreatedAt < cutoff)
                _toasts.RemoveAt(i);
        }
    }

    private static bool IsSafeToGrantItem()
    {
        GameManager? gm = GetGameManagerQuietly();
        if (gm == null || gm.IsInSceneTransition) return false;
#if HOLLOW_KNIGHT
        if (gm.gameState != GameState.PLAYING) return false;
#elif SILKSONG
        if (gm.GameState != GameState.PLAYING) return false;
#endif
        if (HeroController.instance == null || PlayerData.instance == null) return false;
        return true;
    }
}
