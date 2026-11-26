import { element, games, loadWorld, showError } from "./hub.js";
import { parseSpoiler, RoomTracker } from "./tracker.js";

const filters = document.querySelector("#map-filters");
const atlas = document.querySelector("#atlas");
const results = document.querySelector("#map-results");
const count = document.querySelector("#map-count");

// Approximate west/east and elevation, independent of the room dimensions.
const layouts = {
  HK: {
    "Howling Cliffs": [0, 0], Dirtmouth: [2, 0], "Crystal Peak": [4, 0],
    Greenpath: [0, 1], "Forgotten Crossroads": [2, 1], "Resting Grounds": [4, 1],
    "Queen's Gardens": [0, 2], "Fog Canyon": [1, 2], "City of Tears": [4, 2],
    Deepnest: [0, 3], "Fungal Wastes": [2, 3], "Royal Waterways": [4, 3], "Kingdom's Edge": [5, 3],
    "White Palace": [2, 4], "Ancient Basin": [4, 4],
  },
  SS: {
    "Mount Fay": [0, 0], "The Slab": [1, 0], "The Cradle": [4, 0], "High Halls": [5, 0],
    "Sands of Karak": [0, 1], "Cogwork Core": [3, 1], Songclave: [4, 1], Memorium: [5, 1],
    "Blasted Steps": [0, 2], "Grand Gate": [2, 2], "Choral Chambers": [3, 2],
    "Whispering Vaults": [4, 2], "Putrified Ducts": [5, 2],
    Shellwood: [0, 3], Bellhart: [1, 3], Underworks: [2, 3], Whiteward: [3, 3], Bilewater: [5, 3],
    Wormways: [0, 4], Greymoor: [2, 4], "Sinner's Road": [4, 4], Verdania: [5, 4],
    Mosslands: [0, 5], "The Marrow": [2, 5], "Hunter's March": [3, 5], "Far Fields": [4, 5],
    Weavenest: [0, 6], "Deep Docks": [3, 6], "The Abyss": [4, 6], Unmapped: [5, 6],
  },
};
const columns = 6;
const cellWidth = 140;
const cellHeight = 76;

const storage = {
  get(key) {
    try {
      return JSON.parse(localStorage.getItem(key) ?? "null");
    } catch {
      return null;
    }
  },
  set(key, value) {
    try {
      if (value === null) localStorage.removeItem(key);
      else localStorage.setItem(key, JSON.stringify(value));
    } catch {
      // Private browsing or a full store: tracking still works for this visit.
    }
  },
};
const ROOM_KEY = "hkss-map-room";
const SPOILER_KEY = "hkss-map-spoiler";
const marksKey = spoiler => `hkss-map-marks:${spoiler.seed}:${spoiler.players[spoiler.player].name}`;

// What the page knows about the run being tracked.
const tracking = {
  room: null,
  spoiler: null,
  marks: new Set(),
  get active() { return Boolean(this.room || this.spoiler); },
  get spoilerItems() {
    return this.spoiler?.players[this.spoiler.player]?.items ?? null;
  },
  get seedChecks() {
    return this.room?.locations ?? this.spoilerItems ?? null;
  },
  found(id) {
    return this.room ? this.room.found.has(id) : this.marks.has(id);
  },
  item(id) {
    return this.room?.items.get(id) ?? this.spoilerItems?.get(id);
  },
};

function sort(values) {
  return [...new Set(values)].sort((a, b) => a.localeCompare(b));
}

function addOptions(select, values, initial) {
  const previous = select.value;
  select.replaceChildren(element("option", initial, { value: "" }), ...values.map(value =>
    element("option", value.label ?? value, { value: value.key ?? value })));
  if ([...select.options].some(option => option.value === previous)) select.value = previous;
}

function areaKey(check, area) {
  return `${check.game}|${area}`;
}

function renderAtlas(matches, available, activeArea, routes, recent) {
  const previousScroll = new Map([...atlas.querySelectorAll(".atlas-scroll")]
    .map(node => [node.dataset.game, node.scrollLeft]));
  const areas = new Map();
  for (const check of available) {
    for (const area of check.areas) areas.set(areaKey(check, area), { total: 0, found: 0, lit: false });
  }
  for (const check of matches) {
    for (const area of check.areas) {
      const stats = areas.get(areaKey(check, area));
      stats.total++;
      if (tracking.found(check.id)) stats.found++;
      if (recent.has(check.id)) stats.lit = true;
    }
  }
  const kingdoms = Object.entries(games).flatMap(([game, label]) => {
    const keys = sort([...areas.keys()].filter(key => key.startsWith(`${game}|`)));
    if (!keys.length) return [];
    const section = element("section", undefined, { class: `kingdom atlas-kingdom ${game.toLowerCase()}` });
    const heading = element("h3", label);
    if (tracking.active) {
      // Count each check once, even when it belongs to two areas.
      const own = matches.filter(check => check.game === game);
      const total = own.length;
      const found = own.filter(check => tracking.found(check.id)).length;
      heading.append(element("small", ` ${found} / ${total} found`, { class: "kingdom-progress" }));
    }
    const scroll = element("div", undefined, {
      class: "atlas-scroll", "data-game": game, role: "region",
      "aria-label": `${label} map`, tabindex: "0",
    });
    const tiles = element("div", undefined, { class: "atlas-areas" });
    const positions = new Map();
    let rows = Math.max(...Object.values(layouts[game]).map(([, y]) => y)) + 1;
    let next = rows * columns;
    for (const key of keys) {
      const name = key.split("|")[1];
      const position = layouts[game][name];
      positions.set(name, position ?? [next % columns, Math.floor(next / columns)]);
      if (!position) next++;
    }
    rows = Math.ceil(next / columns);
    tiles.style.width = `${columns * cellWidth}px`;
    tiles.style.height = `${rows * cellHeight}px`;
    for (const [source, target] of routes[game]) {
      if (!positions.has(source) || !positions.has(target)) continue;
      const [x1, y1] = positions.get(source);
      const [x2, y2] = positions.get(target);
      const dx = (x2 - x1) * cellWidth;
      const dy = (y2 - y1) * cellHeight;
      const line = element("span", undefined, { class: "atlas-route", "aria-hidden": "true" });
      line.style.left = `${(x1 + .5) * cellWidth}px`;
      line.style.top = `${(y1 + .5) * cellHeight}px`;
      line.style.width = `${Math.hypot(dx, dy)}px`;
      line.style.transform = `rotate(${Math.atan2(dy, dx)}rad)`;
      tiles.append(line);
    }
    for (const key of keys) {
      const area = key.split("|")[1];
      const { total, found, lit } = areas.get(key);
      const button = element("button", undefined, {
        type: "button", "data-area": key, "aria-pressed": String(activeArea === key),
      });
      const [x, y] = positions.get(area);
      button.style.left = `${(x + .5) * cellWidth}px`;
      button.style.top = `${(y + .5) * cellHeight}px`;
      button.style.width = `${cellWidth - 10}px`;
      button.style.height = `${cellHeight - 12}px`;
      const summary = tracking.active ? `${found} / ${total} found` : `${total} ${total === 1 ? "check" : "checks"}`;
      button.append(element("span", area), element("small", summary));
      if (tracking.active && total) {
        button.style.setProperty("--found", `${(found / total) * 100}%`);
        button.classList.add("tracked");
        if (found === total) button.classList.add("cleared");
        if (lit) button.classList.add("lit");
      }
      if (!total) button.disabled = true;
      tiles.append(button);
    }
    scroll.append(tiles);
    section.append(heading, scroll);
    return [section];
  });
  atlas.replaceChildren(...kingdoms);
  for (const scroll of atlas.querySelectorAll(".atlas-scroll")) {
    scroll.scrollLeft = previousScroll.get(scroll.dataset.game) ?? 0;
  }
}

function renderResults(checks, limit, recent, reveal) {
  const marking = Boolean(tracking.spoiler && !tracking.room);
  const items = checks.slice(0, limit).map(check => {
    const found = tracking.active && tracking.found(check.id);
    const row = element("li", undefined, { class: `check-result ${check.game.toLowerCase()}` });
    if (found) row.classList.add("found");
    if (recent.has(check.id)) row.classList.add("lit");
    const name = element("strong", check.name);
    if (found) name.prepend(element("span", "✓ ", { class: "found-mark", "aria-label": "Found: " }));
    row.append(
      name,
      element("span", [games[check.game], check.areas.join(" / "), check.types.join(" / ")].join(" · ")),
      element("code", check.key),
    );
    if (check.room) row.append(element("span", `Room ${check.room}`));
    const item = tracking.item(check.id);
    if (item && (found || reveal)) row.append(element("span", `Holds ${item}`, { class: "check-item" }));
    if (marking) {
      row.append(element("button", found ? "Mark not found" : "Mark found", {
        type: "button", "data-mark": String(check.id), class: "mark-button",
      }));
    }
    return row;
  });
  results.replaceChildren(...items);
  document.querySelector("#map-more").hidden = checks.length <= limit;
}

loadWorld().then(world => {
  const { game, area, type, query, status } = filters.elements;
  const pageSize = 50;
  let limit = pageSize;
  let filtered = [];
  let recent = new Set();
  let litTimer;
  const more = document.querySelector("#map-more");
  const resultsTitle = document.querySelector("#map-results-title");
  const back = document.querySelector("#map-back");
  const roomForm = document.querySelector("#room-form");
  const spoilerForm = document.querySelector("#spoiler-form");
  const trackingStatus = document.querySelector("#tracking-status");
  const seedNote = document.querySelector("[data-seed-note]");
  const statusMessages = { room: "", spoiler: "" };

  function showStatus() {
    trackingStatus.textContent = [statusMessages.room, statusMessages.spoiler].filter(Boolean).join(" ");
  }

  function render() {
    const seedChecks = tracking.seedChecks;
    const available = world.checks.filter(check =>
      (!game.value || check.game === game.value) && (!seedChecks || seedChecks.has(check.id)));
    const areas = sort(available.flatMap(check => check.areas.map(name => areaKey(check, name))));
    addOptions(area, areas.map(key => {
      const [code, name] = key.split("|");
      return { key, label: game.value ? name : `${games[code]} · ${name}` };
    }), "All areas");
    addOptions(type, sort(available.flatMap(check => check.types)), "All check types");
    for (const node of document.querySelectorAll("[data-tracking-only]")) node.hidden = !tracking.active;
    if (!tracking.active) status.value = "";

    const term = query.value.trim().toLowerCase();
    const reveal = Boolean(spoilerForm.elements.reveal.checked && tracking.spoilerItems);
    const matching = available.filter(check =>
      (!type.value || check.types.includes(type.value)) &&
      (!term || [check.name, check.key, check.room, check.id, ...check.areas,
        ...(tracking.found(check.id) || reveal ? [tracking.item(check.id)] : [])]
        .some(value => String(value ?? "").toLowerCase().includes(term))));
    filtered = matching.filter(check =>
      (!area.value || check.areas.some(name => areaKey(check, name) === area.value)) &&
      (!status.value || tracking.found(check.id) === (status.value === "found")));
    renderAtlas(matching, available, area.value, world.routes, recent);
    resultsTitle.textContent = area.value ? area.value.split("|")[1] : "All checks";
    back.hidden = !atlas.querySelector('[aria-pressed="true"]:enabled');
    count.textContent = filtered.length
      ? `${filtered.length.toLocaleString()} matching ${filtered.length === 1 ? "check" : "checks"}`
      : "No checks match these filters.";
    if (seedChecks) {
      const mapped = world.checks.filter(check => seedChecks.has(check.id)).length;
      const missing = seedChecks.size - mapped;
      seedNote.textContent = `Showing the ${mapped.toLocaleString()} checks in this seed.` +
        (missing > 0 ? ` ${missing.toLocaleString()} more ${missing === 1 ? "is" : "are"} not on the map.` : "");
    } else {
      seedNote.textContent = "Available checks vary with seed settings.";
    }
    renderResults(filtered, limit, recent, reveal);
  }

  function update() {
    limit = pageSize;
    render();
  }

  // Room
  const room = new RoomTracker(world, {
    onChange(state) {
      tracking.room = state;
      if (state.recent.length) {
        // Light the new checks once; later renders show them as plain found checks.
        recent = new Set([...recent, ...state.recent]);
        clearTimeout(litTimer);
        litTimer = setTimeout(() => { recent = new Set(); }, 4000);
      }
      render();
    },
    onStatus(kind, message) {
      statusMessages.room = message;
      const following = kind === "connecting" || kind === "connected" || kind === "retrying";
      roomForm.elements.connect.hidden = following;
      roomForm.elements.disconnect.hidden = !following;
      for (const name of ["address", "slot", "password"]) roomForm.elements[name].disabled = following;
      if (kind === "error" || kind === "closed") {
        tracking.room = null;
        recent = new Set();
        render();
      }
      showStatus();
    },
  });
  const savedRoom = storage.get(ROOM_KEY);
  if (savedRoom) {
    roomForm.elements.address.value = savedRoom.address ?? "";
    roomForm.elements.slot.value = savedRoom.slot ?? "";
  }
  roomForm.addEventListener("submit", event => {
    event.preventDefault();
    const { address, slot, password } = roomForm.elements;
    try {
      room.connect(address.value, slot.value, password.value, location.protocol === "https:");
      storage.set(ROOM_KEY, { address: address.value.trim(), slot: slot.value.trim() });
    } catch (error) {
      statusMessages.room = error.message;
      showStatus();
    }
  });
  roomForm.elements.disconnect.addEventListener("click", () => {
    room.disconnect();
    roomForm.elements.connect.focus();
  });

  // Spoiler log
  function showSpoiler() {
    const spoiler = tracking.spoiler;
    const { player } = spoilerForm.elements;
    for (const node of spoilerForm.querySelectorAll("[data-spoiler-only]")) node.hidden = !spoiler;
    spoilerForm.querySelector("[data-spoiler-player]").hidden = !spoiler || spoiler.players.length < 2;
    // A spoiler action replaces an old room error; a live room keeps its status.
    if (!tracking.room && !roomForm.elements.address.disabled) statusMessages.room = "";
    if (spoiler) {
      player.replaceChildren(...spoiler.players.map((entry, index) =>
        element("option", entry.name || "Player 1", { value: String(index) })));
      player.value = String(spoiler.player);
      tracking.marks = new Set(storage.get(marksKey(spoiler)) ?? []);
      const entry = spoiler.players[spoiler.player];
      statusMessages.spoiler = `Spoiler log${spoiler.seed ? ` for seed ${spoiler.seed}` : ""}` +
        `${entry.name ? `, player ${entry.name}` : ""}: ${entry.items.size.toLocaleString()} checks on the map.`;
    } else {
      tracking.marks = new Set();
      statusMessages.spoiler = "";
    }
    showStatus();
  }

  function saveSpoiler() {
    const spoiler = tracking.spoiler;
    storage.set(SPOILER_KEY, spoiler && {
      seed: spoiler.seed,
      player: spoiler.player,
      players: spoiler.players.map(entry => ({ name: entry.name, items: [...entry.items] })),
    });
  }

  const savedSpoiler = storage.get(SPOILER_KEY);
  if (Array.isArray(savedSpoiler?.players) && savedSpoiler.players.length) {
    tracking.spoiler = {
      seed: String(savedSpoiler.seed ?? ""),
      player: Math.min(Number(savedSpoiler.player) || 0, savedSpoiler.players.length - 1),
      players: savedSpoiler.players.map(entry => ({ ...entry, items: new Map(entry.items) })),
    };
  }

  spoilerForm.addEventListener("submit", event => event.preventDefault());
  spoilerForm.elements.file.addEventListener("change", async () => {
    const [file] = spoilerForm.elements.file.files;
    if (!file) return;
    try {
      const parsed = parseSpoiler(await file.text(), world);
      const slot = tracking.room?.slotName;
      const preferred = parsed.players.findIndex(entry => entry.name === slot);
      tracking.spoiler = { ...parsed, player: Math.max(0, preferred) };
      spoilerForm.elements.reveal.checked = false;
      saveSpoiler();
      showSpoiler();
      update();
    } catch (error) {
      statusMessages.spoiler = `${file.name}: ${error.message}`;
      showStatus();
    } finally {
      spoilerForm.elements.file.value = "";
    }
  });
  spoilerForm.elements.player.addEventListener("change", () => {
    tracking.spoiler.player = Number(spoilerForm.elements.player.value);
    saveSpoiler();
    showSpoiler();
    update();
  });
  spoilerForm.elements.reveal.addEventListener("change", render);
  spoilerForm.elements.forget.addEventListener("click", () => {
    tracking.spoiler = null;
    saveSpoiler();
    showSpoiler();
    update();
    spoilerForm.elements.file.focus();
  });
  results.addEventListener("click", event => {
    const button = event.target.closest("button[data-mark]");
    if (!button || !tracking.spoiler || tracking.room) return;
    const id = Number(button.dataset.mark);
    if (tracking.marks.has(id)) tracking.marks.delete(id);
    else tracking.marks.add(id);
    storage.set(marksKey(tracking.spoiler), [...tracking.marks]);
    const index = [...results.querySelectorAll("button[data-mark]")].indexOf(button);
    render();
    results.querySelectorAll("button[data-mark]")[index]?.focus();
  });

  // Filters and atlas
  filters.addEventListener("change", update);
  filters.addEventListener("submit", event => event.preventDefault());
  query.addEventListener("input", update);
  atlas.addEventListener("click", event => {
    const tile = event.target.closest("button[data-area]");
    if (!tile) return;
    area.value = area.value === tile.dataset.area ? "" : tile.dataset.area;
    update();
    if (area.value) {
      resultsTitle.focus({ preventScroll: true });
      resultsTitle.scrollIntoView({ block: "start" });
    } else {
      [...atlas.querySelectorAll("button")].find(button => button.dataset.area === tile.dataset.area)?.focus();
    }
  });
  back.addEventListener("click", () => {
    const tile = atlas.querySelector('[aria-pressed="true"]:enabled');
    tile.focus({ preventScroll: true });
    tile.scrollIntoView({ block: "center", inline: "nearest" });
  });
  more.addEventListener("click", () => {
    const firstNew = limit;
    limit += pageSize;
    renderResults(filtered, limit, recent, Boolean(spoilerForm.elements.reveal.checked && tracking.spoilerItems));
    const next = results.children[firstNew];
    next.tabIndex = -1;
    next.focus();
  });
  filters.hidden = false;
  document.querySelector("#tracking").hidden = false;
  showSpoiler();
  render();
}).catch(showError);
