<script lang="ts">
  import { invoke } from "@tauri-apps/api/core";
  import { listen } from "@tauri-apps/api/event";
  import { onMount } from "svelte";
  import logo from "../../site/assets/logo.webp";

  type Game = { key: string; name: string; build: string | null; tested: boolean };
  type Room = { host: string; port: number; slot: string; password: string; auto_connect: boolean };
  type Slot = { id: number; current: string | null; room: Room | null };
  type Status = { games: Game[]; slots: Slot[]; steam_running: boolean; version: string };
  const blankRoom = (): Room => ({ host: "archipelago.gg", port: 38281, slot: "", password: "", auto_connect: true });

  let status = $state<Status | null>(null);
  let error = $state("");
  let playing = $state(false);
  let editing = $state<number | null>(null);
  let draft = $state<Room>(blankRoom());
  let message = $state("");
  let update = $state<string | null>(null);

  function edit(slot: Slot) {
    error = message = "";
    if (editing === slot.id) { editing = null; return; }
    draft = { ...(slot.room ?? blankRoom()) };
    editing = slot.id;
  }

  async function act(command: string, args: Record<string, unknown>, done: string, close = true) {
    error = message = "";
    try {
      const result = await invoke<string | null>(command, args);
      message = result ? `${done} ${result}` : done;
      if (close) editing = null;
    } catch (e) {
      error = String(e);
    }
    await refresh();
  }

  const saveRoom = (id: number) => act("save_room", { slot: id, room: { ...draft, port: Number(draft.port) } }, `Run ${id} room saved.`);
  const clearRoom = (id: number) => act("clear_room", { slot: id }, `Run ${id} will play offline.`);
  const exportReport = (id: number) => act("export_diagnostics", { slot: id }, "Report written to", false);
  function deleteRun(id: number) {
    if (confirm(`Delete run ${id}? Both saves and its room settings are removed for good.`))
      act("delete_run", { slot: id }, `Run ${id} deleted.`);
  }
  function removeGameFolders() {
    if (confirm("Remove the modded game folders? Your Steam games and runs are not touched; the folders are rebuilt on the next run."))
      act("remove_game_folders", {}, "Modded game folders removed.");
  }

  const newer = (a: string, b: string) => {
    const pa = a.split(".").map(Number), pb = b.split(".").map(Number);
    for (let i = 0; i < 3; i++) if ((pa[i] ?? 0) !== (pb[i] ?? 0)) return (pa[i] ?? 0) > (pb[i] ?? 0);
    return false;
  };

  async function checkUpdate(version: string) {
    try {
      const r = await fetch("https://api.github.com/repos/Decryptu/hkss-decrypted/releases/latest");
      if (!r.ok) return;
      const tag = String((await r.json()).tag_name ?? "").replace(/^v/, "");
      if (tag && newer(tag, version)) update = tag;
    } catch {
      // Offline: nothing to report.
    }
  }

  const ready = $derived(!!status && status.steam_running && status.games.every((g) => g.build));
  const place = (key: string) => (key === "silksong" ? "Pharloom" : "Hallownest");

  async function refresh() {
    try {
      const first = !status;
      status = await invoke<Status>("status");
      if (first) checkUpdate(status.version);
    } catch (e) {
      error = String(e);
    }
  }

  async function play(slot: number) {
    if (!ready || playing) return;
    error = "";
    playing = true;
    try {
      await invoke("play", { slot });
    } catch (e) {
      error = String(e);
      playing = false;
    }
  }

  onMount(() => {
    refresh();
    addEventListener("focus", refresh);
    const ended = listen<string | null>("session-ended", (e) => {
      playing = false;
      error = e.payload ?? "";
      refresh();
    });
    return () => {
      removeEventListener("focus", refresh);
      ended.then((stop) => stop());
    };
  });
</script>

<main class="title-screen launcher">
  <img class="crest" src={logo} alt="" width="512" height="512">
  <h1><small><span class="hk">Hollow Knight</span><span class="x">×</span><span class="ss">Silksong</span></small>Decrypted</h1>
  <svg class="rule" viewBox="0 0 240 20" aria-hidden="true">
    <g fill="none"><path d="M0 10h96" stroke="#a9c8f0" opacity=".55" /><path d="M144 10h96" stroke="#d0463c" opacity=".7" /><path d="M96 10c6-7 14-7 18 0-4 7-12 7-18 0Z" stroke="#a9c8f0" /><path d="M144 10c-6-7-14-7-18 0 4 7 12 7 18 0Z" stroke="#d0463c" /></g>
  </svg>

  {#if status}
    <ul class="menu" role="list">
      {#each status.slots as slot (slot.id)}
        <li>
          <button disabled={!ready || playing} onclick={() => play(slot.id)}>
            <span>Run {slot.id}</span>
            <small>{slot.current ? place(slot.current) : "New run"}{slot.room ? ` · ${slot.room.slot} @ ${slot.room.host}` : " · offline"}</small>
          </button>
          <button class="more" disabled={playing} aria-expanded={editing === slot.id} onclick={() => edit(slot)}>Settings</button>
        </li>
        {#if editing === slot.id}
          <li class="panel">
            <form onsubmit={(e) => { e.preventDefault(); saveRoom(slot.id); }}>
              <label>Host <input bind:value={draft.host} placeholder="archipelago.gg" required></label>
              <label>Port <input type="number" min="1" max="65535" bind:value={draft.port} required></label>
              <label>Slot <input bind:value={draft.slot} placeholder="Your slot name" required></label>
              <label>Password <input type="password" bind:value={draft.password} autocomplete="off"></label>
              <label class="check"><input type="checkbox" bind:checked={draft.auto_connect}> Connect when the game starts</label>
              <div class="actions">
                <button type="submit">Save room</button>
                {#if slot.room}<button type="button" onclick={() => clearRoom(slot.id)}>Play offline</button>{/if}
                <button type="button" onclick={() => exportReport(slot.id)}>Bug report</button>
                {#if slot.current}<button type="button" class="danger" onclick={() => deleteRun(slot.id)}>Delete run</button>{/if}
              </div>
            </form>
          </li>
        {/if}
      {/each}
    </ul>
    <ul class="games" role="list">
      {#each status.games as game (game.key)}
        <li class={game.key}>
          {game.name}
          {#if !game.build}<em>not found in Steam</em>
          {:else if !game.tested}<em>untested build {game.build}</em>{/if}
        </li>
      {/each}
    </ul>
    {#if !status.steam_running}<p class="notice">Start Steam to play.</p>{/if}
    {#if update}<p class="notice">Version {update} is out. <button class="link" onclick={() => invoke("open_releases")}>Get it</button></p>{/if}
    <p class="footer">v{status.version} · <button class="link" disabled={playing} onclick={removeGameFolders}>Remove modded game folders</button></p>
  {/if}
  {#if message}<p class="notice" role="status">{message}</p>{/if}
  {#if error}<p class="notice error" role="alert">{error}</p>{/if}
</main>
