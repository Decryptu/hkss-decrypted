export const games = { HK: "Hallownest", SS: "Pharloom" };

export function releaseUrl(world) {
  return `https://github.com/Decryptu/hkss-decrypted/releases/tag/v${encodeURIComponent(world.version)}`;
}

export function element(tag, text, attributes = {}) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, value);
  return node;
}

export async function loadWorld() {
  const response = await fetch(new URL("./world.json", import.meta.url));
  if (!response.ok) throw new Error("The world catalog could not be loaded.");
  const world = await response.json();
  const version = document.querySelector("[data-world-version]");
  if (version) version.textContent = `World v${world.version} · Archipelago ${world.minimumArchipelago}+`;
  for (const link of document.querySelectorAll("[data-release]")) link.href = releaseUrl(world);
  return world;
}

export function showError(error) {
  document.querySelector("[data-load-error]").textContent =
    `${error.message} Try reloading the page.`;
}
