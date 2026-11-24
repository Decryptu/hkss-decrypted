using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HKSS.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HKSS.Core.Tests;

/// <summary>
/// End-to-end loopback WebSocket test exercising <see cref="ApClient"/>, <see cref="ItemRouter"/>,
/// <see cref="IdSpace"/>, and <see cref="RunStateStore"/> across both games on one Archipelago slot.
/// </summary>
public sealed class ApClientIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hkss-ap-int-" + Guid.NewGuid().ToString("N"));

    public ApClientIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, RunContext.MarkerFile), "{\"current\":\"hollow_knight\"}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private RunContext MakeContext(GameId game) => RunContext.Create(game, name => name switch
    {
        RunContext.RunDirVariable => _dir,
        RunContext.SessionVariable => "int-session",
        _ => null,
    });

    [Fact]
    public async Task PickupInHallownestGrantsInPharloomAndReverseInOneSlot()
    {
        long hkLocId = IdSpace.ToCombined(GameId.HollowKnight, IdSpace.HollowKnightUpstreamBase + 42);
        long ssLocId = IdSpace.ToCombined(GameId.Silksong, IdSpace.SilksongUpstreamBase + 77);
        long hkItemId = IdSpace.ToCombined(GameId.HollowKnight, IdSpace.HollowKnightUpstreamBase + 10);
        long ssItemId = IdSpace.ToCombined(GameId.Silksong, IdSpace.SilksongUpstreamBase + 20);

        using var server = new FakeArchipelagoServer(
            hkLocId, "HK Fury_of_the_Fallen",
            ssLocId, "SS Mossberry: Bone Bottom",
            hkItemId, "HK Mothwing_Cloak",
            ssItemId, "SS Swift Step");
        server.Start();

        var settings = new ApSettings
        {
            Host = "ws://127.0.0.1",
            Port = server.Port,
            Slot = "ComboPlayer",
        };

        var stateStore = new RunStateStore(MakeContext(GameId.HollowKnight));

        // 1. Hollow Knight connects, sends HK Fury_of_the_Fallen, receives SS Swift Step -> cross-game notice.
        using (var hk = new ApClient(GameId.HollowKnight))
        {
            hk.Connect(settings);
            await WaitForEvent(hk, ApEventKind.Connected);

            long? resolvedHkLoc = hk.ResolveLocationId("HK Fury_of_the_Fallen");
            Assert.Equal(hkLocId, resolvedHkLoc);
            Assert.True(hk.CompleteLocation(resolvedHkLoc!.Value));

            ApEvent itemEvt = await WaitForEvent(hk, ApEventKind.ItemReceived);
            RoutedItem routed = ItemRouter.Route(
                GameId.HollowKnight,
                itemEvt.ItemIndex,
                stateStore.GetProcessedItems(GameId.HollowKnight),
                itemEvt.ItemId,
                itemEvt.ItemName,
                itemEvt.SenderName,
                itemEvt.IsLiveReceipt);

            Assert.Equal(RouteAction.CrossGameNotice, routed.Action);
            Assert.Equal("Swift Step sent to Pharloom", routed.Message);
            stateStore.SetProcessedItems(GameId.HollowKnight, itemEvt.ItemIndex + 1);
            hk.Disconnect();
        }

        // 2. Silksong connects to the SAME slot, catches up and applies SS Swift Step locally,
        //    then sends SS Mossberry: Bone Bottom and sees HK Mothwing_Cloak sent to Hallownest.
        using (var ss = new ApClient(GameId.Silksong))
        {
            ss.Connect(settings);
            List<ApEvent> events = await WaitForEvents(ss, e => e.Kind == ApEventKind.Connected);
            ApEvent? ssGrantEvt = events.Find(e => e.Kind == ApEventKind.ItemReceived);
            ssGrantEvt ??= await WaitForEvent(ss, ApEventKind.ItemReceived);

            RoutedItem routedGrant = ItemRouter.Route(
                GameId.Silksong,
                ssGrantEvt.ItemIndex,
                stateStore.GetProcessedItems(GameId.Silksong),
                ssGrantEvt.ItemId,
                ssGrantEvt.ItemName,
                ssGrantEvt.SenderName,
                ssGrantEvt.IsLiveReceipt);

            Assert.Equal(RouteAction.ApplyLocally, routedGrant.Action);
            Assert.Equal("Swift Step", routedGrant.DisplayName);
            stateStore.SetProcessedItems(GameId.Silksong, ssGrantEvt.ItemIndex + 1);

            long? resolvedSsLoc = ss.ResolveLocationId("SS Mossberry: Bone Bottom");
            Assert.Equal(ssLocId, resolvedSsLoc);
            Assert.True(ss.CompleteLocation(resolvedSsLoc!.Value));

            ApEvent hkItemEvt = await WaitForEvent(ss, ApEventKind.ItemReceived);
            RoutedItem routedCross = ItemRouter.Route(
                GameId.Silksong,
                hkItemEvt.ItemIndex,
                stateStore.GetProcessedItems(GameId.Silksong),
                hkItemEvt.ItemId,
                hkItemEvt.ItemName,
                hkItemEvt.SenderName,
                hkItemEvt.IsLiveReceipt);

            Assert.Equal(RouteAction.CrossGameNotice, routedCross.Action);
            Assert.Equal("Mothwing_Cloak sent to Hallownest", routedCross.Message);
            stateStore.SetProcessedItems(GameId.Silksong, hkItemEvt.ItemIndex + 1);
            ss.Disconnect();
        }

        // 3. Hollow Knight reconnects to the SAME slot, skips index 0 (already processed),
        //    and applies index 1 (HK Mothwing_Cloak) locally.
        using (var hk2 = new ApClient(GameId.HollowKnight))
        {
            hk2.Connect(settings);
            var itemEvents = new List<ApEvent>();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && itemEvents.Count < 2)
            {
                while (hk2.TryDequeueEvent(out ApEvent evt))
                {
                    if (evt.Kind == ApEventKind.ItemReceived) itemEvents.Add(evt);
                }
                if (itemEvents.Count < 2) await Task.Delay(20);
            }

            Assert.Equal(2, itemEvents.Count);
            int hkProcessed = stateStore.GetProcessedItems(GameId.HollowKnight);
            Assert.Equal(1, hkProcessed);

            RoutedItem first = ItemRouter.Route(
                GameId.HollowKnight, itemEvents[0].ItemIndex, hkProcessed,
                itemEvents[0].ItemId, itemEvents[0].ItemName, itemEvents[0].SenderName, itemEvents[0].IsLiveReceipt);
            Assert.Equal(RouteAction.SkipAlreadyProcessed, first.Action);

            RoutedItem second = ItemRouter.Route(
                GameId.HollowKnight, itemEvents[1].ItemIndex, hkProcessed,
                itemEvents[1].ItemId, itemEvents[1].ItemName, itemEvents[1].SenderName, itemEvents[1].IsLiveReceipt);
            Assert.Equal(RouteAction.ApplyLocally, second.Action);
            Assert.Equal("Mothwing_Cloak", second.DisplayName);
            hk2.Disconnect();
        }
    }

    private static async Task<ApEvent> WaitForEvent(ApClient client, ApEventKind kind)
    {
        List<ApEvent> seen = await WaitForEvents(client, e => e.Kind == kind);
        return seen.FindLast(e => e.Kind == kind)!;
    }

    private static async Task<List<ApEvent>> WaitForEvents(ApClient client, Predicate<ApEvent> stopWhen)
    {
        var list = new List<ApEvent>();
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            while (client.TryDequeueEvent(out ApEvent evt))
            {
                list.Add(evt);
                if (evt.Kind == ApEventKind.ConnectionFailed)
                    throw new InvalidOperationException($"Connection failed: {evt.Message}");
                if (stopWhen(evt)) return list;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Timed out waiting for Archipelago client event.");
    }

    /// <summary>Minimal RFC 6455 WebSocket server speaking the Archipelago JSON packet protocol.</summary>
    private sealed class FakeArchipelagoServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly long _hkLocId, _ssLocId, _hkItemId, _ssItemId;
        private readonly string _hkLocName, _ssLocName, _hkItemName, _ssItemName;
        private readonly List<object> _receivedItems = new();
        private readonly HashSet<long> _checkedLocations = new();

        public int Port { get; private set; }

        public FakeArchipelagoServer(
            long hkLocId, string hkLocName,
            long ssLocId, string ssLocName,
            long hkItemId, string hkItemName,
            long ssItemId, string ssItemName)
        {
            _hkLocId = hkLocId; _hkLocName = hkLocName;
            _ssLocId = ssLocId; _ssLocName = ssLocName;
            _hkItemId = hkItemId; _hkItemName = hkItemName;
            _ssItemId = ssItemId; _ssItemName = ssItemName;
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => HandleClient(client));
                }
                catch
                {
                    break;
                }
            }
        }

        private async Task HandleClient(TcpClient tcp)
        {
            using (tcp)
            await using (NetworkStream stream = tcp.GetStream())
            {
                if (!await PerformWebSocketHandshake(stream)) return;
                using WebSocket ws = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, TimeSpan.FromSeconds(30));

                await SendPackets(ws, new object[]
                {
                    new
                    {
                        cmd = "RoomInfo",
                        version = new { major = 0, minor = 6, build = 8, @class = "Version" },
                        generator_version = new { major = 0, minor = 6, build = 8, @class = "Version" },
                        tags = new[] { "AP" },
                        password = false,
                        permissions = new { release = 0, collect = 0, remaining = 0 },
                        hint_cost = 10,
                        location_check_points = 1,
                        games = new[] { IdSpace.GameName },
                        datapackage_checksums = new Dictionary<string, string> { [IdSpace.GameName] = "test-checksum-v1" },
                        seed_name = "test-seed",
                        time = 0.0,
                    },
                });

                byte[] buf = new byte[65536];
                while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var ms = new MemoryStream();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(buf, _cts.Token);
                        if (res.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    string text = Encoding.UTF8.GetString(ms.ToArray());
                    JArray packets = JArray.Parse(text);
                    foreach (JObject pkt in packets)
                    {
                        string cmd = pkt.Value<string>("cmd") ?? string.Empty;
                        if (cmd == "GetDataPackage")
                        {
                            await SendPackets(ws, new object[]
                            {
                                new
                                {
                                    cmd = "DataPackage",
                                    data = new
                                    {
                                        games = new Dictionary<string, object>
                                        {
                                            [IdSpace.GameName] = new
                                            {
                                                checksum = "test-checksum-v1",
                                                item_name_to_id = new Dictionary<string, long>
                                                {
                                                    [_hkItemName] = _hkItemId,
                                                    [_ssItemName] = _ssItemId,
                                                },
                                                location_name_to_id = new Dictionary<string, long>
                                                {
                                                    [_hkLocName] = _hkLocId,
                                                    [_ssLocName] = _ssLocId,
                                                },
                                            },
                                        },
                                    },
                                },
                            });
                        }
                        else if (cmd == "Connect")
                        {
                            List<object> snapshot;
                            long[] checkedSnap;
                            lock (_receivedItems)
                            {
                                snapshot = new List<object>(_receivedItems);
                                checkedSnap = new List<long>(_checkedLocations).ToArray();
                            }

                            var missing = new List<long>();
                            if (!_checkedLocations.Contains(_hkLocId)) missing.Add(_hkLocId);
                            if (!_checkedLocations.Contains(_ssLocId)) missing.Add(_ssLocId);

                            await SendPackets(ws, new object[]
                            {
                                new
                                {
                                    cmd = "Connected",
                                    team = 0,
                                    slot = 1,
                                    players = new[]
                                    {
                                        new { team = 0, slot = 1, alias = "ComboPlayer", name = "ComboPlayer" },
                                    },
                                    missing_locations = missing.ToArray(),
                                    checked_locations = checkedSnap,
                                    slot_data = new Dictionary<string, object>
                                    {
                                        ["games"] = "both",
                                        ["combo_goal"] = "both",
                                        ["death_link"] = false,
                                    },
                                    slot_info = new Dictionary<string, object>
                                    {
                                        ["1"] = new { name = "ComboPlayer", game = IdSpace.GameName, type = 1, group_members = Array.Empty<int>() },
                                    },
                                    hint_points = 0,
                                },
                                new
                                {
                                    cmd = "ReceivedItems",
                                    index = 0,
                                    items = snapshot.ToArray(),
                                },
                            });
                        }
                        else if (cmd == "LocationChecks")
                        {
                            JArray? locs = pkt["locations"] as JArray;
                            if (locs == null) continue;
                            foreach (JToken tok in locs)
                            {
                                long locId = tok.Value<long>();
                                object? granted = null;
                                int startIndex = 0;
                                lock (_receivedItems)
                                {
                                    if (!_checkedLocations.Add(locId)) continue;
                                    startIndex = _receivedItems.Count;
                                    if (locId == _hkLocId)
                                        granted = new { item = _ssItemId, location = _hkLocId, player = 1, flags = 1 };
                                    else if (locId == _ssLocId)
                                        granted = new { item = _hkItemId, location = _ssLocId, player = 1, flags = 1 };
                                    if (granted != null) _receivedItems.Add(granted);
                                }
                                if (granted != null)
                                {
                                    await SendPackets(ws, new object[]
                                    {
                                        new { cmd = "ReceivedItems", index = startIndex, items = new[] { granted } },
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        private static async Task SendPackets(WebSocket ws, object[] packets)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packets));
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }

        private static async Task<bool> PerformWebSocketHandshake(NetworkStream stream)
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            string? key = null;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
            }
            if (string.IsNullOrEmpty(key)) return false;

            string accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            byte[] respBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(respBytes, 0, respBytes.Length);
            await stream.FlushAsync();
            return true;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
