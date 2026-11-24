import "@fontsource/cinzel/400.css";
import "@fontsource/cinzel/600.css";
import "@fontsource/eb-garamond/400.css";
import "@fontsource/eb-garamond/400-italic.css";
import "../../site/style.css";
import "./launcher.css";
import "../../site/embers.js";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { mount } from "svelte";
import Cover from "./Cover.svelte";
import Menu from "./Menu.svelte";

const target = document.getElementById("app")!;
mount(getCurrentWindow().label === "cover" ? Cover : Menu, { target });
