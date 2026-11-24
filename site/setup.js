import { element, loadWorld, releaseUrl, showError } from "./hub.js";

const setup = document.querySelector("#setup");
const steps = document.querySelector("#steps");
const progress = document.querySelector("#progress");
const completed = new Set();

function render(world) {
  const release = releaseUrl(world);
  const role = setup.elements.role.value;
  const method = setup.elements.method.value;
  const player = role !== "host";
  const host = role !== "join";
  const tasks = [
    ...(player ? [
      ["steam", "Get the Steam games ready",
        "Install the Steam games enabled for your slot. Tested builds are Hollow Knight 1.5.12620 and Silksong 1.0.30000. Keep Steam running while you play."],
      ["launcher", "Install HKSS Launcher", "Run the Windows installer from the matching release.",
        "Get the launcher", release],
    ] : []),
    ["yaml", host ? "Collect player YAMLs" : "Send your YAML to the host",
      host ? "Collect one YAML per player. Use the exact name from each YAML when sharing the room details."
        : `Create a YAML for ${world.game} and send it to the host before they generate the seed.`,
      "Open the YAML generator", "./yaml.html"],
    ...(host ? [
      ["world", "Install the combined world",
        method === "file"
          ? `Install Archipelago ${world.minimumArchipelago} or newer. Open hkss.apworld with Archipelago Launcher and accept the installation prompt. Restart the launcher if it was already open.`
          : `Install Archipelago ${world.minimumArchipelago} or newer. Copy hkss.apworld into Archipelago's custom_worlds folder, then reopen Archipelago Launcher.`,
        "Get hkss.apworld", release],
      ["generate", "Generate and host the multiworld",
        "Put the collected YAMLs in Archipelago’s Players folder and run Generate. Host the resulting multiworld ZIP in an Archipelago room or with your local server. Share the address, port and room password if you set one."],
    ] : []),
    ...(player ? [
      ["connect", "Connect your run",
        "In HKSS Launcher, create a run and open its Settings. Enter the host address, port, your exact YAML slot name and the room password if required. Start the run from the launcher."],
      ["travel", "Find the town gates",
        "Combo runs start in Hallownest. Enter the Dirtmouth gate to reach Pharloom. After you reach Bone Bottom, its gate leads back to Dirtmouth."],
    ] : []),
  ];

  function updateProgress() {
    progress.textContent = `${tasks.filter(([key]) => completed.has(key)).length} of ${tasks.length} steps complete`;
  }

  steps.replaceChildren(...tasks.map(([key, title, body, link, href]) => {
    const item = element("li", undefined, { class: "check-step" });
    const label = element("label");
    const checkbox = element("input", undefined, { type: "checkbox" });
    checkbox.checked = completed.has(key);
    checkbox.addEventListener("change", () => {
      if (checkbox.checked) completed.add(key);
      else completed.delete(key);
      updateProgress();
    });
    label.append(checkbox, element("strong", title));
    item.append(label, element("p", body));
    if (href) item.append(element("a", link, { href }));
    return item;
  }));
  document.querySelector("#install-method").hidden = !host;
  updateProgress();
}

loadWorld().then(world => {
  setup.hidden = false;
  setup.addEventListener("change", event => {
    if (event.target.matches("select")) render(world);
  });
  render(world);
}).catch(showError);
