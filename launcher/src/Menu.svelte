<script lang="ts">
  import { invoke } from "@tauri-apps/api/core";
  import { listen } from "@tauri-apps/api/event";
  import { onMount } from "svelte";
  import logo from "../../site/assets/logo.webp";

  type Issue = { severity: "error" | "warning"; code: string; summary: string; detail: string };
  type Game = { key: string; name: string; build: string | null; tested: boolean; unity_version: string | null; issues: Issue[] };
  type Room = { host: string; port: number; slot: string; password: string; auto_connect: boolean };
  type Stats = { play_seconds: number; deaths: number; kills: number; completion_percent: number; checks_found: number; checks_total: number };
  type Slot = {
    id: number;
    current: string | null;
    room: Room | null;
    hk_goal_completed: boolean;
    ss_goal_completed: boolean;
    hk_stats: Stats;
    ss_stats: Stats;
    error: string | null;
  };
  type Status = { games: Game[]; slots: Slot[]; steam_running: boolean; version: string };
  const blankRoom = (): Room => ({ host: "archipelago.gg", port: 38281, slot: "", password: "", auto_connect: true });

  let status = $state<Status | null>(null);
  let error = $state("");
  let playing = $state(false);
  let editing = $state<number | null>(null);
  let draft = $state<Room>(blankRoom());
  let message = $state("");
  type UpdateInfo = {
    version: string;
    tag: string;
    asset_name: string;
    download_url: string;
    sha256: string | null;
    size: number;
    release_url: string;
  };
  type UpdateProgress = { stage: "downloading" | "verifying" | "installing"; downloaded: number; total: number | null };
  let update = $state<UpdateInfo | null>(null);
  let checkingUpdate = $state(false);
  let updateNotice = $state("");
  let updating = $state(false);
  let updateProgress = $state<UpdateProgress | null>(null);
  type CheckResult = { world_version: string | null; version_warning: string | null };
  let testing = $state(false);
  let connection = $state<{ text: string; kind: "ok" | "warn" | "error" } | null>(null);
  let activeRun = $state<number | null>(null);
  let failedRun = $state<number | null>(null);
  let showSettings = $state(false);
  const busy = $derived(playing || testing || updating);

  function edit(slot: Slot) {
    showSettings = false;
    error = message = "";
    connection = null;
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
  async function testConnection(form: HTMLFormElement | null) {
    if (testing || !form?.reportValidity()) return;
    testing = true;
    connection = { text: "Checking room…", kind: "ok" };
    try {
      const result = await invoke<CheckResult>("test_connection", { room: { ...draft, port: Number(draft.port) } });
      if (result.version_warning) {
        connection = {
          text: `The room accepted your slot. Warning: ${result.version_warning}`,
          kind: "warn",
        };
      } else {
        const version = result.world_version ? ` (apworld v${result.world_version})` : "";
        connection = {
          text: `The room accepted your slot${version}. Save room to use these settings.`,
          kind: "ok",
        };
      }
    } catch (e) {
      connection = { text: String(e), kind: "error" };
    } finally {
      testing = false;
    }
  }
  function deleteRun(id: number) {
    if (confirm(`Delete run ${id}? Both saves and its room settings are removed for good.`))
      act("delete_run", { slot: id }, `Run ${id} deleted.`);
  }
  function removeGameFolders() {
    if (confirm("Remove the modded game folders? Your Steam games and runs are not touched; the folders are rebuilt on the next run."))
      act("remove_game_folders", {}, "Modded game folders removed.");
  }

  const formatMb = (bytes: number) => (bytes / (1024 * 1024)).toFixed(1);
  const updatePercent = $derived.by(() => {
    const total = updateProgress?.total ?? update?.size ?? 0;
    if (!updateProgress || total <= 0) return 0;
    return Math.min(100, Math.round((updateProgress.downloaded / total) * 100));
  });

  async function checkUpdate(manual = false) {
    if (checkingUpdate || updating) return;
    if (manual) {
      checkingUpdate = true;
      updateNotice = "";
    }
    try {
      const found = await invoke<UpdateInfo | null>("check_update");
      update = found;
      if (manual) {
        updateNotice = found
          ? `Update available: v${found.version}.`
          : `Up to date (v${status?.version ?? ""}).`;
      }
    } catch (e) {
      if (manual) updateNotice = String(e);
    } finally {
      if (manual) checkingUpdate = false;
    }
  }

  async function installUpdate() {
    if (!update || busy) return;
    error = message = updateNotice = "";
    editing = null;
    showSettings = false;
    updating = true;
    updateProgress = { stage: "downloading", downloaded: 0, total: update.size };
    try {
      await invoke("install_update", { info: update });
    } catch (e) {
      error = String(e);
      updating = false;
      updateProgress = null;
    }
  }

  const gameIssues = $derived(
    status ? status.games.flatMap((g) => g.issues.map((i) => ({ ...i, game: g.key }))) : [],
  );
  const hasBlockingIssue = $derived(gameIssues.some((i) => i.severity === "error"));
  const ready = $derived(!!status && status.steam_running && status.games.every((g) => g.build) && !hasBlockingIssue);
  const place = (key: string) => (key === "silksong" ? "Pharloom" : "Hallownest");
  const goalBadge = (slot: Slot) =>
    slot.hk_goal_completed && slot.ss_goal_completed
      ? " · Both goals done"
      : slot.hk_goal_completed
        ? " · HK goal done"
        : slot.ss_goal_completed
          ? " · SS goal done"
          : "";

  const played = (s: Stats) => s.play_seconds > 0 || s.deaths > 0 || s.kills > 0 || s.checks_found > 0;
  const duration = (seconds: number) => {
    const t = Math.max(0, Math.floor(seconds));
    return `${Math.floor(t / 3600)}:${String(Math.floor(t / 60) % 60).padStart(2, "0")}:${String(t % 60).padStart(2, "0")}`;
  };
  const checks = (found: number, total: number) => (total > 0 ? `${found} / ${total}` : `${found}`);
  const goalText = (done: boolean, s: Stats) => (done ? "Done" : played(s) ? "Not yet" : "Not started");

  async function refresh() {
    try {
      const first = !status;
      status = await invoke<Status>("status");
      if (first) checkUpdate(false);
    } catch (e) {
      error = String(e);
    }
  }

  async function play(slot: number) {
    if (!ready || busy) return;
    error = message = "";
    failedRun = null;
    activeRun = slot;
    editing = null;
    showSettings = false;
    playing = true;
    try {
      await invoke("play", { slot });
    } catch (e) {
      error = String(e);
      failedRun = slot;
      playing = false;
    }
  }

  onMount(() => {
    refresh();
    addEventListener("focus", refresh);
    const ended = listen<string | null>("session-ended", (e) => {
      playing = false;
      error = e.payload ?? "";
      failedRun = e.payload ? activeRun : null;
      activeRun = null;
      refresh();
    });
    const progress = listen<UpdateProgress>("update-progress", (e) => {
      updateProgress = e.payload;
    });
    return () => {
      removeEventListener("focus", refresh);
      ended.then((stop) => stop());
      progress.then((stop) => stop());
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
    <div class="launcher-settings">
      <button type="button" class="settings-cog" title="Launcher settings" aria-label="Launcher settings"
        aria-expanded={showSettings} aria-controls="launcher-settings" disabled={busy}
        onclick={() => { showSettings = !showSettings; }}>
        <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
          <path stroke-linejoin="round" d="M10 3h4l.5 2.5 1.6.9 2.4-.8 2 3.4-1.9 1.7v1.8l1.9 1.7-2 3.4-2.4-.8-1.6.9L14 20h-4l-.5-2.3-1.6-.9-2.4.8-2-3.4 1.9-1.7v-1.8L3.5 9l2-3.4 2.4.8 1.6-.9z"/>
          <circle cx="12" cy="11.5" r="3"/>
        </svg>
      </button>
      <div id="launcher-settings" class="settings-panel" hidden={!showSettings}>
        <h2>Launcher settings</h2>
        <div class="settings-section">
          <p>Check GitHub Releases for a newer launcher and mod payload.</p>
          <div class="actions">
            <button type="button" class="secondary" disabled={busy || checkingUpdate} onclick={() => checkUpdate(true)}>
              {checkingUpdate ? "Checking…" : "Check for updates"}
            </button>
          </div>
          {#if updateNotice}<p class="update-check-note" role="status">{updateNotice}</p>{/if}
        </div>
        <div class="settings-section">
          <p>Free up disk space. The launcher rebuilds these folders the next time you play.</p>
          <div class="actions">
            <button type="button" disabled={busy} onclick={removeGameFolders}>Remove modded game folders</button>
          </div>
        </div>
      </div>
    </div>
    <ul class="menu" role="list">
      {#each status.slots as slot (slot.id)}
        <li>
          <button disabled={!ready || busy || !!slot.error} onclick={() => play(slot.id)}>
            <span>Run {slot.id}</span>
            <small>{slot.error ? `Corrupted data (${slot.error})` : `${slot.current ? place(slot.current) : "New run"}${slot.room ? ` · ${slot.room.slot} @ ${slot.room.host}` : " · offline"}${goalBadge(slot)}`}</small>
          </button>
          <button class="more" disabled={busy} aria-expanded={editing === slot.id} onclick={() => edit(slot)}>Settings</button>
        </li>
        {#if editing === slot.id}
          <li class="panel">
            <form oninput={() => { if (!testing) connection = null; }} onsubmit={(e) => { e.preventDefault(); if (!testing) saveRoom(slot.id); }}>
              <fieldset disabled={testing}>
              <label>Host <input bind:value={draft.host} placeholder="archipelago.gg" required></label>
              <label>Port <input type="number" min="1" max="65535" bind:value={draft.port} required></label>
              <label>Slot <input bind:value={draft.slot} placeholder="Your slot name" required></label>
              <label>Password <input type="password" bind:value={draft.password} autocomplete="off"></label>
              <label class="check"><input type="checkbox" bind:checked={draft.auto_connect}> Connect when the game starts</label>
              <div class="actions">
                <button type="submit">Save room</button>
                <button type="button" onclick={(e) => testConnection(e.currentTarget.form)}>Test connection</button>
                {#if slot.room || slot.error}<button type="button" onclick={() => clearRoom(slot.id)}>Play offline</button>{/if}
                <button type="button" onclick={() => exportReport(slot.id)}>Bug report</button>
                {#if slot.current || slot.error}<button type="button" class="danger" onclick={() => deleteRun(slot.id)}>Delete run</button>{/if}
              </div>
              </fieldset>
              {#if connection}<p class:error={connection.kind === "error"} class:warn={connection.kind === "warn"} role="status">{connection.text}</p>{/if}
            </form>
            {#if played(slot.hk_stats) || played(slot.ss_stats)}
              {@const hk = slot.hk_stats}
              {@const ss = slot.ss_stats}
              <table class="stats">
                <caption>{slot.hk_goal_completed && slot.ss_goal_completed ? "Run complete" : "Run so far"}</caption>
                <thead><tr><th></th><th>Hallownest</th><th>Pharloom</th><th>Total</th></tr></thead>
                <tbody>
                  <tr><th>Goal</th><td>{goalText(slot.hk_goal_completed, hk)}</td><td>{goalText(slot.ss_goal_completed, ss)}</td><td></td></tr>
                  <tr><th>Play time</th><td>{duration(hk.play_seconds)}</td><td>{duration(ss.play_seconds)}</td><td>{duration(hk.play_seconds + ss.play_seconds)}</td></tr>
                  <tr><th>Completion</th><td>{Math.round(hk.completion_percent)}%</td><td>{Math.round(ss.completion_percent)}%</td><td></td></tr>
                  <tr><th>Checks</th><td>{checks(hk.checks_found, hk.checks_total)}</td><td>{checks(ss.checks_found, ss.checks_total)}</td><td>{checks(hk.checks_found + ss.checks_found, hk.checks_total + ss.checks_total)}</td></tr>
                  <tr><th>Deaths</th><td>{hk.deaths}</td><td>{ss.deaths}</td><td>{hk.deaths + ss.deaths}</td></tr>
                  <tr><th>Kills</th><td>{hk.kills}</td><td>{ss.kills}</td><td>{hk.kills + ss.kills}</td></tr>
                </tbody>
              </table>
            {/if}
          </li>
        {/if}
      {/each}
    </ul>
    <ul class="games" role="list">
      {#each status.games as game (game.key)}
        {@const firstIssue = game.issues[0]}
        <li class={game.key}>
          {game.name}
          {#if firstIssue}<em class:error={firstIssue.severity === "error"}>{firstIssue.summary}</em>{/if}
        </li>
      {/each}
    </ul>
    {#if !status.steam_running}<p class="notice error">Steam is not running. Start Steam and sign in to play.</p>{/if}
    {#if gameIssues.length > 0}
      <ul class="preflight" role="list">
        {#each gameIssues as issue (`${issue.game}:${issue.code}:${issue.summary}`)}
          <li class="notice" class:error={issue.severity === "error"} role={issue.severity === "error" ? "alert" : "status"}>
            {issue.detail}
          </li>
        {/each}
      </ul>
    {/if}
    {#if !status.steam_running || hasBlockingIssue}
      <div class="actions">
        <button type="button" disabled={busy} onclick={refresh}>Recheck</button>
      </div>
    {/if}
    {#if update}
      <div class="notice update" role="status">
        {#if updating}
          {#if updateProgress?.stage === "verifying"}
            Verifying v{update.version} checksum…
          {:else if updateProgress?.stage === "installing"}
            Starting v{update.version} installer…
          {:else}
            Downloading v{update.version}… {formatMb(updateProgress?.downloaded ?? 0)} / {formatMb(updateProgress?.total ?? update.size)} MB ({updatePercent}%)
            <div class="update-bar" aria-hidden="true"><div style="width: {updatePercent}%"></div></div>
          {/if}
        {:else}
          Update available: v{update.version} (current: v{status.version}, {formatMb(update.size)} MB).
          {#if update.sha256}
            <button class="link" disabled={busy} onclick={installUpdate}>Install update</button> ·
          {/if}
          <button class="link" onclick={() => invoke("open_releases")}>Release page</button>
        {/if}
      </div>
    {/if}
    <p class="footer">Mods &amp; Archipelago are bundled automatically · Steam installs stay vanilla · v{status.version}</p>
  {/if}
  {#if message}<p class="notice" role="status">{message}</p>{/if}
  {#if error}<p class="notice error" role="alert">{error}</p>{/if}
  {#if failedRun !== null}
    {@const id = failedRun}
    <div class="actions">
      <button disabled={!ready || busy} onclick={() => play(id)}>Retry run {id}</button>
      <button disabled={busy} onclick={() => exportReport(id)}>Export bug report</button>
    </div>
  {/if}
</main>
