using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using BepInEx.Configuration;
using GlobalEnums;
using HKSS.Core;
using HKSS.Unity;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Hollow Knight SoulLink, GeoLink, and KnockbackLink matching Silksong's
/// SilkLink, CurrencyLink, and KnockbackLink protocols.
/// </summary>
internal sealed class HkLinksManager : MonoBehaviour
{
    private const string SoulStorageKey = "HKSS:SoulLink:v1:BaseSoul";
    private const string GeoStorageKey = "HKSS:GeoLink:v1:Geo";
    private const double KnockbackLifetimeSeconds = 3.0;
    private const double KnockbackCooldownSeconds = 1.0;
    private const float KnockbackOutgoingStrength = 40f;

    internal static HkLinksManager? Instance { get; private set; }

    private static ConfigEntry<bool>? _soulLinkCfg;
    private static ConfigEntry<bool>? _geoLinkCfg;
    private static ConfigEntry<bool>? _knockbackLinkCfg;

    // A seed that sets a link decides it; older seeds fall back to the local setting.
    private static HkSlotOptions? SeedOptions => HkClientHost.CurrentSlotData?.Options;

    internal static bool SoulLinkSetBySeed => SeedOptions?.SoulLink.HasValue ?? false;
    internal static bool GeoLinkSetBySeed => SeedOptions?.GeoLink.HasValue ?? false;
    internal static bool KnockbackLinkSetBySeed => SeedOptions?.KnockbackLink.HasValue ?? false;

    internal static bool SoulLinkEnabled
    {
        get => SeedOptions?.SoulLink ?? _soulLinkCfg?.Value ?? false;
        set
        {
            if (SoulLinkSetBySeed) return;
            if (_soulLinkCfg != null) _soulLinkCfg.Value = value;
            Instance?.Reconfigure();
        }
    }

    internal static bool GeoLinkEnabled
    {
        get => SeedOptions?.GeoLink ?? _geoLinkCfg?.Value ?? false;
        set
        {
            if (GeoLinkSetBySeed) return;
            if (_geoLinkCfg != null) _geoLinkCfg.Value = value;
            Instance?.Reconfigure();
        }
    }

    internal static bool KnockbackLinkEnabled
    {
        get => SeedOptions?.KnockbackLink ?? _knockbackLinkCfg?.Value ?? false;
        set
        {
            if (KnockbackLinkSetBySeed) return;
            if (_knockbackLinkCfg != null) _knockbackLinkCfg.Value = value;
            Instance?.Reconfigure();
        }
    }

    private sealed class PoolState
    {
        public readonly string Key;
        public readonly int MaxValue;
        public bool Active;
        public bool Ready;
        public int Authoritative;
        public int PendingDelta;
        public int LastObserved = -1;
        public DataStorageElement? Element;
        public DataStorageHelper.DataStorageUpdatedHandler? Handler;
        public readonly Queue<(int Gen, int Value, bool IsAck, int Delta)> Updates = new();

        public PoolState(string key, int maxValue)
        {
            Key = key;
            MaxValue = maxValue;
        }
    }

    private readonly object _sync = new();
    private readonly PoolState _soul = new(SoulStorageKey, LinkMath.HkBaseSoulCapacity);
    private readonly PoolState _geo = new(GeoStorageKey, LinkMath.HkMaxSharedGeo);
    private readonly Dictionary<string, double> _seenKnockbacks = new(StringComparer.Ordinal);

    private ArchipelagoSession? _session;
    private ArchipelagoSocketHelperDelagates.PacketReceivedHandler? _packetHandler;
    private PlayerData? _boundPlayerData;
    private string _sourceAlias = string.Empty;
    private string _uuid = Guid.NewGuid().ToString();
    private int _generation;
    private bool _knockbackActive;
    private bool _applyingRemote;
    private Vector2? _pendingKnockback;
    private double _knockbackExpires;
    private double _lastKbSent = double.NegativeInfinity;
    private double _lastKbApplied = double.NegativeInfinity;

    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    private static double UnixNow => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

    private void Awake()
    {
        Instance = this;
        ConfigFile? cfg = HkssPlugin.PluginConfig;
        if (cfg != null)
        {
            _soulLinkCfg ??= cfg.Bind("Links", "SoulLink", false, "Share the base 99 Soul orb with teammates using SoulLink.");
            _geoLinkCfg ??= cfg.Bind("Links", "GeoLink", false, "Share Geo with teammates using GeoLink.");
            _knockbackLinkCfg ??= cfg.Bind("Links", "KnockbackLink", false, "Share damage knockback across Hollow Knight and Silksong via KnockbackLink.");
        }

        ApManager.OnConnected += OnApConnected;
        ApManager.OnDisconnected += OnApDisconnected;
        On.HeroController.TakeDamage += OnHeroTakeDamage;

        if (ApManager.Instance?.Client is ApClient client && client.IsConnected)
        {
            OnApConnected(client);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ApManager.OnConnected -= OnApConnected;
        ApManager.OnDisconnected -= OnApDisconnected;
        On.HeroController.TakeDamage -= OnHeroTakeDamage;
        ResetAll(disconnecting: true);
    }

    private void OnApConnected(ApClient client) => Reconfigure();
    private void OnApDisconnected() => ResetAll(disconnecting: true);

    internal void Reconfigure()
    {
        ResetAll(disconnecting: false);

        ApClient? client = ApManager.Instance?.Client;
        ArchipelagoSession? session = client?.Session;
        if (client == null || session == null || !client.IsConnected)
            return;

        bool wantSoul = SoulLinkEnabled;
        bool wantGeo = GeoLinkEnabled;
        bool wantKb = KnockbackLinkEnabled;
        if (!wantSoul && !wantGeo && !wantKb) return;

        lock (_sync)
        {
            _session = session;
            _sourceAlias = session.Players?.ActivePlayer?.Alias ?? client.ConnectedSlot;
            _uuid = Guid.NewGuid().ToString();
            int gen = _generation;

            if (wantSoul) _soul.Active = true;
            if (wantGeo) _geo.Active = true;
            if (wantKb)
            {
                _knockbackActive = true;
                _packetHandler = packet => OnPacketReceived(packet, gen);
                session.Socket.PacketReceived += _packetHandler;
                try
                {
                    string[] tags = session.ConnectionInfo.Tags
                        .Concat(new[] { LinkMath.KnockbackLinkTag })
                        .Distinct()
                        .ToArray();
                    session.ConnectionInfo.UpdateConnectionOptions(tags);
                }
                catch (Exception ex)
                {
                    HkssPlugin.Log.LogWarning($"[HK Links] Failed to add KnockbackLink tag: {ex.Message}");
                }
            }
        }

        HkssPlugin.Log.LogInfo($"[HK Links] Configured (Soul={wantSoul}, Geo={wantGeo}, Knockback={wantKb}).");
    }

    private void ResetAll(bool disconnecting)
    {
        ArchipelagoSession? oldSession;
        ArchipelagoSocketHelperDelagates.PacketReceivedHandler? oldHandler;
        bool hadKb;

        lock (_sync)
        {
            _generation++;
            oldSession = _session;
            oldHandler = _packetHandler;
            hadKb = _knockbackActive;

            DetachPoolLocked(_soul);
            DetachPoolLocked(_geo);

            _session = null;
            _packetHandler = null;
            _knockbackActive = false;
            _pendingKnockback = null;
            _seenKnockbacks.Clear();
            _boundPlayerData = null;
            _lastKbSent = double.NegativeInfinity;
            _lastKbApplied = double.NegativeInfinity;
        }

        if (oldSession != null && oldHandler != null)
        {
            try { oldSession.Socket.PacketReceived -= oldHandler; } catch { }
        }

        if (!disconnecting && hadKb && oldSession != null && oldSession.Socket.Connected)
        {
            try
            {
                string[] tags = oldSession.ConnectionInfo.Tags
                    .Where(t => !string.Equals(t, LinkMath.KnockbackLinkTag, StringComparison.Ordinal))
                    .ToArray();
                oldSession.ConnectionInfo.UpdateConnectionOptions(tags);
            }
            catch { }
        }
    }

    private static void DetachPoolLocked(PoolState pool)
    {
        if (pool.Element != null && pool.Handler != null)
        {
            try { pool.Element.OnValueChanged -= pool.Handler; } catch { }
        }
        pool.Element = null;
        pool.Handler = null;
        pool.Active = false;
        pool.Ready = false;
        pool.Authoritative = 0;
        pool.PendingDelta = 0;
        pool.LastObserved = -1;
        pool.Updates.Clear();
    }

    private void Update()
    {
        if (_session == null) return;

        GameManager? gm = GameManager._instance;
        HeroController? hc = HeroController.instance;
        PlayerData? pd = PlayerData.instance;
        if (gm == null || hc == null || pd == null
            || gm.gameState != GameState.PLAYING
            || !gm.IsGameplayScene()
            || gm.IsInSceneTransition)
        {
            return;
        }

        if (!ReferenceEquals(_boundPlayerData, pd))
        {
            _boundPlayerData = pd;
            _soul.LastObserved = pd.MPCharge;
            _geo.LastObserved = pd.geo;
        }

        if (_soul.Active && !hc.cState.dead)
            UpdatePool(_soul, pd.MPCharge, LocalSoulCap(pd), target => ApplySoul(hc, pd, target), () => pd.MPCharge);

        if (_geo.Active && !hc.cState.dead)
            UpdatePool(_geo, pd.geo, _geo.MaxValue, target => ApplyGeo(hc, pd, target), () => pd.geo);

        if (_knockbackActive)
            UpdateKnockback(gm, hc, pd);
    }

    // The shared pool holds up to 99 Soul, but a cracked vessel after death holds less.
    private static int LocalSoulCap(PlayerData pd) =>
        LinkMath.Clamp(pd.maxMP, 0, LinkMath.HkBaseSoulCapacity);

    private void UpdatePool(PoolState pool, int currentLocal, int localCap, Action<int> applyToGame, Func<int> readLocal)
    {
        ArchipelagoSession? session;
        int gen;
        lock (_sync)
        {
            session = _session;
            gen = _generation;
            if (session == null || !pool.Active) return;

            if (pool.Element == null)
            {
                StartPoolLocked(session, pool, gen, currentLocal);
                return;
            }

            while (pool.Updates.Count > 0)
            {
                var u = pool.Updates.Dequeue();
                if (u.Gen != gen) continue;
                if (u.IsAck) pool.PendingDelta -= u.Delta;
                pool.Authoritative = LinkMath.Clamp(u.Value, 0, pool.MaxValue);
                pool.Ready = true;
            }
        }

        if (!pool.Ready) return;

        int localDelta = pool.LastObserved >= 0 ? currentLocal - pool.LastObserved : 0;
        if (localDelta != 0)
        {
            pool.LastObserved = currentLocal;
            SendPoolDelta(session, pool, gen, localDelta);
        }

        int target = LinkMath.ComputeLocalTarget(pool.Authoritative, pool.PendingDelta, pool.MaxValue, localCap);
        if (target != currentLocal)
        {
            _applyingRemote = true;
            try
            {
                applyToGame(target);
                // Record what the game actually holds so a capped apply is never sent back as a loss.
                pool.LastObserved = readLocal();
            }
            finally
            {
                _applyingRemote = false;
            }
        }
    }

    private void StartPoolLocked(ArchipelagoSession session, PoolState pool, int gen, int initialLocal)
    {
        try
        {
            DataStorageElement el = session.DataStorage[Scope.Team, pool.Key];
            DataStorageHelper.DataStorageUpdatedHandler handler = (_, newVal, _) =>
            {
                EnqueuePoolUpdate(pool, gen, newVal, isAck: false, delta: 0);
            };
            el.OnValueChanged += handler;
            el.Initialize(LinkMath.Clamp(initialLocal, 0, pool.MaxValue));
            pool.Element = el;
            pool.Handler = handler;
            pool.LastObserved = initialLocal;
            SendPoolDelta(session, pool, gen, 0);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK Links] Failed to start pool {pool.Key}: {ex.Message}");
            pool.Active = false;
        }
    }

    private void SendPoolDelta(ArchipelagoSession session, PoolState pool, int gen, int delta)
    {
        DataStorageHelper.DataStorageUpdatedHandler ack = (_, newVal, _) =>
        {
            EnqueuePoolUpdate(pool, gen, newVal, isAck: true, delta: delta);
        };

        pool.PendingDelta += delta;
        try
        {
            session.DataStorage[Scope.Team, pool.Key] =
                session.DataStorage[Scope.Team, pool.Key]
                + delta
                + Operation.Max(0)
                + Operation.Min(pool.MaxValue)
                + Callback.Add(ack);
        }
        catch (Exception ex)
        {
            pool.PendingDelta -= delta;
            HkssPlugin.Log.LogWarning($"[HK Links] Pool update failed ({pool.Key}): {ex.Message}");
        }
    }

    private void EnqueuePoolUpdate(PoolState pool, int gen, JToken? val, bool isAck, int delta)
    {
        int parsed = 0;
        try
        {
            if (val != null && val.Type != JTokenType.Null)
                parsed = val.ToObject<int>();
        }
        catch
        {
            return;
        }

        lock (_sync)
        {
            if (gen != _generation || !pool.Active) return;
            pool.Updates.Enqueue((gen, parsed, isAck, delta));
        }
    }

    private static void ApplySoul(HeroController hc, PlayerData pd, int targetSoul)
    {
        int diff = targetSoul - pd.MPCharge;
        if (diff > 0)
        {
            hc.AddMPCharge(diff);
        }
        else if (diff < 0)
        {
            hc.TakeMPQuick(-diff);
        }
    }

    private static void ApplyGeo(HeroController hc, PlayerData pd, int targetGeo)
    {
        int diff = targetGeo - pd.geo;
        if (diff > 0)
        {
            hc.AddGeo(diff);
        }
        else if (diff < 0)
        {
            hc.TakeGeo(-diff);
        }
    }

    private void OnPacketReceived(ArchipelagoPacketBase packet, int gen)
    {
        if (packet is not BouncedPacket bounced) return;
        lock (_sync)
        {
            if (!_knockbackActive || _session == null || gen != _generation) return;
            if (!TryReadKnockbackPacket(
                bounced,
                out float dx,
                out float dy,
                out string sender,
                out string? senderUuid,
                out string dedupKey))
            {
                return;
            }

            if (senderUuid == _uuid || (string.IsNullOrEmpty(senderUuid) && sender == _sourceAlias))
                return;

            double now = Now;
            foreach (string old in _seenKnockbacks.Where(kv => now - kv.Value > 30.0).Select(kv => kv.Key).ToArray())
                _seenKnockbacks.Remove(old);
            if (_seenKnockbacks.ContainsKey(dedupKey) || _seenKnockbacks.Count >= 256)
                return;

            _seenKnockbacks[dedupKey] = now;
            _pendingKnockback = new Vector2(dx, dy);
            _knockbackExpires = now + KnockbackLifetimeSeconds;
        }
    }

    private static bool TryReadKnockbackPacket(
        BouncedPacket packet,
        out float dirX,
        out float dirY,
        out string sender,
        out string? senderUuid,
        out string dedupKey)
    {
        dirX = 0f;
        dirY = 0f;
        sender = string.Empty;
        senderUuid = null;
        dedupKey = string.Empty;

        if (packet.Tags == null || !packet.Tags.Contains(LinkMath.KnockbackLinkTag) || packet.Data == null)
            return false;
        if (!packet.Data.TryGetValue("source", out JToken? sourceTok) || sourceTok.Type != JTokenType.String)
            return false;
        if (!packet.Data.TryGetValue("time", out JToken? timeTok)
            || (timeTok.Type != JTokenType.Float && timeTok.Type != JTokenType.Integer))
            return false;
        double time = timeTok.Value<double>();
        if (double.IsNaN(time) || double.IsInfinity(time))
            return false;
        if (!packet.Data.TryGetValue("value", out JToken? valTok) || valTok is not JObject vec)
            return false;
        JToken? xTok = vec["x"];
        JToken? yTok = vec["y"];
        if (xTok == null || yTok == null
            || (xTok.Type != JTokenType.Float && xTok.Type != JTokenType.Integer)
            || (yTok.Type != JTokenType.Float && yTok.Type != JTokenType.Integer))
            return false;
        if (!LinkMath.TryNormalizeDirection(xTok.Value<double>(), yTok.Value<double>(), out dirX, out dirY))
            return false;

        sender = sourceTok.Value<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sender) || sender.Length > 256)
            return false;

        if (packet.Data.TryGetValue("uuid", out JToken? uuidTok))
        {
            if (uuidTok.Type != JTokenType.String) return false;
            senderUuid = uuidTok.Value<string>();
            if (senderUuid != null && senderUuid.Length > 256) return false;
        }

        dedupKey = LinkMath.FormatKnockbackKey(sender, senderUuid, time);
        return true;
    }

    private void UpdateKnockback(GameManager gm, HeroController hc, PlayerData pd)
    {
        Vector2? dir = null;
        double now = Now;
        lock (_sync)
        {
            if (!_pendingKnockback.HasValue) return;
            if (now > _knockbackExpires)
            {
                _pendingKnockback = null;
                return;
            }
            if (now - _lastKbApplied < KnockbackCooldownSeconds) return;
            if (!CanApplyKnockback(gm, hc, pd)) return;

            dir = _pendingKnockback;
            _pendingKnockback = null;
            _lastKbApplied = now;
        }

        if (!dir.HasValue) return;

        _applyingRemote = true;
        try
        {
            if (dir.Value.x >= 0f)
                hc.RecoilRight();
            else
                hc.RecoilLeft();
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogDebug($"[HK Links] Knockback apply failed: {ex.Message}");
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    private static bool CanApplyKnockback(GameManager gm, HeroController hc, PlayerData pd) =>
        !gm.isPaused
        && hc.acceptingInput
        && !pd.atBench
        && !hc.cState.dead
        && !hc.cState.hazardDeath
        && !hc.cState.hazardRespawning
        && !hc.cState.transitioning;

    private void OnHeroTakeDamage(
        On.HeroController.orig_TakeDamage orig,
        HeroController self,
        GameObject go,
        CollisionSide damageSide,
        int damageAmount,
        int hazardType)
    {
        orig(self, go, damageSide, damageAmount, hazardType);

        if (_applyingRemote || damageAmount <= 0 || damageAmount >= 9999) return;
        SendKnockback(self, damageSide);
    }

    private void SendKnockback(HeroController hero, CollisionSide side)
    {
        ArchipelagoSession? session;
        BouncePacket? packet;

        lock (_sync)
        {
            if (!_knockbackActive || _session == null || !_session.Socket.Connected) return;
            double now = Now;
            if (now - _lastKbSent < KnockbackCooldownSeconds) return;

            float x = side switch
            {
                CollisionSide.left => 1f,
                CollisionSide.right => -1f,
                _ => hero.cState.facingRight ? -1f : 1f,
            };
            float y = side switch
            {
                CollisionSide.top => -1f,
                CollisionSide.bottom => 1f,
                _ => 0.25f,
            };

            if (!LinkMath.TryNormalizeDirection(x, y, out float nx, out float ny))
                return;

            _lastKbSent = now;
            session = _session;
            packet = new BouncePacket
            {
                Tags = new List<string> { LinkMath.KnockbackLinkTag },
                Data = new Dictionary<string, JToken>
                {
                    ["source"] = _sourceAlias,
                    ["uuid"] = _uuid,
                    ["cause"] = $"{_sourceAlias} was knocked back.",
                    ["time"] = UnixNow,
                    ["value"] = new JObject
                    {
                        ["x"] = nx * KnockbackOutgoingStrength,
                        ["y"] = ny * KnockbackOutgoingStrength,
                        ["z"] = 0f,
                    },
                },
            };
        }

        SendPacketAsync(session, packet);
    }

    private static async void SendPacketAsync(ArchipelagoSession session, BouncePacket packet)
    {
        try
        {
            await session.Socket.SendPacketAsync(packet);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogDebug($"[HK Links] Knockback send failed: {ex.Message}");
        }
    }
}
