import { svelte } from "@sveltejs/vite-plugin-svelte";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [svelte()],
  clearScreen: false,
  // The launcher reuses the hub's stylesheet, backdrop and logo from ../site.
  server: { port: 1420, strictPort: true, fs: { allow: [".."] }, watch: { ignored: ["**/src-tauri/**"] } },
  build: { target: "chrome110" },
});
