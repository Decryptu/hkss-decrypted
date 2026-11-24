import { element, loadWorld, releaseUrl, showError } from "./hub.js";

const setup = document.querySelector("#setup");
const steps = document.querySelector("#steps");
const progress = document.querySelector("#progress");
const completed = new Set();

function render(world) {
  const release = releaseUrl(world);
  const role = setup.elements.role.value;
  const player = role !== "host";
  const host = role !== "join";
  const tasks = [
    ...(player ? [
      ["steam", "Get the Steam games ready",
        "Install the Steam games enabled for your slot. Tested builds are Hollow Knight 1.5.12620 and Silksong 1.0.30000. Keep Steam running while you play."],
      ["launcher", "Install HKSS Launcher", "Run the Windows installer from the matching release.",
        "Get the launcher", release],
    ] : []),
    [host ? "collect-yaml" : "send-yaml", host ? "Collect player YAMLs" : "Send your YAML to the host",
      host ? "Collect one YAML per player. Use the exact name from each YAML when sharing the room details."
        : `Create a YAML for ${world.game} and send it to the host before they generate the seed.`,
      "Open the YAML generator", "./yaml.html"],
    ...(host ? [
      ["world", "Add Hollow Knight × Silksong to Archipelago",
        `Install Archipelago ${world.minimumArchipelago} or newer first. Download hkss.apworld below, then double-click it in your Downloads folder. Archipelago Launcher opens. Confirm the installation when prompted, then reopen the launcher if it was already running.`,
        "Download hkss.apworld", release.replace("/tag/", "/download/") + "/hkss.apworld"],
      ["generate", "Generate and host the multiworld",
        "Put the collected YAMLs in Archipelago’s Players folder and run Generate. Host the resulting multiworld ZIP in an Archipelago room or with your local server. Share the address, port and room password if you set one.",
        "Run the host smoke test", "https://github.com/Decryptu/hkss-decrypted/blob/main/docs/PLAYTEST.md"],
    ] : []),
    ...(player ? [
      ["connect", "Connect your run",
        "In HKSS Launcher, open Settings on an unused run. Enter the host address, port, your exact YAML slot name and the room password if required. Choose Test connection, then Save room and start the run."],
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
    if (href) item.append(element("a", link, { href, target: "_blank", rel: "noopener" }));
    if (key === "world") {
      const help = element("details", undefined, { class: "check-step-help" });
      help.append(element("summary", "The file won’t open"),
        element("p", "Close Archipelago Launcher. Open the folder where you installed Archipelago and copy hkss.apworld into its custom_worlds folder. Then reopen Archipelago Launcher."));
      item.append(help);
    }
    return item;
  }));
  updateProgress();
}

loadWorld().then(world => {
  setup.hidden = false;
  setup.addEventListener("change", event => {
    if (event.target.matches("select")) render(world);
  });
  render(world);
}).catch(showError);
