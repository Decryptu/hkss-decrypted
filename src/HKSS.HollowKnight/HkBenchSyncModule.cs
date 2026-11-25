using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using HKSS.Core;
using HKSS.Unity;
using ItemChanger.Modules;
using Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HKSS.HollowKnight;

/// <summary>
/// Synchronizes discovered Hollow Knight benches across coop sessions and restarts
/// via Archipelago slot DataStorage, feeding directly into the unified Benchwarp panel.
/// </summary>
public sealed class HkBenchSyncModule : Module
{
    internal const string StorageKey = "HKSS:Benches:HK:v1";

    private readonly ConcurrentQueue<List<BenchEntry>> _incomingBatches = new();
    private ArchipelagoSession? _session;
    private DataStorageElement? _subscribedElement;
    private DataStorageHelper.DataStorageUpdatedHandler? _subscribedHandler;
    private bool _attached;

    public override void Initialize()
    {
        ApManager.OnConnected += AttachToSession;
        ApManager.OnDisconnected += DetachFromSession;
        Benchwarp.OnBenchDiscovered += OnLocalBenchDiscovered;
        ModHooks.HeroUpdateHook += OnHeroUpdate;

        if (ApManager.Instance?.Client is ApClient client && client.IsConnected)
        {
            AttachToSession(client);
        }
    }

    public override void Unload()
    {
        ApManager.OnConnected -= AttachToSession;
        ApManager.OnDisconnected -= DetachFromSession;
        Benchwarp.OnBenchDiscovered -= OnLocalBenchDiscovered;
        ModHooks.HeroUpdateHook -= OnHeroUpdate;
        DetachFromSession();
    }

    private void AttachToSession(ApClient client)
    {
        DetachFromSession();
        ArchipelagoSession? session = client.Session;
        if (session == null) return;

        try
        {
            _session = session;
            DataStorageElement element = session.DataStorage[Scope.Slot, StorageKey];
            DataStorageHelper.DataStorageUpdatedHandler handler = (_, newValue, _) =>
            {
                List<BenchEntry> parsed = ParseBenches(newValue);
                if (parsed.Count > 0)
                {
                    _incomingBatches.Enqueue(parsed);
                }
            };

            element.OnValueChanged += handler;
            _subscribedElement = element;
            _subscribedHandler = handler;
            _attached = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    JToken? raw = await element.GetAsync<JToken>();
                    List<BenchEntry> remote = ParseBenches(raw);
                    if (remote.Count > 0)
                    {
                        _incomingBatches.Enqueue(remote);
                    }
                    PushUnion(session, remote);
                }
                catch (Exception ex)
                {
                    HkssPlugin.Log.LogDebug($"[HK BenchSync] Initial fetch skipped: {ex.Message}");
                    PushUnion(session, null);
                }
            });
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK BenchSync] Failed to attach: {ex.Message}");
            DetachFromSession();
        }
    }

    private void DetachFromSession()
    {
        _attached = false;
        if (_subscribedElement != null && _subscribedHandler != null)
        {
            try
            {
                _subscribedElement.OnValueChanged -= _subscribedHandler;
            }
            catch
            {
                // Ignore cleanup errors on closed sessions.
            }
        }
        _subscribedElement = null;
        _subscribedHandler = null;
        _session = null;
        while (_incomingBatches.TryDequeue(out _)) { }
    }

    private void OnLocalBenchDiscovered(BenchEntry bench)
    {
        if (!_attached || _session == null) return;
        PushUnion(_session, null);
    }

    private void OnHeroUpdate()
    {
        Benchwarp? bw = Benchwarp.Instance;
        if (bw == null) return;

        int totalAdded = 0;
        while (_incomingBatches.TryDequeue(out List<BenchEntry> batch))
        {
            totalAdded += bw.MergeExternalBenches(batch);
        }

        if (totalAdded > 0)
        {
            ApManager.Instance?.PushToast(
                totalAdded == 1
                    ? "Synced 1 bench from Archipelago."
                    : $"Synced {totalAdded} benches from Archipelago.",
                ToastKind.Info);
        }
    }

    private static void PushUnion(ArchipelagoSession session, List<BenchEntry>? remote)
    {
        try
        {
            var merged = new Dictionary<string, BenchEntry>(StringComparer.Ordinal);
            if (remote != null)
            {
                foreach (BenchEntry b in remote)
                {
                    if (b != null && !string.IsNullOrWhiteSpace(b.SceneName) && !string.IsNullOrWhiteSpace(b.MarkerName))
                        merged[b.Key] = b;
                }
            }

            Benchwarp? bw = Benchwarp.Instance;
            if (bw != null)
            {
                foreach (BenchEntry b in bw.Store.Discovered)
                {
                    merged[b.Key] = b;
                }
            }

            if (merged.Count == 0) return;

            var list = merged.Values.ToList();
            JArray payload = JArray.FromObject(list);
            session.DataStorage[Scope.Slot, StorageKey] = payload;
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK BenchSync] Failed to push benches: {ex.Message}");
        }
    }

    private static List<BenchEntry> ParseBenches(JToken? token)
    {
        var result = new List<BenchEntry>();
        if (token == null || token.Type == JTokenType.Null) return result;
        try
        {
            if (token is JArray arr)
            {
                List<BenchEntry>? parsed = arr.ToObject<List<BenchEntry>>();
                if (parsed != null)
                {
                    result.AddRange(parsed.Where(b =>
                        b != null
                        && !string.IsNullOrWhiteSpace(b.SceneName)
                        && !string.IsNullOrWhiteSpace(b.MarkerName)));
                }
            }
            else if (token.Type == JTokenType.String)
            {
                List<BenchEntry>? parsed = JsonConvert.DeserializeObject<List<BenchEntry>>(token.Value<string>() ?? string.Empty);
                if (parsed != null)
                {
                    result.AddRange(parsed);
                }
            }
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogDebug($"[HK BenchSync] Ignored malformed bench payload: {ex.Message}");
        }
        return result;
    }
}
