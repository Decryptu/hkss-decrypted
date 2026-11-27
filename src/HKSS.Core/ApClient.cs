using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;

namespace HKSS.Core;

public enum ApConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}

public enum ApEventKind
{
    Connected,
    Disconnected,
    ConnectionFailed,
    ItemReceived,
    LogMessage,
    LocationSent,
    GoalSent,
    GoalStateSynced,
    Error,
}

public sealed class ApEvent
{
    public ApEventKind Kind { get; }
    public string Message { get; }
    public int ItemIndex { get; }
    public long ItemId { get; }
    public long LocationId { get; }
    public int SenderSlot { get; }
    public string ItemName { get; }
    public string SenderName { get; }
    public bool IsLiveReceipt { get; }

    public ApEvent(
        ApEventKind kind,
        string message = "",
        int itemIndex = -1,
        long itemId = 0,
        long locationId = 0,
        int senderSlot = -1,
        string itemName = "",
        string senderName = "",
        bool isLiveReceipt = false)
    {
        Kind = kind;
        Message = message;
        ItemIndex = itemIndex;
        ItemId = itemId;
        LocationId = locationId;
        SenderSlot = senderSlot;
        ItemName = itemName;
        SenderName = senderName;
        IsLiveReceipt = isLiveReceipt;
    }
}

/// <summary>
/// Thread-safe Archipelago client wrapper for a single game in a combo slot.
/// Network callbacks push events to a queue that the host (Unity main thread) drains in Update().
/// </summary>
public sealed class ApClient : IDisposable
{
    private static readonly Version ProtocolVersion = new(0, 6, 8);

    private readonly GameId _thisGame;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<ApEvent> _events = new();

    private ArchipelagoSession? _session;
    private int _generation;
    private bool _initialSyncDone;
    private int _queuedItemCount;
    private bool _comboGoalSent;
    private Hint[]? _roomHints;

    private volatile ApConnectionState _state = ApConnectionState.Disconnected;

    public GameId ThisGame => _thisGame;
    public ApConnectionState State { get => _state; private set => _state = value; }
    public string StatusDetail { get; private set; } = "Not connected";
    public string ConnectedSlot { get; private set; } = string.Empty;
    public int Team { get; private set; }
    public int Slot { get; private set; }
    public string GamesMode { get; private set; } = GoalTracker.GamesBoth;
    public string ComboGoal { get; private set; } = GoalTracker.ComboGoalBoth;
    public bool HkGoalCompleted { get; private set; }
    public bool SsGoalCompleted { get; private set; }
    public bool DeathLinkEnabled { get; private set; }
    public bool DoubleSteelSoul { get; private set; }
    public string? WorldVersion { get; private set; }
    public Dictionary<string, object>? SlotData { get; private set; }
    public ArchipelagoSession? Session { get { lock (_gate) return _session; } }
    public IReadOnlyList<Hint>? RoomHints { get { lock (_gate) return _roomHints; } }
    public bool IsConnected => State == ApConnectionState.Connected && _session != null;

    public ApClient(GameId thisGame)
    {
        _thisGame = thisGame;
    }

    public bool TryDequeueEvent(out ApEvent evt) => _events.TryDequeue(out evt!);

    public void Connect(ApSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (!settings.IsConfigured)
        {
            State = ApConnectionState.Failed;
            StatusDetail = "Enter server address and slot name.";
            _events.Enqueue(new ApEvent(ApEventKind.ConnectionFailed, StatusDetail));
            return;
        }

        ApSettings copy = settings.Clone();
        int gen;
        lock (_gate)
        {
            DisconnectInternal();
            gen = ++_generation;
            State = ApConnectionState.Connecting;
            StatusDetail = $"Connecting to {copy.FormatAddress()} as {copy.Slot}...";
            _initialSyncDone = false;
            _queuedItemCount = 0;
        }

        _ = Task.Run(() => ConnectWorker(copy, gen));
    }

    private void ConnectWorker(ApSettings settings, int gen)
    {
        ArchipelagoSession? session = null;
        try
        {
            session = ArchipelagoSessionFactory.CreateSession(settings.Host, settings.Port);
            session.Socket.SocketClosed += reason => OnSocketClosed(gen, reason);
            session.MessageLog.OnMessageReceived += msg => OnLogMessage(gen, msg);
            session.Items.ItemReceived += helper => OnItemReceived(gen, helper);

            LoginResult result = session.TryConnectAndLogin(
                IdSpace.GameName,
                settings.Slot,
                ItemsHandlingFlags.AllItems,
                ProtocolVersion,
                tags: null,
                uuid: null,
                password: string.IsNullOrEmpty(settings.Password) ? null : settings.Password,
                requestSlotData: true);

            if (result is LoginFailure failure)
            {
                string reason = string.Join("; ", failure.Errors ?? Array.Empty<string>());
                if (string.IsNullOrWhiteSpace(reason)) reason = "Login failed.";
                SafeClose(session);
                lock (_gate)
                {
                    if (gen != _generation) return;
                    State = ApConnectionState.Failed;
                    StatusDetail = reason;
                }
                _events.Enqueue(new ApEvent(ApEventKind.ConnectionFailed, reason));
                return;
            }

            LoginSuccessful ok = (LoginSuccessful)result;
            string gamesMode = ReadString(ok.SlotData, "games", GoalTracker.GamesBoth);
            string comboGoal = ReadString(ok.SlotData, "combo_goal", GoalTracker.ComboGoalBoth);
            bool deathLink = ReadBool(ok.SlotData, "death_link", false);
            bool doubleSteelSoul = ReadBool(ok.SlotData, "double_steel_soul", false);
            string rawWorldVersion = ReadString(ok.SlotData, "world_version", string.Empty).Trim();
            string? worldVersion = rawWorldVersion.Length > 0 ? rawWorldVersion : null;

            lock (_gate)
            {
                if (gen != _generation)
                {
                    SafeClose(session);
                    return;
                }

                _session = session;
                Team = ok.Team;
                Slot = ok.Slot;
                ConnectedSlot = settings.Slot;
                GamesMode = gamesMode;
                ComboGoal = comboGoal;
                DeathLinkEnabled = deathLink;
                DoubleSteelSoul = doubleSteelSoul;
                WorldVersion = worldVersion;
                SlotData = ok.SlotData;
                State = ApConnectionState.Connected;
                StatusDetail = $"Connected to {settings.FormatAddress()} as {settings.Slot}";

                // Emit Connected before initial items so listeners bind seed state before draining.
                _events.Enqueue(new ApEvent(ApEventKind.Connected, StatusDetail));

                // Queue any items already received during the login handshake before flipping live mode.
                DrainSessionItemsLocked(session, isLive: false);
                _initialSyncDone = true;
            }

            StartHintTracking(session, gen);
        }
        catch (Exception e)
        {
            SafeClose(session);
            lock (_gate)
            {
                if (gen != _generation) return;
                State = ApConnectionState.Failed;
                StatusDetail = e.Message;
            }
            _events.Enqueue(new ApEvent(ApEventKind.ConnectionFailed, e.Message));
        }
    }

    private void OnItemReceived(int gen, ReceivedItemsHelper helper)
    {
        lock (_gate)
        {
            if (gen != _generation || _session == null) return;
            DrainSessionItemsLocked(_session, isLive: _initialSyncDone);
        }
    }

    private void DrainSessionItemsLocked(ArchipelagoSession session, bool isLive)
    {
        IReadOnlyList<ItemInfo> all = session.Items.AllItemsReceived;
        while (_queuedItemCount < all.Count)
        {
            int idx = _queuedItemCount++;
            ItemInfo info = all[idx];
            string itemName = info.ItemDisplayName ?? info.ItemName ?? $"Item #{info.ItemId}";
            string senderName = info.Player?.Name ?? "Archipelago";
            _events.Enqueue(new ApEvent(
                ApEventKind.ItemReceived,
                itemIndex: idx,
                itemId: info.ItemId,
                locationId: info.LocationId,
                senderSlot: info.Player?.Slot ?? -1,
                itemName: itemName,
                senderName: senderName,
                isLiveReceipt: isLive));
        }
    }

    private void OnLogMessage(int gen, LogMessage message)
    {
        lock (_gate)
        {
            if (gen != _generation) return;
        }
        string text = message?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(text))
            _events.Enqueue(new ApEvent(ApEventKind.LogMessage, text));
    }

    private void StartHintTracking(ArchipelagoSession session, int gen)
    {
        try
        {
            session.DataStorage.TrackHints(hints =>
            {
                if (hints == null) return;
                lock (_gate)
                {
                    if (gen != _generation || !ReferenceEquals(_session, session)) return;
                    _roomHints = hints.ToArray();
                }
            }, retrieveCurrentlyUnlockedHints: true);
        }
        catch
        {
            // Hint tracking is optional; failures do not affect gameplay.
        }
    }

    private void OnSocketClosed(int gen, string reason)
    {
        bool notify = false;
        lock (_gate)
        {
            if (gen != _generation) return;
            if (State == ApConnectionState.Connected || State == ApConnectionState.Connecting)
            {
                State = ApConnectionState.Disconnected;
                StatusDetail = string.IsNullOrWhiteSpace(reason) ? "Disconnected" : $"Disconnected: {reason}";
                _session = null;
                _roomHints = null;
                notify = true;
            }
        }
        if (notify)
            _events.Enqueue(new ApEvent(ApEventKind.Disconnected, StatusDetail));
    }

    public long[] GetOwnLocationIds()
    {
        ArchipelagoSession? session;
        lock (_gate) session = _session;
        if (session == null) return Array.Empty<long>();
        return session.Locations.AllLocations
            .Where(id => IdSpace.GameForId(id) == _thisGame)
            .ToArray();
    }

    public long[] GetOwnCheckedLocationIds()
    {
        ArchipelagoSession? session;
        lock (_gate) session = _session;
        if (session == null) return Array.Empty<long>();
        return session.Locations.AllLocationsChecked
            .Where(id => IdSpace.GameForId(id) == _thisGame)
            .ToArray();
    }

    /// <summary>Checked and total slot locations for <paramref name="game"/>, or null when not connected.</summary>
    public (int Found, int Total)? GetCheckCounts(GameId game)
    {
        ArchipelagoSession? session;
        lock (_gate) session = _session;
        if (session == null || State != ApConnectionState.Connected) return null;
        try
        {
            int total = session.Locations.AllLocations.Count(id => IdSpace.GameForId(id) == game);
            int found = session.Locations.AllLocationsChecked.Count(id => IdSpace.GameForId(id) == game);
            return (found, total);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public long? ResolveLocationId(string locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName)) return null;
        string fullName = EnsureGamePrefix(locationName);
        ArchipelagoSession? session;
        lock (_gate) session = _session;
        if (session == null) return null;

        long id = session.Locations.GetLocationIdFromName(IdSpace.GameName, fullName);
        if (id < 0) return null;
        return IdSpace.GameForId(id) == _thisGame ? id : null;
    }

    public bool CompleteLocation(long combinedLocationId)
    {
        if (IdSpace.GameForId(combinedLocationId) != _thisGame)
            throw new ArgumentOutOfRangeException(nameof(combinedLocationId), combinedLocationId,
                $"Refused to report location {combinedLocationId} from {_thisGame}.");

        ArchipelagoSession? session;
        lock (_gate) session = _session;
        if (session == null || State != ApConnectionState.Connected) return false;

        try
        {
            if (session.Locations.AllLocationsChecked.Contains(combinedLocationId))
                return true;

            session.Locations.CompleteLocationChecks(combinedLocationId);
            string name = session.Locations.GetLocationNameFromId(combinedLocationId, IdSpace.GameName)
                ?? $"Location #{combinedLocationId}";
            _events.Enqueue(new ApEvent(ApEventKind.LocationSent, $"Checked {IdSpace.StripPrefix(name)}"));
            return true;
        }
        catch (Exception e)
        {
            _events.Enqueue(new ApEvent(ApEventKind.Error, $"Failed to send check {combinedLocationId}: {e.Message}"));
            return false;
        }
    }

    public void SyncCheckedLocations(IEnumerable<long> combinedLocationIds)
    {
        if (combinedLocationIds == null) return;
        long[] ownIds = combinedLocationIds.Where(id => IdSpace.GameForId(id) == _thisGame).Distinct().ToArray();
        if (ownIds.Length == 0) return;

        ArchipelagoSession? session;
        lock (_gate) session = _session;
        if (session == null || State != ApConnectionState.Connected) return;

        try
        {
            session.Locations.CompleteLocationChecks(ownIds);
        }
        catch (Exception e)
        {
            _events.Enqueue(new ApEvent(ApEventKind.Error, $"Failed to sync checks: {e.Message}"));
        }
    }

    public void ReportLocalGoalComplete()
    {
        SyncGoalProgress(
            localHkDone: _thisGame == GameId.HollowKnight,
            localSsDone: _thisGame == GameId.Silksong,
            notifyLocalCompletion: true);
    }

    public void SyncGoalProgress(bool localHkDone, bool localSsDone, bool notifyLocalCompletion = false)
    {
        ArchipelagoSession? session;
        int team, slot, generation;
        string gamesMode, comboGoal;
        lock (_gate)
        {
            session = _session;
            team = Team;
            slot = Slot;
            generation = _generation;
            gamesMode = GamesMode;
            comboGoal = ComboGoal;
            if (localHkDone) HkGoalCompleted = true;
            if (localSsDone) SsGoalCompleted = true;
        }
        if (session == null || State != ApConnectionState.Connected) return;

        _ = Task.Run(async () =>
        {
            try
            {
                string hkKey = GoalTracker.DataStorageKey(team, slot, GameId.HollowKnight);
                string ssKey = GoalTracker.DataStorageKey(team, slot, GameId.Silksong);

                if (localHkDone) session.DataStorage[Scope.Global, hkKey] = true;
                if (localSsDone) session.DataStorage[Scope.Global, ssKey] = true;

                bool remoteHkDone = localHkDone || await ReadBoolDataStorageAsync(session, hkKey);
                bool remoteSsDone = localSsDone || await ReadBoolDataStorageAsync(session, ssKey);

                bool shouldSendComboToast = false;
                bool stateChanged = false;
                bool comboSatisfied = GoalTracker.IsComboGoalSatisfied(gamesMode, comboGoal, remoteHkDone, remoteSsDone);
                lock (_gate)
                {
                    if (generation != _generation) return;
                    if (remoteHkDone && !HkGoalCompleted) { HkGoalCompleted = true; stateChanged = true; }
                    if (remoteSsDone && !SsGoalCompleted) { SsGoalCompleted = true; stateChanged = true; }
                    if (comboSatisfied && !_comboGoalSent)
                    {
                        _comboGoalSent = true;
                        shouldSendComboToast = true;
                    }
                }

                if (stateChanged || localHkDone || localSsDone)
                    _events.Enqueue(new ApEvent(ApEventKind.GoalStateSynced));

                if (comboSatisfied)
                {
                    session.SetGoalAchieved();
                    if (shouldSendComboToast)
                        _events.Enqueue(new ApEvent(ApEventKind.GoalSent, "Combo goal completed!"));
                }
                else if (notifyLocalCompletion)
                {
                    GameId otherGame = _thisGame == GameId.HollowKnight ? GameId.Silksong : GameId.HollowKnight;
                    _events.Enqueue(new ApEvent(
                        ApEventKind.GoalSent,
                        $"{ItemRouter.WorldName(_thisGame)} goal complete! Complete {ItemRouter.WorldName(otherGame)} to finish the combo goal."));
                }
            }
            catch (Exception e)
            {
                lock (_gate)
                {
                    if (generation != _generation) return;
                }
                _events.Enqueue(new ApEvent(ApEventKind.Error, $"Failed to report goal: {e.Message}"));
            }
        });
    }

    private static async Task<bool> ReadBoolDataStorageAsync(ArchipelagoSession session, string key)
    {
        try
        {
            return await session.DataStorage[Scope.Global, key].GetAsync<bool>();
        }
        catch
        {
            return false;
        }
    }

    public void Disconnect()
    {
        bool wasConnected;
        lock (_gate)
        {
            wasConnected = State == ApConnectionState.Connected || State == ApConnectionState.Connecting;
            _generation++;
            _comboGoalSent = false;
            HkGoalCompleted = false;
            SsGoalCompleted = false;
            DisconnectInternal();
            State = ApConnectionState.Disconnected;
            StatusDetail = "Disconnected";
        }
        if (wasConnected)
            _events.Enqueue(new ApEvent(ApEventKind.Disconnected, "Disconnected"));
    }

    public void Dispose() => Disconnect();

    private void DisconnectInternal()
    {
        ArchipelagoSession? old = _session;
        _session = null;
        _roomHints = null;
        if (old != null) SafeClose(old);
    }

    private static void SafeClose(ArchipelagoSession? session)
    {
        if (session == null) return;
        try
        {
            if (session.Socket.Connected)
                session.Socket.DisconnectAsync();
        }
        catch
        {
            // Ignore shutdown errors on an already closed socket.
        }
    }

    private string EnsureGamePrefix(string name)
    {
        string prefix = _thisGame == GameId.HollowKnight
            ? IdSpace.HollowKnightPrefix
            : IdSpace.SilksongPrefix;
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name : prefix + name;
    }

    private static string ReadString(Dictionary<string, object>? data, string key, string fallback)
    {
        if (data == null || !data.TryGetValue(key, out object? val) || val == null) return fallback;
        return val.ToString() ?? fallback;
    }

    private static bool ReadBool(Dictionary<string, object>? data, string key, bool fallback)
    {
        if (data == null || !data.TryGetValue(key, out object? val) || val == null) return fallback;
        if (val is bool b) return b;
        return bool.TryParse(val.ToString(), out bool parsed) ? parsed : fallback;
    }
}
