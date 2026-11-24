import { element, games, loadWorld, showError } from "./hub.js";

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

function renderAtlas(matches, available, activeArea, routes) {
  const previousScroll = new Map([...atlas.querySelectorAll(".atlas-scroll")]
    .map(node => [node.dataset.game, node.scrollLeft]));
  const areas = new Map();
  for (const check of available) {
    for (const area of check.areas) areas.set(areaKey(check, area), 0);
  }
  for (const check of matches) {
    for (const area of check.areas) {
      const key = areaKey(check, area);
      areas.set(key, areas.get(key) + 1);
    }
  }
  const kingdoms = Object.entries(games).flatMap(([game, label]) => {
    const keys = sort([...areas.keys()].filter(key => key.startsWith(`${game}|`)));
    if (!keys.length) return [];
    const section = element("section", undefined, { class: `kingdom atlas-kingdom ${game.toLowerCase()}` });
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
      const button = element("button", undefined, {
        type: "button", "data-area": key, "aria-pressed": String(activeArea === key),
      });
      const [x, y] = positions.get(area);
      button.style.left = `${(x + .5) * cellWidth}px`;
      button.style.top = `${(y + .5) * cellHeight}px`;
      button.style.width = `${cellWidth - 10}px`;
      button.style.height = `${cellHeight - 12}px`;
      button.append(element("span", area), element("small", `${areas.get(key)} checks`));
      if (!areas.get(key)) button.disabled = true;
      tiles.append(button);
    }
    scroll.append(tiles);
    section.append(element("h3", label), scroll);
    return [section];
  });
  atlas.replaceChildren(...kingdoms);
  for (const scroll of atlas.querySelectorAll(".atlas-scroll")) {
    scroll.scrollLeft = previousScroll.get(scroll.dataset.game) ?? 0;
  }
}

function renderResults(checks, limit) {
  const items = checks.slice(0, limit).map(check => {
    const row = element("li", undefined, { class: `check-result ${check.game.toLowerCase()}` });
    row.append(
      element("strong", check.name),
      element("span", [games[check.game], check.areas.join(" / "), check.types.join(" / ")].join(" · ")),
      element("code", check.key),
    );
    if (check.room) row.append(element("span", `Room ${check.room}`));
    return row;
  });
  results.replaceChildren(...items);
  document.querySelector("#map-more").hidden = checks.length <= limit;
}

loadWorld().then(world => {
  const { game, area, type, query } = filters.elements;
  const pageSize = 50;
  let limit = pageSize;
  let filtered = [];
  const more = document.querySelector("#map-more");
  const resultsTitle = document.querySelector("#map-results-title");
  const back = document.querySelector("#map-back");

  function render() {
    const available = world.checks.filter(check => !game.value || check.game === game.value);
    const areas = sort(available.flatMap(check => check.areas.map(name => areaKey(check, name))));
    addOptions(area, areas.map(key => {
      const [code, name] = key.split("|");
      return { key, label: game.value ? name : `${games[code]} · ${name}` };
    }), "All areas");
    addOptions(type, sort(available.flatMap(check => check.types)), "All check types");

    const term = query.value.trim().toLowerCase();
    const matching = available.filter(check =>
      (!type.value || check.types.includes(type.value)) &&
      (!term || [check.name, check.key, check.room, check.id, ...check.areas]
        .some(value => String(value ?? "").toLowerCase().includes(term))));
    filtered = matching.filter(check => !area.value || check.areas.some(name => areaKey(check, name) === area.value));
    renderAtlas(matching, available, area.value, world.routes);
    resultsTitle.textContent = area.value ? area.value.split("|")[1] : "All checks";
    back.hidden = !atlas.querySelector('[aria-pressed="true"]:enabled');
    count.textContent = filtered.length
      ? `${filtered.length.toLocaleString()} matching ${filtered.length === 1 ? "check" : "checks"}`
      : "No checks match these filters.";
    renderResults(filtered, limit);
  }

  function update() {
    limit = pageSize;
    render();
  }

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
    renderResults(filtered, limit);
    const next = results.children[firstNew];
    next.tabIndex = -1;
    next.focus();
  });
  filters.hidden = false;
  render();
}).catch(showError);
