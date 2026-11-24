import { element, loadWorld, showError } from "./hub.js";

const form = document.querySelector("#yaml-form");
const groups = document.querySelector("#option-groups");
const status = document.querySelector("#yaml-status");
const preview = document.querySelector("#yaml-preview");
const download = document.querySelector("#yaml-download");
const yaml = globalThis.jsyaml;
const yamlConfig = { schema: yaml.CORE_SCHEMA };

const isMapping = value => value !== null && typeof value === "object" && !Array.isArray(value);
const integer = /^-?\d+$/;
const randomRange = /^random-range-(-?\d+)-(-?\d+)$/;

function display(value) {
  return typeof value === "string" ? value : JSON.stringify(value);
}

function parseField(raw, option) {
  if (option.kind === "text") return raw;
  const input = raw.trim();
  if (input.startsWith("{") || input.startsWith("[") || ["mapping", "list"].includes(option.kind)) {
    return yaml.load(input, yamlConfig);
  }
  if (option.kind === "toggle" && ["true", "false"].includes(input)) return input === "true";
  if (option.kind !== "textChoice" && integer.test(input)) return Number(input);
  return input;
}

function validateScalar(value, option) {
  if (option.kind === "text" || option.kind === "textChoice") return typeof value === "string";
  const keyword = typeof value === "string" ? value.toLowerCase() : value;
  if (keyword === "random") return true;
  if (option.kind === "toggle") return [true, false, 0, 1, "on", "off", "yes", "no", "true", "false"].includes(keyword);
  if (option.kind === "choice") {
    return [...option.choices, ...option.aliases, ...option.numbers].includes(keyword);
  }
  if (Number.isInteger(value)) {
    return (value >= option.min && value <= option.max) || Object.values(option.names).includes(value);
  }
  if (typeof keyword !== "string") return false;
  if (Object.hasOwn(option.names, keyword) || ["random-low", "random-middle", "random-high"].includes(keyword)) return true;
  const bounds = keyword.match(randomRange);
  return Boolean(bounds && Number(bounds[1]) >= option.min && Number(bounds[2]) <= option.max
    && Number(bounds[1]) <= Number(bounds[2]));
}

function validOption(value, option, allowed) {
  if (option.kind === "mapping" || option.kind === "list") {
    if (option.kind === "mapping" ? !isMapping(value) : !Array.isArray(value)) return false;
    if (option.counts && !Object.values(value).every(count => Number.isSafeInteger(count) && count >= 0)) return false;
    const entries = Array.isArray(value) ? value : Object.keys(value);
    return entries.every(key => (!option.names || allowed[option.names].has(key))
      && (!option.keys || (typeof key === "string" && option.keys.includes(option.casefold ? key.toLowerCase() : key))));
  }
  if (!isMapping(value)) return validateScalar(value, option);
  const weights = Object.entries(value);
  return weights.some(([, weight]) => weight > 0) && weights.every(([key, weight]) =>
    Number.isSafeInteger(weight) && weight >= 0
    && validateScalar(integer.test(key) && option.kind !== "text" && option.kind !== "textChoice" ? Number(key) : key, option));
}

function hints(option) {
  if (option.kind === "toggle") return ["false", "true", "random"];
  if (option.choices) return [...option.choices, "random"];
  if (option.kind === "range") return [option.min, option.max, ...Object.keys(option.names), "random"];
  return [];
}

function optionEditor(key, option) {
  const row = element("div", undefined, { class: "option-row", "data-key": key });
  const id = `option-${key}`;
  const label = element("label", option.label || key, { for: id });
  const long = ["mapping", "list", "text"].includes(option.kind);
  const input = element(long ? "textarea" : "input", undefined, {
    id, spellcheck: "false", "aria-describedby": `${id}-hint`,
    ...(long ? { rows: "2" } : { autocomplete: "off" }),
  });
  const suggestions = hints(option);
  if (suggestions.length) {
    const list = element("datalist", undefined, { id: `${id}-values` });
    list.append(...suggestions.map(value => element("option", undefined, { value })));
    input.setAttribute("list", list.id);
    row.append(list);
  }
  const suffix = option.kind === "range" ? ` · ${option.min}–${option.max}` : "";
  const detail = element("details", undefined, { class: "option-help", id: `${id}-hint` });
  detail.append(element("summary", `${key}${suffix}`));
  if (option.help) detail.append(element("p", option.help));
  if (suggestions.length) detail.append(element("p", `Suggested values: ${suggestions.join(", ")}`));
  if (option.kind === "mapping") detail.append(element("p", "Enter a mapping, for example {} or {name: 1}."));
  if (option.kind === "list") detail.append(element("p", "Enter a list, for example [] or [name]."));
  input.value = display(option.default);
  row.append(label, input, detail);
  return { row, input };
}

loadWorld().then(world => {
  const options = new Map();
  const allowed = Object.fromEntries(Object.entries(world.names).map(([key, list]) => [key, new Set(list)]));
  const { slot, description, preset, file } = form.elements;
  let extras = {};
  let importedValues = {};
  let output = "";
  for (const group of world.groups) {
    const panel = element("details", undefined, { class: "option-group" });
    panel.open = group.name === "Combo";
    panel.append(element("summary", group.name));
    for (const [key, schema] of Object.entries(group.options)) {
      const editor = optionEditor(key, schema);
      options.set(key, { ...editor, schema });
      panel.append(editor.row);
    }
    groups.append(panel);
  }

  function applyValues(values) {
    importedValues = values;
    for (const [key, { input, schema }] of options) {
      input.value = display(Object.hasOwn(values, key) ? values[key] : schema.default);
    }
  }

  function validate() {
    const invalid = [];
    const playerName = slot.value.trim();
    if (!playerName || [...playerName].length > 16) invalid.push("Enter a slot name of 1–16 characters.");
    const values = {};
    for (const [key, { input, schema }] of options) {
      let valid = false;
      try {
        const original = Object.hasOwn(importedValues, key) ? importedValues[key] : schema.default;
        const value = input.value === display(original) ? original : parseField(input.value, schema);
        valid = validOption(value, schema, allowed);
        if (valid && (JSON.stringify(value) !== JSON.stringify(schema.default) || Object.hasOwn(importedValues, key))) {
          values[key] = value;
        }
      } catch { /* Malformed values are reported beside the option key below. */ }
      input.setAttribute("aria-invalid", String(!valid));
      if (!valid) invalid.push(`Check ${key}.`);
    }
    if (invalid.length) {
      status.textContent = invalid.join(" ");
      output = "";
    } else {
      output = yaml.dump({
        name: playerName,
        game: world.game,
        ...(description.value.trim() ? { description: description.value } : {}),
        ...extras,
        [world.game]: values,
      }, { lineWidth: -1, quotingType: '"' });
      status.textContent = "YAML is ready to download.";
    }
    preview.value = output;
    download.disabled = !output;
  }

  form.addEventListener("input", event => {
    if (event.target !== file && event.target !== preset) validate();
  });
  form.addEventListener("submit", event => {
    event.preventDefault();
    validate();
    if (!output) return;
    const url = URL.createObjectURL(new Blob([output], { type: "text/yaml;charset=utf-8" }));
    element("a", undefined, { href: url, download: `${slot.value.trim().replace(/[^a-z0-9_-]/gi, "_")}.yaml` }).click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  preset.addEventListener("change", () => {
    if (!preset.value) return;
    const overrides = {
      defaults: {}, either: { combo_goal: "either" },
      hk: { games: "hollow_knight_only" }, ss: { games: "silksong_only" },
    };
    applyValues(overrides[preset.value]);
    preset.value = "";
    validate();
  });
  file.addEventListener("change", async () => {
    const selected = file.files[0];
    if (!selected) return;
    try {
      if (selected.size > 1024 * 1024) throw new Error("Choose a YAML file under 1 MB.");
      const data = yaml.load(await selected.text(), yamlConfig);
      if (!isMapping(data) || data.game !== world.game || !isMapping(data[world.game])) {
        throw new Error(`Choose a single-slot YAML for ${world.game}.`);
      }
      const rootKeys = new Set(["name", "game", "description", "requires", world.game]);
      const unknown = [...Object.keys(data).filter(key => !rootKeys.has(key)),
        ...Object.keys(data[world.game]).filter(key => !options.has(key))];
      if (unknown.length) throw new Error(`Unsupported YAML keys: ${unknown.join(", ")}.`);
      if (typeof data.name !== "string" || (data.description !== undefined && typeof data.description !== "string")) {
        throw new Error("Slot name and description must be text.");
      }
      slot.value = data.name;
      description.value = data.description ?? "";
      extras = Object.hasOwn(data, "requires") ? { requires: data.requires } : {};
      applyValues(data[world.game]);
      validate();
    } catch (error) {
      status.textContent = error.message;
    } finally {
      file.value = "";
    }
  });
  form.hidden = false;
  validate();
}).catch(showError);
