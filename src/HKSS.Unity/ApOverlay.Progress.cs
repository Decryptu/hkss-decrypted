using System;
using System.Linq;
using Archipelago.MultiClient.Net;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

internal sealed partial class ApOverlay
{
    private ArchipelagoSession? _progressSession;
    private float _nextProgressRefresh;
    private Vector2 _inventoryScroll;
    private Vector2 _hintsScroll;
    private string _itemSearch = string.Empty;
    private string _appliedItemSearch = string.Empty;
    private bool _showFoundHints;
    private bool _appliedShowFoundHints;
    private string[] _checkProgress = Array.Empty<string>();
    private (string Name, int Count)[] _inventory = Array.Empty<(string, int)>();
    private (string Item, string Location, bool Found)[]? _hints;
    private bool _hkGoalComplete;
    private bool _ssGoalComplete;
    private string _gamesMode = GoalTracker.GamesBoth;

    private void UpdateProgress()
    {
        // Keep the row layout stable between IMGUI's input and repaint events.
        _appliedItemSearch = _itemSearch.Trim();
        _appliedShowFoundHints = _showFoundHints;

        ApClient? client = ApManager.Instance?.Client;
        ArchipelagoSession? session = client?.Session;
        if (!ReferenceEquals(session, _progressSession))
        {
            _progressSession = session;
            _nextProgressRefresh = 0;
            _checkProgress = Array.Empty<string>();
            _inventory = Array.Empty<(string, int)>();
            _hints = null;
            _inventoryScroll = _hintsScroll = Vector2.zero;
        }

        if (client?.IsConnected != true || session == null ||
            (_activeTab != OverlayTab.Inventory && _activeTab != OverlayTab.Hints) ||
            Time.unscaledTime < _nextProgressRefresh)
            return;

        _nextProgressRefresh = Time.unscaledTime + 0.5f;
        if (_activeTab == OverlayTab.Inventory)
        {
            var locations = session.Locations.AllLocations.ToLookup(IdSpace.GameForId);
            var checkedLocations = session.Locations.AllLocationsChecked.ToLookup(IdSpace.GameForId);
            _checkProgress = new[] { GameId.HollowKnight, GameId.Silksong }
                .Where(game => locations.Contains(game))
                .Select(game => $"{ItemRouter.WorldName(game)} checks: {checkedLocations[game].Count()} / {locations[game].Count()}")
                .ToArray();
            _hkGoalComplete = HkssPlugin.State.IsGoalCompleted(GameId.HollowKnight) || client.HkGoalCompleted;
            _ssGoalComplete = HkssPlugin.State.IsGoalCompleted(GameId.Silksong) || client.SsGoalCompleted;
            _gamesMode = client.GamesMode;
            _inventory = session.Items.AllItemsReceived
                .GroupBy(item => item.ItemId)
                .Select(group => (Name: FormatName(group.Key, group.First().ItemName, "Item"), Count: group.Count()))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return;
        }

        _hints = client.RoomHints?
            .Where(hint => hint.ReceivingPlayer == client.Slot || hint.FindingPlayer == client.Slot)
            .Select(hint =>
            {
                string itemGame = session.Players.GetPlayerInfo(hint.ReceivingPlayer)?.Game ?? IdSpace.GameName;
                string locationGame = session.Players.GetPlayerInfo(hint.FindingPlayer)?.Game ?? IdSpace.GameName;
                string item = FormatName(hint.ItemId, session.Items.GetItemName(hint.ItemId, itemGame), "Item");
                string location = FormatName(hint.LocationId,
                    session.Locations.GetLocationNameFromId(hint.LocationId, locationGame), "Location");
                string receiver = session.Players.GetPlayerAlias(hint.ReceivingPlayer);
                string finder = session.Players.GetPlayerAlias(hint.FindingPlayer);
                return (Item: $"{item} for {receiver}", Location: $"{location} in {finder}'s world", hint.Found);
            })
            .OrderBy(hint => hint.Found)
            .ThenBy(hint => hint.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatName(long id, string? name, string fallback)
    {
        name = string.IsNullOrWhiteSpace(name) ? $"{fallback} #{id}" : name;
        if (CrossGameItems.TryGet(id, out CrossGameItem? shared))
            return $"{shared!.Name} (Both)";
        return IdSpace.GameForId(id) is GameId game
            ? $"{IdSpace.StripPrefix(name)} ({ItemRouter.WorldName(game)})"
            : name;
    }

    private bool DrawProgressConnectionStatus(ApClient client)
    {
        if (client.IsConnected && ReferenceEquals(client.Session, _progressSession))
            return true;
        GUILayout.Label("Connect to an Archipelago room to view your slot's progress.", _labelStyle);
        return false;
    }

    private void DrawInventoryTab(ApClient client)
    {
        if (!DrawProgressConnectionStatus(client)) return;

        GUILayout.Label("Slot progress", _sectionStyle);
        foreach (string line in _checkProgress)
            GUILayout.Label(line, _labelStyle);
        if (!string.Equals(_gamesMode, GoalTracker.GamesSilksongOnly, StringComparison.OrdinalIgnoreCase))
            GUILayout.Label(_hkGoalComplete ? "Hollow Knight goal: Complete" : "Hollow Knight goal: In progress", _labelStyle);
        if (!string.Equals(_gamesMode, GoalTracker.GamesHollowKnightOnly, StringComparison.OrdinalIgnoreCase))
            GUILayout.Label(_ssGoalComplete ? "Silksong goal: Complete" : "Silksong goal: In progress", _labelStyle);

        GUILayout.Space(6f);
        GUILayout.Label($"Received items ({_inventory.Sum(item => item.Count)})", _sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Filter items", _labelStyle, GUILayout.Width(72f));
        string search = GUILayout.TextField(_itemSearch);
        if (search != _itemSearch)
        {
            _itemSearch = search;
            _inventoryScroll = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        _inventoryScroll = GUILayout.BeginScrollView(_inventoryScroll);
        bool hasMatches = false;
        foreach (var item in _inventory)
        {
            if (item.Name.IndexOf(_appliedItemSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
            GUILayout.Label($"{item.Count} × {item.Name}", _labelStyle);
            hasMatches = true;
        }
        if (!hasMatches)
            GUILayout.Label(_inventory.Length == 0 ? "No items received yet." : "No matching items.", _labelStyle);
        GUILayout.EndScrollView();
    }

    private void DrawHintsTab(ApClient client)
    {
        if (!DrawProgressConnectionStatus(client)) return;
        if (_hints == null)
        {
            GUILayout.Label("Loading room hints...", _labelStyle);
            return;
        }

        bool showFound = GUILayout.Toggle(_showFoundHints, "Show found hints");
        if (showFound != _showFoundHints)
        {
            _showFoundHints = showFound;
            _hintsScroll = Vector2.zero;
        }

        _hintsScroll = GUILayout.BeginScrollView(_hintsScroll);
        bool hasHints = false;
        foreach (var hint in _hints)
        {
            if (hint.Found && !_appliedShowFoundHints) continue;
            GUILayout.Label(hint.Found ? $"{hint.Item} (found)" : hint.Item, _sectionStyle);
            GUILayout.Label(hint.Location, _labelStyle);
            GUILayout.Space(4f);
            hasHints = true;
        }
        if (!hasHints)
            GUILayout.Label(_hints.Length == 0 ? "No hints yet." : "All hints have been found.", _labelStyle);
        GUILayout.EndScrollView();
    }
}
