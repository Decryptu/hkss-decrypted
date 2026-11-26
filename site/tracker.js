// Live tracking for the Item Map: an Archipelago room (read-only tracker connection) or a spoiler log.

const DEFAULT_PORT = 38281;
const RETRY_DELAYS = [2, 5, 10, 20, 30];

function isLocalHost(host) {
  return /^(localhost|127\.\d+\.\d+\.\d+|\[::1\])$/i.test(host);
}

/** WebSocket URLs to try for what a player typed, such as `archipelago.gg:38281` or `/connect localhost`. */
export function roomUrls(address, securePage) {
  const text = address.trim().replace(/^\/connect\s+/i, "").replace(/\/+$/, "");
  if (!text) throw new Error("Enter the room address, such as archipelago.gg:38281.");
  if (/^wss?:\/\//i.test(text)) return [new URL(text).href];
  if (/^[a-z][a-z0-9+.-]*:\/\//i.test(text)) {
    throw new Error("Enter the room address shown on the room page, such as archipelago.gg:38281.");
  }
  const url = new URL(`ws://${text}`);
  if (!url.hostname || url.pathname !== "/" || url.search || url.hash) {
    throw new Error("Enter the room address shown on the room page, such as archipelago.gg:38281.");
  }
  const host = `${url.hostname}:${url.port || DEFAULT_PORT}`;
  // An https page may only open secure sockets, except to this computer.
  if (securePage && !isLocalHost(url.hostname)) return [`wss://${host}/`];
  return [`wss://${host}/`, `ws://${host}/`];
}

function version(text) {
  const [major = 0, minor = 0, build = 0] = String(text).split(".").map(Number);
  return { major, minor, build, class: "Version" };
}

function newestVersion(a, b) {
  for (const key of ["major", "minor", "build"]) {
    if ((a[key] ?? 0) !== (b[key] ?? 0)) return (a[key] ?? 0) > (b[key] ?? 0) ? a : b;
  }
  return a;
}

function uuid() {
  if (globalThis.crypto?.randomUUID) return crypto.randomUUID();
  return Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16)).join("");
}

const refusals = {
  InvalidSlot: "No player with that slot name is in this room. Slot names are case-sensitive.",
  InvalidGame: "That slot is not a Hollow Knight x Silksong slot.",
  IncompatibleVersion: "This room runs an Archipelago version the map cannot follow.",
  InvalidPassword: "The room password is wrong.",
  InvalidItemsHandling: "The room refused the tracker connection.",
};

/**
 * A read-only connection to one slot. It never sends checks or items; it only listens.
 * `onChange(state)` gets { seedName, slotName, locations, found, items, recent } after each update,
 * `onStatus(kind, message)` reports "connecting", "connected", "retrying", "error" or "closed".
 */
export class RoomTracker {
  constructor(world, { onChange, onStatus }) {
    this.world = world;
    this.onChange = onChange;
    this.onStatus = onStatus;
    this.socket = null;
    this.stopped = true;
  }

  connect(address, slot, password, securePage) {
    this.disconnect(false);
    this.urls = roomUrls(address, securePage);
    this.slot = slot.trim();
    if (!this.slot) throw new Error("Enter your slot name.");
    this.password = password;
    this.stopped = false;
    this.attempt = 0;
    this.everConnected = false;
    this.reset();
    this.open(0);
  }

  reset() {
    this.found = new Set();
    this.locations = new Set();
    this.items = new Map();
    this.names = { items: new Map(), players: new Map() };
    this.ownSlot = 0;
    this.seedName = "";
    this.serverVersion = version("0.0.0");
  }

  disconnect(report = true) {
    this.stopped = true;
    clearTimeout(this.retryTimer);
    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = socket.onerror = socket.onmessage = socket.onopen = null;
      socket.close();
    }
    if (report) this.onStatus("closed", "Disconnected from the room.");
  }

  open(index) {
    const url = this.urls[index];
    this.onStatus("connecting", `Connecting to ${new URL(url).host}…`);
    let opened = false;
    let socket;
    try {
      socket = new WebSocket(url);
    } catch {
      this.failed(index, false);
      return;
    }
    this.socket = socket;
    socket.onopen = () => { opened = true; };
    socket.onmessage = event => {
      let packets;
      try {
        packets = JSON.parse(event.data);
      } catch {
        return;
      }
      if (!Array.isArray(packets)) return;
      for (const packet of packets) {
        if (this.socket !== socket) return;
        this.receive(packet);
      }
    };
    socket.onerror = () => {};
    socket.onclose = () => {
      if (this.socket !== socket) return;
      this.socket = null;
      this.failed(index, opened);
    };
  }

  failed(index, opened) {
    if (this.stopped) return;
    // A secure socket that never opened may just mean a plain-socket room: try the next form.
    if (!opened && !this.everConnected && index + 1 < this.urls.length) {
      this.open(index + 1);
      return;
    }
    if (!this.everConnected) {
      this.stopped = true;
      this.onStatus("error", opened
        ? "The room closed the connection."
        : "Could not reach the room. Check the address and port, and that the room is still running.");
      return;
    }
    const delay = RETRY_DELAYS[Math.min(this.attempt++, RETRY_DELAYS.length - 1)];
    this.onStatus("retrying", `Connection lost. Reconnecting in ${delay} s…`);
    this.retryTimer = setTimeout(() => {
      if (!this.stopped) this.open(Math.max(0, this.urls.indexOf(this.liveUrl)));
    }, delay * 1000);
  }

  send(packet) {
    if (this.socket?.readyState === WebSocket.OPEN) this.socket.send(JSON.stringify([packet]));
  }

  receive(packet) {
    switch (packet?.cmd) {
      case "RoomInfo": return this.roomInfo(packet);
      case "Connected": return this.connected(packet);
      case "ConnectionRefused": return this.refused(packet);
      case "DataPackage": return this.dataPackage(packet);
      case "LocationInfo": return this.locationInfo(packet);
      case "RoomUpdate": return this.roomUpdate(packet);
      default: return undefined;
    }
  }

  roomInfo(packet) {
    this.seedName = String(packet.seed_name ?? "");
    this.serverVersion = packet.version ?? this.serverVersion;
    if (packet.password && !this.password) {
      this.stop("This room needs a password.");
      return;
    }
    this.send({
      cmd: "Connect",
      game: this.world.game,
      name: this.slot,
      password: this.password || null,
      uuid: uuid(),
      // The server's own version always satisfies the world's minimum client version.
      version: newestVersion(version(this.world.minimumArchipelago), this.serverVersion),
      items_handling: 0,
      tags: ["Tracker"],
      slot_data: false,
    });
  }

  stop(message) {
    this.disconnect(false);
    this.onStatus("error", message);
  }

  connected(packet) {
    const own = packet.slot_info?.[packet.slot];
    if (own?.game && own.game !== this.world.game) {
      this.stop(`That slot plays ${own.game}, not ${this.world.game}.`);
      return;
    }
    this.ownSlot = packet.slot;
    this.liveUrl = this.socket.url;
    this.everConnected = true;
    this.attempt = 0;
    const team = packet.team;
    this.names.players = new Map((packet.players ?? [])
      .filter(player => player.team === team)
      .map(player => [player.slot, player.alias || player.name]));
    const games = new Set(Object.values(packet.slot_info ?? {}).map(info => info.game).filter(Boolean));
    this.slotGames = new Map(Object.entries(packet.slot_info ?? {}).map(([slot, info]) => [Number(slot), info.game]));
    const checked = packet.checked_locations ?? [];
    this.locations = new Set([...(packet.missing_locations ?? []), ...checked]);
    this.found = new Set(checked);
    this.onStatus("connected", `Following ${this.names.players.get(this.ownSlot) ?? this.slot} live.`);
    this.send({ cmd: "GetDataPackage", games: [...games] });
    this.changed([]);
  }

  refused(packet) {
    const reasons = (packet.errors ?? []).map(error => refusals[error] ?? `The room refused the connection (${error}).`);
    this.stop(reasons.join(" ") || "The room refused the connection.");
  }

  dataPackage(packet) {
    for (const [game, data] of Object.entries(packet.data?.games ?? {})) {
      const names = new Map(Object.entries(data.item_name_to_id ?? {}).map(([name, id]) => [id, name]));
      this.names.items.set(game, names);
    }
    // Ask what was found only once item names are known.
    this.scout([...this.found]);
  }

  scout(ids) {
    const unknown = ids.filter(id => !this.items.has(id));
    if (unknown.length && this.names.items.size) {
      this.send({ cmd: "LocationScouts", locations: unknown, create_as_hint: 0 });
    }
  }

  locationInfo(packet) {
    for (const item of packet.locations ?? []) {
      const game = this.slotGames?.get(item.player);
      const name = this.names.items.get(game)?.get(item.item) ?? `Item ${item.item}`;
      const owner = item.player === this.ownSlot ? "" : this.names.players.get(item.player) ?? `Player ${item.player}`;
      this.items.set(item.location, owner ? `${name} (${owner})` : name);
    }
    this.changed([]);
  }

  roomUpdate(packet) {
    const fresh = (packet.checked_locations ?? []).filter(id => !this.found.has(id));
    if (!fresh.length) return;
    for (const id of fresh) this.found.add(id);
    this.scout(fresh);
    this.changed(fresh);
  }

  changed(recent) {
    this.onChange({
      seedName: this.seedName,
      slotName: this.names.players.get(this.ownSlot) ?? this.slot,
      locations: this.locations,
      found: this.found,
      items: this.items,
      recent,
    });
  }
}

/**
 * Reads an Archipelago spoiler log. Returns { seed, players: [{ name, items: Map(locationId -> item) }] }.
 * Only players of this world are returned, and only locations on the map catalog (events are left out).
 */
export function parseSpoiler(text, world) {
  const lines = text.replace(/\r\n?/g, "\n").split("\n");
  const seed = lines[0]?.match(/Seed:\s*(\S+)/)?.[1] ?? "";
  const players = [];
  let current = null;
  let index = 0;
  for (; index < lines.length; index++) {
    const line = lines[index];
    if (/^Locations:\s*$/.test(line)) break;
    const player = line.match(/^Player \d+: (.+)$/);
    if (player) current = { name: player[1].trim(), game: "" };
    const game = line.match(/^Game:\s+(.+)$/);
    if (game) {
      // Single-player logs have no "Player" line before the game.
      current ??= { name: "", game: "" };
      current.game = game[1].trim();
      players.push(current);
      current = null;
    }
  }
  if (index >= lines.length) {
    throw new Error("This file has no Locations section. Open the Spoiler.txt from the seed's output.");
  }
  const ours = players.filter(player => player.game === world.game);
  if (!ours.length) throw new Error(`No ${world.game} player is in this spoiler log.`);
  const multi = players.length > 1;
  const ids = new Map(world.checks.map(check => [check.key, check.id]));
  const byName = new Map(ours.map(player => [player.name, { name: player.name, items: new Map() }]));
  for (index++; index < lines.length; index++) {
    const line = lines[index];
    if (!line.trim()) continue;
    // Section headers such as "Playthrough:" end the list.
    if (/^[A-Z][A-Za-z ]*:\s*$/.test(line)) break;
    const entry = splitEntry(line, multi, byName, ids);
    if (!entry) continue;
    if (entry.id !== undefined) byName.get(entry.player).items.set(entry.id, entry.item);
  }
  const result = [...byName.values()];
  if (!result.some(player => player.items.size)) {
    throw new Error("None of this log's locations are on the map. Is it from a different world version?");
  }
  return { seed, players: result };
}

// "Location (Player): Item (Player)" in a multiworld, "Location: Item" alone. Names may contain ": ".
function splitEntry(line, multi, byName, ids) {
  let at = line.indexOf(": ");
  while (at !== -1) {
    const left = line.slice(0, at);
    const item = line.slice(at + 2).trim();
    if (multi) {
      const match = left.match(/^(.*) \((.+)\)$/);
      if (match && byName.has(match[2])) {
        // Items for the same player read like a single-player log, as the room shows them.
        const own = ` (${match[2]})`;
        return { player: match[2], id: ids.get(match[1]), item: item.endsWith(own) ? item.slice(0, -own.length) : item };
      }
    } else if (ids.has(left) || !line.slice(at + 2).includes(": ")) {
      return { player: [...byName.keys()][0], id: ids.get(left), item };
    }
    at = line.indexOf(": ", at + 2);
  }
  return null;
}
