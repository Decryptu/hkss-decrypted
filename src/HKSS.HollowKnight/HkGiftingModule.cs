using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using BepInEx.Configuration;
using HKSS.Core;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Modules;
using ItemChanger.UIDefs;
using Modding;
using Newtonsoft.Json.Linq;

namespace HKSS.HollowKnight;

/// <summary>
/// Archipelago Gifting module for Hollow Knight (AP GiftBox v2/v3 over DataStorage).
/// Receives gifts from other multiworld players and lets the player send Geo or Rancid Eggs.
/// </summary>
public sealed class HkGiftingModule : Module
{
    internal static HkGiftingModule? Instance { get; private set; }

    private static ConfigEntry<bool>? _enabledConfig;

    internal static bool IsEnabled
    {
        get => _enabledConfig?.Value ?? true;
        set
        {
            if (_enabledConfig != null) _enabledConfig.Value = value;
            Instance?.RefreshGiftBoxState();
        }
    }

    private readonly ConcurrentQueue<ApGift> _pendingGifts = new();
    private readonly HashSet<string> _processedIds = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private ArchipelagoSession? _session;
    private DataStorageElement? _boxElement;
    private DataStorageHelper.DataStorageUpdatedHandler? _boxHandler;
    private int _team;
    private int _slot;
    private List<(int Slot, string Name)> _openRecipients = new();

    internal IReadOnlyList<(int Slot, string Name)> OpenRecipients
    {
        get { lock (_gate) return _openRecipients.ToList(); }
    }

    internal static void BindConfig(ConfigFile? config)
    {
        if (config == null || _enabledConfig != null) return;
        _enabledConfig = config.Bind(
            "Archipelago",
            "EnableGifting",
            true,
            "Open an Archipelago GiftBox in Hollow Knight to receive and send gifts.");
    }

    public override void Initialize()
    {
        Instance = this;
        BindConfig(HkssPlugin.PluginConfig);
        ApManager.OnConnected += AttachToSession;
        ApManager.OnDisconnected += DetachFromSession;
        ModHooks.HeroUpdateHook += OnHeroUpdate;

        if (ApManager.Instance?.Client is ApClient client && client.IsConnected)
        {
            AttachToSession(client);
        }
    }

    public override void Unload()
    {
        if (Instance == this) Instance = null;
        ApManager.OnConnected -= AttachToSession;
        ApManager.OnDisconnected -= DetachFromSession;
        ModHooks.HeroUpdateHook -= OnHeroUpdate;
        DetachFromSession();
    }

    internal void RefreshGiftBoxState()
    {
        ArchipelagoSession? session = _session;
        if (session == null || !session.Socket.Connected) return;
        PublishMetadata(session, _team, _slot, IsEnabled);
    }

    internal void RefreshOpenRecipientsAsync()
    {
        ArchipelagoSession? session = _session;
        int team = _team;
        int mySlot = _slot;
        if (session == null || !session.Socket.Connected) return;

        _ = Task.Run(async () =>
        {
            try
            {
                JObject? boxes = await session.DataStorage[Scope.Global, GiftProtocol.GiftBoxesKey(team)].GetAsync<JObject>();
                var list = new List<(int Slot, string Name)>();
                if (boxes != null)
                {
                    foreach (JProperty prop in boxes.Properties())
                    {
                        if (!int.TryParse(prop.Name, out int targetSlot) || targetSlot == mySlot) continue;
                        ApGiftBoxMetadata? meta = prop.Value?.ToObject<ApGiftBoxMetadata>();
                        if (meta == null || !meta.IsOpen) continue;
                        string name = session.Players?.GetPlayerName(targetSlot) ?? $"Slot {targetSlot}";
                        list.Add((targetSlot, name));
                    }
                }
                lock (_gate)
                {
                    _openRecipients = list;
                }
            }
            catch (Exception ex)
            {
                HkssPlugin.Log.LogDebug($"[HK Gifting] Failed to refresh recipients: {ex.Message}");
            }
        });
    }

    internal bool TrySendGeoGift(int targetSlot, int geoAmount)
    {
        if (!IsEnabled || _session == null || !_session.Socket.Connected)
        {
            ApManager.Instance?.PushToast("Connect to Archipelago with Gifting enabled first.", ToastKind.Error);
            return false;
        }
        HeroController? hc = HeroController.instance;
        PlayerData? pd = PlayerData.instance;
        if (hc == null || pd == null || pd.geo < geoAmount)
        {
            ApManager.Instance?.PushToast($"Not enough Geo (need {geoAmount}).", ToastKind.Error);
            return false;
        }

        hc.TakeGeo(geoAmount);
        ApGift gift = GiftProtocol.CreateGeoGift(_team, _slot, targetSlot, geoAmount);
        return SendGift(_session, gift, targetSlot);
    }

    internal bool TrySendRancidEggGift(int targetSlot)
    {
        if (!IsEnabled || _session == null || !_session.Socket.Connected)
        {
            ApManager.Instance?.PushToast("Connect to Archipelago with Gifting enabled first.", ToastKind.Error);
            return false;
        }
        PlayerData? pd = PlayerData.instance;
        if (pd == null || pd.rancidEggs < 1)
        {
            ApManager.Instance?.PushToast("You have no Rancid Eggs to gift.", ToastKind.Error);
            return false;
        }

        pd.DecrementInt(nameof(PlayerData.rancidEggs));
        ApGift gift = GiftProtocol.CreateRancidEggGift(_team, _slot, targetSlot);
        return SendGift(_session, gift, targetSlot);
    }

    private bool SendGift(ArchipelagoSession session, ApGift gift, int targetSlot)
    {
        try
        {
            string key = GiftProtocol.GiftBoxKey(_team, targetSlot);
            var update = new Dictionary<string, JToken>
            {
                [gift.Id] = JObject.FromObject(gift),
            };
            session.DataStorage[Scope.Global, key] += Operation.Update(update);
            string targetName = session.Players?.GetPlayerName(targetSlot) ?? $"Slot {targetSlot}";
            ApManager.Instance?.PushToast($"Sent gift '{gift.ItemName}' to {targetName}!", ToastKind.Info);
            HkssPlugin.Log.LogInfo($"[HK Gifting] Sent '{gift.ItemName}' to {targetName} (slot {targetSlot}).");
            return true;
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK Gifting] Failed to send gift: {ex.Message}");
            ApManager.Instance?.PushToast($"Failed to send gift: {ex.Message}", ToastKind.Error);
            return false;
        }
    }

    private void AttachToSession(ApClient client)
    {
        DetachFromSession();
        ArchipelagoSession? session = client.Session;
        if (session == null) return;

        try
        {
            _session = session;
            _team = client.Team;
            _slot = client.Slot;

            PublishMetadata(session, _team, _slot, IsEnabled);

            string boxKey = GiftProtocol.GiftBoxKey(_team, _slot);
            DataStorageElement element = session.DataStorage[Scope.Global, boxKey];
            element.Initialize(new JObject());

            DataStorageHelper.DataStorageUpdatedHandler handler = (_, newValue, _) => EnqueueFromToken(newValue);
            element.OnValueChanged += handler;
            _boxElement = element;
            _boxHandler = handler;

            _ = Task.Run(async () =>
            {
                try
                {
                    JToken? raw = await element.GetAsync<JToken>();
                    EnqueueFromToken(raw);
                }
                catch (Exception ex)
                {
                    HkssPlugin.Log.LogDebug($"[HK Gifting] Initial box read skipped: {ex.Message}");
                }
            });

            RefreshOpenRecipientsAsync();
            HkssPlugin.Log.LogInfo($"[HK Gifting] GiftBox active (enabled={IsEnabled}, key={boxKey}).");
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK Gifting] Failed to attach: {ex.Message}");
            DetachFromSession();
        }
    }

    private void DetachFromSession()
    {
        if (_boxElement != null && _boxHandler != null)
        {
            try
            {
                _boxElement.OnValueChanged -= _boxHandler;
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
        _boxElement = null;
        _boxHandler = null;
        _session = null;
        lock (_gate) _openRecipients.Clear();
        while (_pendingGifts.TryDequeue(out _)) { }
    }

    private static void PublishMetadata(ArchipelagoSession session, int team, int slot, bool isOpen)
    {
        try
        {
            string key = GiftProtocol.GiftBoxesKey(team);
            session.DataStorage[Scope.Global, key].Initialize(new JObject());
            var update = new Dictionary<string, JToken>
            {
                [slot.ToString()] = JObject.FromObject(GiftProtocol.CreateDefaultMetadata(isOpen)),
            };
            session.DataStorage[Scope.Global, key] += Operation.Update(update);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK Gifting] Failed to publish GiftBox metadata: {ex.Message}");
        }
    }

    private void EnqueueFromToken(JToken? token)
    {
        if (!IsEnabled || token is not JObject obj) return;
        foreach (JProperty prop in obj.Properties())
        {
            try
            {
                ApGift? gift = prop.Value?.ToObject<ApGift>();
                if (gift == null) continue;
                if (string.IsNullOrEmpty(gift.Id)) gift.Id = prop.Name;

                lock (_gate)
                {
                    if (!_processedIds.Add(gift.Id)) continue;
                }
                _pendingGifts.Enqueue(gift);
            }
            catch (Exception ex)
            {
                HkssPlugin.Log.LogDebug($"[HK Gifting] Ignored malformed gift '{prop.Name}': {ex.Message}");
            }
        }
    }

    private void OnHeroUpdate()
    {
        if (!IsEnabled || _pendingGifts.IsEmpty) return;

        GameManager? gm = GameManager._instance;
        HeroController? hc = HeroController.instance;
        PlayerData? pd = PlayerData.instance;
        if (gm == null || hc == null || pd == null || !gm.IsGameplayScene() || gm.IsInSceneTransition)
            return;

        while (_pendingGifts.TryDequeue(out ApGift gift))
        {
            ApplyGift(gift, hc, pd);
            ConsumeGiftFromStorage(gift.Id);
        }
    }

    private void ApplyGift(ApGift gift, HeroController hc, PlayerData pd)
    {
        string sender = _session?.Players?.GetPlayerName(gift.SenderSlot) ?? $"Slot {gift.SenderSlot}";
        (GiftEffectKind kind, int mag) = GiftProtocol.ClassifyGift(gift);

        string effectDesc;
        switch (kind)
        {
            case GiftEffectKind.Heal:
                hc.AddHealth(mag);
                effectDesc = $"+{mag} Mask{(mag == 1 ? "" : "s")}";
                break;
            case GiftEffectKind.Soul:
                hc.AddMPCharge(mag);
                effectDesc = $"+{mag} Soul";
                break;
            case GiftEffectKind.RancidEgg:
                pd.SetInt(nameof(PlayerData.rancidEggs), pd.rancidEggs + mag);
                effectDesc = $"+{mag} Rancid Egg{(mag == 1 ? "" : "s")}";
                break;
            case GiftEffectKind.Trap:
                if (pd.health > 1)
                {
                    hc.TakeDamage(hc.gameObject, GlobalEnums.CollisionSide.other, 1, 1);
                    effectDesc = "Trap (-1 Mask)";
                }
                else
                {
                    hc.RecoilLeft();
                    effectDesc = "Trap (Knockback)";
                }
                break;
            default:
                hc.AddGeo(mag);
                effectDesc = $"+{mag} Geo";
                break;
        }

        string title = $"Gift from {sender}: {gift.ItemName} ({effectDesc})";
        ApManager.Instance?.PushToast(title, ToastKind.LocalItem);
        new MsgUIDef
        {
            name = new BoxedString(title),
            sprite = new EmptySprite(),
        }.SendMessage(MessageType.Corner, null);
        HkssPlugin.Log.LogInfo($"[HK Gifting] Claimed '{gift.ItemName}' from {sender} -> {effectDesc}.");
    }

    private void ConsumeGiftFromStorage(string giftId)
    {
        ArchipelagoSession? session = _session;
        if (session == null || !session.Socket.Connected || string.IsNullOrEmpty(giftId)) return;
        try
        {
            string boxKey = GiftProtocol.GiftBoxKey(_team, _slot);
            session.DataStorage[Scope.Global, boxKey] += Operation.Pop(giftId);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogDebug($"[HK Gifting] Failed to pop gift '{giftId}': {ex.Message}");
        }
    }
}
