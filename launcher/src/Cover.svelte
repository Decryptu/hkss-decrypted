<script lang="ts">
  import { listen } from "@tauri-apps/api/event";
  import { onMount } from "svelte";

  let to = $state("hollow_knight");
  let shown = $state(true);

  onMount(() => {
    const raised = listen<string>("cover-raise", (e) => {
      to = e.payload;
      shown = true;
    });
    const lowered = listen("cover-lower", () => (shown = false));
    return () => {
      raised.then((stop) => stop());
      lowered.then((stop) => stop());
    };
  });
</script>

<div class="cover {to}" class:shown>
  <p>{to === "silksong" ? "Pharloom" : "Hallownest"}</p>
  <i></i>
</div>
