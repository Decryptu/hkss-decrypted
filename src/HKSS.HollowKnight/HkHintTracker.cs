// Ported from ArchipelagoMW/Archipelago.HollowKnight, HintTracker.cs & Extensions.cs, commit 373cc00 (MIT)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Exceptions;
using Archipelago.MultiClient.Net.Models;
using HKSS.Core;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Modules;
using ItemChanger.Placements;
using ItemChanger.Tags;
using Modding;
using UnityEngine.SceneManagement;

namespace HKSS.HollowKnight;

/// <summary>
/// Tracks Archipelago hints for Hollow Knight placements and automatically scouts/hints
/// previewed shops and locations when leaving a scene.
/// </summary>
public sealed class HkHintTracker : Module
{
    public static event Action? OnArchipelagoHintUpdate;
    public static List<Hint>? Hints { get; private set; }
    private static ArchipelagoSession? _hintSession;

    internal static IReadOnlyList<Hint>? GetRoomHints(ArchipelagoSession session) =>
        ReferenceEquals(session, _hintSession) ? Hints : null;

    private readonly HashSet<AbstractPlacement> _pendingPlacementHints = new();
    private readonly ConcurrentQueue<(ArchipelagoSession Session, Hint[] Hints)> _pendingHintUpdates = new();
    private ArchipelagoSession? _trackedSession;
    private bool _inGame;

    public override void Initialize()
    {
        _pendingPlacementHints.Clear();
        while (_pendingHintUpdates.TryDequeue(out _)) { }
        _trackedSession = null;
        _hintSession = null;
        Hints = null;
        _inGame = false;

        Events.OnEnterGame += OnEnterGame;
        ApManager.OnConnected += OnApConnected;
        ApManager.OnDisconnected += OnApDisconnected;
        ModHooks.HeroUpdateHook += DrainHintUpdates;
    }

    public override async void Unload()
    {
        Events.OnEnterGame -= OnEnterGame;
        ApManager.OnConnected -= OnApConnected;
        ApManager.OnDisconnected -= OnApDisconnected;
        ModHooks.HeroUpdateHook -= DrainHintUpdates;
        AbstractItem.AfterGiveGlobal -= UpdateHintFoundStatus;
        Events.OnSceneChange -= SendHintsOnSceneChange;
        _inGame = false;
        _trackedSession = null;
        _hintSession = null;
        Hints = null;
        await SendPlacementHintsAsync();
    }

    private void OnEnterGame()
    {
        _inGame = true;
        AbstractItem.AfterGiveGlobal += UpdateHintFoundStatus;
        Events.OnSceneChange += SendHintsOnSceneChange;
        TryTrackHints();
    }

    private void OnApConnected(ApClient client)
    {
        if (_inGame)
        {
            TryTrackHints();
            _ = SendPlacementHintsAsync();
        }
    }

    private void OnApDisconnected()
    {
        _trackedSession = null;
        _hintSession = null;
        Hints = null;
    }

    private void TryTrackHints()
    {
        ArchipelagoSession? session = ApManager.Instance?.Client.Session;
        if (session == null || ReferenceEquals(session, _trackedSession)) return;

        _trackedSession = session;
        Hints = null;
        try
        {
            session.DataStorage.TrackHints(hints =>
            {
                if (hints != null)
                {
                    _pendingHintUpdates.Enqueue((session, hints));
                }
            }, retrieveCurrentlyUnlockedHints: true);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK] Failed to subscribe to Archipelago hints: {ex.Message}");
        }
    }

    public void HintPlacement(AbstractPlacement pmt)
    {
        if (pmt != null)
        {
            _pendingPlacementHints.Add(pmt);
        }
    }

    private void DrainHintUpdates()
    {
        Hint[]? latest = null;
        while (_pendingHintUpdates.TryDequeue(out var next))
        {
            if (ReferenceEquals(next.Session, _trackedSession) &&
                ReferenceEquals(next.Session, ApManager.Instance?.Client.Session))
                latest = next.Hints;
        }
        if (latest != null)
        {
            ApplyHintUpdate(latest);
        }
    }

    private void ApplyHintUpdate(Hint[] arrayHints)
    {
        ApClient? client = ApManager.Instance?.Client;
        int mySlot = client?.Slot ?? -1;
        _hintSession = _trackedSession;
        Hints = arrayHints.ToList();

        foreach (Hint hint in Hints)
        {
            if (hint.FindingPlayer != mySlot) continue;
            if (!IdSpace.IsHollowKnight(hint.LocationId)) continue;
            if (!HkPlacementTag.PlacementsByLocationId.TryGetValue(hint.LocationId, out AbstractPlacement? placement) || placement == null)
                continue;

            foreach (AbstractItem item in placement.Items)
            {
                if (item.GetTag(out HkItemTag tag) && tag != null && tag.Location == hint.LocationId)
                {
                    tag.Hinted = true;
                }
            }

            if (placement.Items.TrueForAll(item => !item.GetTag(out HkItemTag t) || t == null || t.Hinted))
            {
                if (placement.GetTag(out HkPlacementTag pTag) && pTag != null)
                {
                    pTag.Hinted = true;
                }
            }

            if (placement is ShopPlacement shop)
            {
                var previewRecordTag = shop.GetOrAddTag<MultiPreviewRecordTag>();
                if (previewRecordTag.previewTexts == null || previewRecordTag.previewTexts.Length != shop.Items.Count)
                {
                    previewRecordTag.previewTexts = new string[shop.Items.Count];
                }

                for (int i = 0; i < shop.Items.Count; i++)
                {
                    AbstractItem item = shop.Items[i];
                    bool hinted = item.GetTag(out HkItemTag t) && t != null && t.Hinted;
                    previewRecordTag.previewTexts[i] = hinted
                        ? GetPreviewWithCost(item)
                        : Language.Language.Get("???", "IC");
                }
            }
            else
            {
                var previewText = new List<string>();
                foreach (AbstractItem item in placement.Items)
                {
                    if (item.WasEverObtained()) continue;
                    bool hinted = item.GetTag(out HkItemTag t) && t != null && t.Hinted;
                    previewText.Add(hinted
                        ? GetPreviewWithCost(item)
                        : Language.Language.Get("???", "IC"));
                }

                placement.GetOrAddTag<PreviewRecordTag>().previewText =
                    string.Join(Language.Language.Get("COMMA_SPACE", "IC"), previewText);
            }
        }

        try
        {
            OnArchipelagoHintUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogError($"[HK] Error invoking OnArchipelagoHintUpdate: {ex}");
        }
    }

    private void UpdateHintFoundStatus(ReadOnlyGiveEventArgs args)
    {
        if (Hints == null || args.Orig == null || !args.Orig.GetTag(out HkItemTag tag) || tag == null)
            return;

        long location = tag.Location;
        foreach (Hint hint in Hints)
        {
            if (hint.LocationId == location && hint.FindingPlayer == ApManager.Instance?.Client.Slot)
            {
                hint.Found = true;
                try
                {
                    OnArchipelagoHintUpdate?.Invoke();
                }
                catch (Exception ex)
                {
                    HkssPlugin.Log.LogError($"[HK] Error invoking OnArchipelagoHintUpdate: {ex}");
                }
                break;
            }
        }
    }

    private async void SendHintsOnSceneChange(Scene scene)
    {
        await SendPlacementHintsAsync();
    }

    private async Task SendPlacementHintsAsync()
    {
        if (_pendingPlacementHints.Count == 0) return;

        ArchipelagoSession? session = ApManager.Instance?.Client.Session;
        if (session == null || !session.Socket.Connected) return;

        var hintedTags = new HashSet<HkItemTag>();
        var hintedLocationIds = new HashSet<long>();

        foreach (AbstractPlacement pmt in _pendingPlacementHints)
        {
            foreach (AbstractItem item in pmt.Items)
            {
                if (item.GetTag(out HkItemTag tag) && tag != null && !tag.Hinted)
                {
                    if ((tag.Flags.HasFlag(ItemFlags.Advancement) || tag.Flags.HasFlag(ItemFlags.NeverExclude))
                        && !item.WasEverObtained()
                        && !item.HasTag<DisableItemPreviewTag>()
                        && tag.Location > 0)
                    {
                        hintedTags.Add(tag);
                        hintedLocationIds.Add(tag.Location);
                    }
                    else
                    {
                        tag.Hinted = true;
                    }
                }
            }
        }

        _pendingPlacementHints.Clear();
        if (hintedLocationIds.Count == 0) return;

        HkssPlugin.Log.LogDebug($"[HK] Auto-hinting {hintedLocationIds.Count} previewed location(s).");
        try
        {
            Task<Dictionary<long, ScoutedItemInfo>> scoutTask =
                session.Locations.ScoutLocationsAsync(HintCreationPolicy.CreateAndAnnounceOnce, hintedLocationIds.ToArray());
            Task completed = await Task.WhenAny(scoutTask, Task.Delay(2500));
            if (completed == scoutTask && !scoutTask.IsFaulted)
            {
                foreach (HkItemTag tag in hintedTags)
                {
                    tag.Hinted = true;
                }
            }
        }
        catch (Exception ex) when (ex is ArchipelagoSocketClosedException or TimeoutException)
        {
            HkssPlugin.Log.LogWarning($"[HK] Auto-hint request did not complete: {ex.Message}");
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK] Auto-hint request failed: {ex.Message}");
        }
    }

    private static string GetPreviewWithCost(AbstractItem item)
    {
        string text = item.GetPreviewName();
        if (item.GetTag(out CostTag tag) && tag?.Cost != null)
        {
            text += "  -  " + tag.Cost.GetCostText();
        }
        return text;
    }
}
