import { element, games, loadWorld, showError } from "./hub.js";
import { parseSpoiler, RoomTracker } from "./tracker.js";

const filters = document.querySelector("#map-filters");
const atlas = document.querySelector("#atlas");
const results = document.querySelector("#map-results");
const count = document.querySelector("#map-count");
const SVG_NS = "http://www.w3.org/2000/svg";

const MAP_IMAGES = {
  HK: "./assets/maps/hk.png",
  SS: "./assets/maps/ss.png",
  HK_WP: "./assets/maps/hk_white_palace.png",
};

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

// Persist pan/zoom state per map key across re-renders.
const viewStates = new Map();

function svgEl(tag, attrs = {}) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v !== undefined && v !== null) node.setAttribute(k, String(v));
  }
  return node;
}

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

function checkMapRoom(check) {
  return check.mapRoom ?? check.room ?? null;
}

function attachPanZoom(viewport, svg, mapKey, baseW, baseH) {
  let state = viewStates.get(mapKey);
  if (!state) {
    state = { x: 0, y: 0, w: baseW, h: baseH };
    viewStates.set(mapKey, state);
  }

  function apply() {
    state.w = Math.min(baseW, Math.max(baseW / 6, state.w));
    state.h = state.w * (baseH / baseW);
    state.x = Math.max(0, Math.min(baseW - state.w, state.x));
    state.y = Math.max(0, Math.min(baseH - state.h, state.y));
    svg.setAttribute("viewBox", `${state.x.toFixed(1)} ${state.y.toFixed(1)} ${state.w.toFixed(1)} ${state.h.toFixed(1)}`);
  }
  apply();

  function zoomBy(factor, clientX, clientY) {
    const rect = svg.getBoundingClientRect();
    const rx = rect.width ? (clientX - rect.left) / rect.width : 0.5;
    const ry = rect.height ? (clientY - rect.top) / rect.height : 0.5;
    const oldW = state.w;
    const oldH = state.h;
    const newW = Math.min(baseW, Math.max(baseW / 6, oldW * factor));
    const newH = newW * (baseH / baseW);
    state.x += (oldW - newW) * rx;
    state.y += (oldH - newH) * ry;
    state.w = newW;
    state.h = newH;
    apply();
  }

  viewport.addEventListener("wheel", event => {
    event.preventDefault();
    zoomBy(event.deltaY > 0 ? 1.2 : 1 / 1.2, event.clientX, event.clientY);
  }, { passive: false });

  let drag = null;
  viewport.addEventListener("pointerdown", event => {
    if (event.button !== 0 || event.target.closest(".atlas-pin")) return;
    drag = { id: event.pointerId, sx: event.clientX, sy: event.clientY, ox: state.x, oy: state.y, moved: false };
    viewport.setPointerCapture(event.pointerId);
    viewport.classList.add("dragging");
  });
  viewport.addEventListener("pointermove", event => {
    if (!drag || event.pointerId !== drag.id) return;
    const dx = event.clientX - drag.sx;
    const dy = event.clientY - drag.sy;
    if (Math.hypot(dx, dy) > 4) drag.moved = true;
    const rect = svg.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    state.x = drag.ox - dx * (state.w / rect.width);
    state.y = drag.oy - dy * (state.h / rect.height);
    apply();
  });
  const endDrag = event => {
    if (!drag || event.pointerId !== drag.id) return;
    drag = null;
    viewport.classList.remove("dragging");
  };
  viewport.addEventListener("pointerup", endDrag);
  viewport.addEventListener("pointercancel", endDrag);

  return {
    zoomIn() {
      const rect = svg.getBoundingClientRect();
      zoomBy(1 / 1.35, rect.left + rect.width / 2, rect.top + rect.height / 2);
    },
    zoomOut() {
      const rect = svg.getBoundingClientRect();
      zoomBy(1.35, rect.left + rect.width / 2, rect.top + rect.height / 2);
    },
    reset() {
      state.x = 0;
      state.y = 0;
      state.w = baseW;
      state.h = baseH;
      apply();
    },
  };
}

function buildInteractiveMap({ mapKey, gameClass, imageUrl, width, height, pins, activeArea, activeRoom, ariaLabel }) {
  const stage = element("div", undefined, { class: "atlas-stage" });
  const viewport = element("div", undefined, { class: "atlas-viewport", role: "region", "aria-label": ariaLabel });
  const svg = svgEl("svg", {
    class: "atlas-svg",
    viewBox: `0 0 ${width} ${height}`,
    preserveAspectRatio: "xMidYMid meet",
  });
  svg.append(svgEl("image", { href: imageUrl, x: 0, y: 0, width, height }));

  const pinLayer = svgEl("g", { class: "atlas-pins" });
  const tooltip = element("div", undefined, { class: "atlas-tooltip", hidden: true });

  for (const pin of pins) {
    const g = svgEl("g", {
      class: "atlas-pin",
      transform: `translate(${pin.x.toFixed(1)}, ${pin.y.toFixed(1)})`,
      tabindex: "0",
      role: "button",
      "aria-label": `${pin.title}: ${pin.summary}`,
    });
    if (pin.roomId) g.dataset.room = pin.roomId;
    if (pin.areaKey) g.dataset.area = pin.areaKey;
    if (pin.isPop) g.classList.add("pop");
    const isSelected = (activeRoom && pin.roomId === activeRoom) ||
      (!activeRoom && activeArea && pin.areaKey === activeArea && !pin.roomId);
    const isDimmed = Boolean(activeArea && pin.areaKey && pin.areaKey !== activeArea);
    if (isSelected) g.classList.add("selected");
    if (isDimmed) g.classList.add("dimmed");
    if (pin.lit) g.classList.add("lit");
    if (tracking.active) {
      if (pin.found === pin.total) g.classList.add("cleared");
      else if (pin.found > 0) g.classList.add("partial");
    }

    const r = pin.total >= 10 ? 11.5 : pin.total >= 4 ? 9.5 : 8;
    if (isSelected) {
      g.append(svgEl("circle", { class: "pin-ring", r: r + 4.5 }));
    }
    if (pin.isDiamond) {
      const d = r + 1;
      g.append(svgEl("polygon", { class: "pin-body", points: `0,${-d} ${d},0 0,${d} ${-d},0` }));
    } else {
      g.append(svgEl("circle", { class: "pin-body", r }));
    }
    if (pin.total > 1) {
      const label = svgEl("text", {});
      label.textContent = tracking.active ? `${pin.total - pin.found}` : `${pin.total}`;
      g.append(label);
    }

    const showTip = event => {
      tooltip.replaceChildren(
        element("strong", pin.title),
        element("small", pin.summary),
        ...(pin.preview ? [element("small", pin.preview)] : []),
      );
      tooltip.hidden = false;
      const rect = stage.getBoundingClientRect();
      const px = (event.clientX ?? rect.left + rect.width / 2) - rect.left + 14;
      const py = (event.clientY ?? rect.top + rect.height / 2) - rect.top + 14;
      tooltip.style.left = `${Math.min(rect.width - 260, Math.max(8, px))}px`;
      tooltip.style.top = `${Math.min(rect.height - 90, Math.max(8, py))}px`;
    };
    g.addEventListener("pointerenter", showTip);
    g.addEventListener("pointermove", showTip);
    g.addEventListener("pointerleave", () => { tooltip.hidden = true; });
    g.addEventListener("focus", showTip);
    g.addEventListener("blur", () => { tooltip.hidden = true; });
    g.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        g.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      }
    });
    pinLayer.append(g);
  }
  svg.append(pinLayer);
  viewport.append(svg);

  const controls = attachPanZoom(viewport, svg, mapKey, width, height);
  const toolbar = element("div", undefined, { class: "atlas-toolbar" });
  const zoomIn = element("button", "+", { type: "button", "aria-label": "Zoom in", title: "Zoom in" });
  const zoomOut = element("button", "−", { type: "button", "aria-label": "Zoom out", title: "Zoom out" });
  const reset = element("button", "Reset", { type: "button", title: "Reset view" });
  zoomIn.addEventListener("click", controls.zoomIn);
  zoomOut.addEventListener("click", controls.zoomOut);
  reset.addEventListener("click", controls.reset);
  toolbar.append(zoomIn, zoomOut, reset);

  stage.append(viewport, toolbar, tooltip);
  return stage;
}

function summarizeChecks(checks, recent) {
  let found = 0;
  let lit = false;
  for (const check of checks) {
    if (tracking.found(check.id)) found++;
    if (recent.has(check.id)) lit = true;
  }
  const total = checks.length;
  const summary = tracking.active ? `${found} / ${total} found` : `${total} ${total === 1 ? "check" : "checks"}`;
  const names = checks.slice(0, 3).map(c => c.name);
  const preview = names.join(" · ") + (checks.length > 3 ? ` (+${checks.length - 3} more)` : "");
  return { total, found, lit, summary, preview };
}

function renderWhitePalaceSubmap(wpMeta, matches, activeRoom, recent) {
  const section = element("section", undefined, { class: "kingdom atlas-kingdom atlas-submap hk" });
  const heading = element("h3", "White Palace & Path of Pain");
  const wpRoomIds = new Set(Object.keys(wpMeta.rooms));
  const own = matches.filter(check => check.game === "HK" && check.room && wpRoomIds.has(check.room));
  if (tracking.active) {
    const found = own.filter(check => tracking.found(check.id)).length;
    heading.append(element("small", ` ${found} / ${own.length} found`, { class: "kingdom-progress" }));
  }
  const note = element("p", "Room-by-room illustrated floorplan. Click a room pin to filter checks.", { class: "atlas-submap-note" });

  const byRoom = new Map();
  for (const check of own) {
    if (!byRoom.has(check.room)) byRoom.set(check.room, []);
    byRoom.get(check.room).push(check);
  }
  const pins = [];
  for (const [roomId, roomChecks] of byRoom.entries()) {
    const info = wpMeta.rooms[roomId];
    const stats = summarizeChecks(roomChecks, recent);
    pins.push({
      roomId,
      areaKey: "HK|White Palace",
      x: info.x,
      y: info.y - 8,
      title: info.label,
      isPop: info.group === "pop",
      isDiamond: true,
      ...stats,
    });
  }
  const { width, height } = wpMeta.projection;
  const stage = buildInteractiveMap({
    mapKey: "HK_WP",
    gameClass: "hk",
    imageUrl: MAP_IMAGES.HK_WP,
    width,
    height,
    pins,
    activeArea: "HK|White Palace",
    activeRoom,
    ariaLabel: "White Palace and Path of Pain illustrated map",
  });
  section.append(heading, note, stage);
  return section;
}

function renderAtlas(mapsMeta, matches, available, activeArea, activeRoom, recent) {
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
    const meta = mapsMeta[game];
    const section = element("section", undefined, { class: `kingdom atlas-kingdom ${game.toLowerCase()}` });
    const heading = element("h3", label);
    const ownMatches = matches.filter(check => check.game === game);
    if (tracking.active) {
      const found = ownMatches.filter(check => tracking.found(check.id)).length;
      heading.append(element("small", ` ${found} / ${ownMatches.length} found`, { class: "kingdom-progress" }));
    }

    // Group matching checks by exact room (falling back to area centroid for global/unmapped checks).
    const byPin = new Map();
    for (const check of ownMatches) {
      const room = checkMapRoom(check);
      const roomInfo = room ? meta.rooms[room] : null;
      if (roomInfo) {
        const pinKey = `room:${room}`;
        if (!byPin.has(pinKey)) {
          byPin.set(pinKey, {
            roomId: room,
            areaKey: areaKey(check, roomInfo.area),
            x: roomInfo.x,
            y: roomInfo.y,
            title: `${roomInfo.area} · ${room}`,
            isPop: room.startsWith("White_Palace_1") && ["17", "18", "19", "20"].some(s => room.endsWith(s)),
            checks: [],
          });
        }
        byPin.get(pinKey).checks.push(check);
      } else {
        const primaryArea = check.areas[0] ?? "Unmapped";
        const pt = meta.areas[primaryArea] ?? [meta.projection.width - 140, 110];
        const pinKey = `area:${primaryArea}`;
        if (!byPin.has(pinKey)) {
          byPin.set(pinKey, {
            roomId: "",
            areaKey: areaKey(check, primaryArea),
            x: pt[0],
            y: pt[1] + 20,
            title: `${primaryArea} (area checks)`,
            isDiamond: true,
            checks: [],
          });
        }
        byPin.get(pinKey).checks.push(check);
      }
    }

    const pins = [...byPin.values()].map(group => ({
      ...group,
      ...summarizeChecks(group.checks, recent),
    }));

    const { width, height } = meta.projection;
    const stage = buildInteractiveMap({
      mapKey: game,
      gameClass: game.toLowerCase(),
      imageUrl: MAP_IMAGES[game],
      width,
      height,
      pins,
      activeArea,
      activeRoom,
      ariaLabel: `${label} illustrated map`,
    });

    const chips = element("div", undefined, { class: "atlas-chips", role: "group", "aria-label": `${label} areas` });
    for (const key of keys) {
      const area = key.split("|")[1];
      const { total, found, lit } = areas.get(key);
      const button = element("button", undefined, {
        type: "button", "data-area": key, "aria-pressed": String(activeArea === key),
      });
      const summary = tracking.active ? `${found}/${total}` : `${total}`;
      button.append(element("span", area), element("small", summary));
      if (key === "HK|White Palace") button.classList.add("has-submap");
      if (tracking.active && total) {
        button.style.setProperty("--found", `${(found / total) * 100}%`);
        button.classList.add("tracked");
        if (found === total) button.classList.add("cleared");
        if (lit) button.classList.add("lit");
      }
      if (!total) button.disabled = true;
      chips.append(button);
    }

    section.append(heading, stage, chips);
    const result = [section];
    if (activeArea === "HK|White Palace" && game === "HK" && mapsMeta.HK_WP) {
      result.push(renderWhitePalaceSubmap(mapsMeta.HK_WP, matches, activeRoom, recent));
    }
    return result;
  });
  atlas.replaceChildren(...kingdoms);
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

async function loadMapsMeta() {
  const response = await fetch("./assets/maps/maps_meta.json");
  if (!response.ok) throw new Error(`Could not load map metadata (${response.status}).`);
  return response.json();
}

Promise.all([loadWorld(), loadMapsMeta()]).then(([world, mapsMeta]) => {
  const { game, area, type, query, status } = filters.elements;
  const pageSize = 50;
  let limit = pageSize;
  let filtered = [];
  let activeRoom = "";
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
      (!activeRoom || checkMapRoom(check) === activeRoom || check.room === activeRoom) &&
      (!status.value || tracking.found(check.id) === (status.value === "found")));

    renderAtlas(mapsMeta, matching, available, area.value, activeRoom, recent);
    const wpLabel = mapsMeta.HK_WP?.rooms?.[activeRoom]?.label;
    resultsTitle.textContent = area.value
      ? (activeRoom ? `${area.value.split("|")[1]} · ${wpLabel ?? activeRoom}` : area.value.split("|")[1])
      : (activeRoom ? `Room ${wpLabel ?? activeRoom}` : "All checks");
    back.hidden = !area.value && !activeRoom;
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

  // Room tracking
  const room = new RoomTracker(world, {
    onChange(state) {
      tracking.room = state;
      if (state.recent.length) {
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
  filters.addEventListener("change", () => {
    activeRoom = "";
    update();
  });
  filters.addEventListener("submit", event => event.preventDefault());
  query.addEventListener("input", update);
  atlas.addEventListener("click", event => {
    const pin = event.target.closest(".atlas-pin");
    if (pin) {
      const pinRoom = pin.dataset.room ?? "";
      const pinArea = pin.dataset.area ?? "";
      if (pinRoom) {
        const same = activeRoom === pinRoom;
        activeRoom = same ? "" : pinRoom;
        if (!same && pinArea) area.value = pinArea;
      } else if (pinArea) {
        area.value = area.value === pinArea ? "" : pinArea;
        activeRoom = "";
      }
      update();
      if (activeRoom || area.value) {
        resultsTitle.focus({ preventScroll: true });
        resultsTitle.scrollIntoView({ block: "start" });
      }
      return;
    }
    const chip = event.target.closest("button[data-area]");
    if (!chip) return;
    area.value = area.value === chip.dataset.area ? "" : chip.dataset.area;
    activeRoom = "";
    update();
    if (area.value === "HK|White Palace") {
      atlas.querySelector(".atlas-submap")?.scrollIntoView({ block: "nearest" });
    } else if (area.value) {
      resultsTitle.focus({ preventScroll: true });
      resultsTitle.scrollIntoView({ block: "start" });
    }
  });
  back.addEventListener("click", () => {
    atlas.scrollIntoView({ block: "start" });
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
