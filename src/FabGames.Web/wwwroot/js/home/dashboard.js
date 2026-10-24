// The dashboard: game tiles with spring focus, backdrop crossfade, and the create / join / how-to dialogs.

import { t, culture } from "../core/i18n.js";
import { play, isMuted, setMuted } from "../core/audio.js";
import { setBackdrop, isLite, setLite } from "../core/atmos.js";
import { local } from "../core/storage.js";
import { openDialog, segmented } from "../core/ui.js";
import { avatarUrl } from "../core/profile.js";

const tiles = [...document.querySelectorAll(".tile")];
const tilesBox = document.getElementById("tiles");
const info = document.getElementById("game-info");
const title = document.getElementById("game-title");
const desc = document.getElementById("game-desc");
const players = document.getElementById("game-players");
const btnCreate = document.getElementById("btn-create");
const btnJoin = document.getElementById("btn-join");
const btnHow = document.getElementById("btn-how");
const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

let active = null;

export function initDashboard({ onSwitchProfile }) {
  for (const tile of tiles) {
    tile.addEventListener("pointerenter", (e) => { if (e.pointerType === "mouse") activate(tile); });
    tile.addEventListener("focus", () => activate(tile));
    tile.addEventListener("click", () => {
      if (tile.dataset.game === "soon") return;
      if (active === tile && tile.dataset.game) {
        play("select");
        openDialog(document.getElementById("dlg-create"), tile);
      } else {
        activate(tile);
      }
    });
    wireTilt(tile);
  }

  btnCreate.addEventListener("click", () => { play("select"); openDialog(document.getElementById("dlg-create"), btnCreate); });
  btnJoin.addEventListener("click", () => { play("select"); openDialog(document.getElementById("dlg-join"), btnJoin); });
  btnHow.addEventListener("click", () => { play("select"); openHow(); });
  document.getElementById("profile-chip").addEventListener("click", () => { play("back"); onSwitchProfile(); });
  document.getElementById("open-settings").addEventListener("click", (e) => {
    play("select");
    syncSettings();
    openDialog(document.getElementById("dlg-settings"), e.currentTarget);
  });

  initCreate();
  initJoin();
  initSettings();
  startClock();
}

export function showProfile(profile) {
  document.getElementById("profile-chip-avatar").src = avatarUrl(profile.avatar);
  document.getElementById("profile-chip-name").textContent = profile.name;
}

export function enter() {
  const last = local.get("fab.lastGame", "chess");
  const tile = tiles.find((el) => el.dataset.game === last) ?? tiles[0];
  activate(tile, { silent: true });
  tile.focus({ preventScroll: true });
}

export function leave() {
  setBackdrop(null);
}

function activate(tile, { silent = false } = {}) {
  if (!tile || active === tile) return;
  active = tile;
  for (const el of tiles) el.classList.toggle("is-active", el === tile);
  tilesBox.classList.add("has-active");

  const game = tile.dataset.game;
  const playable = game !== "soon";
  setBackdrop(playable ? tile.dataset.cover : null);
  if (playable) local.set("fab.lastGame", game);
  if (!silent) play("tick");

  title.textContent = tile.dataset.title;
  desc.textContent = tile.dataset.desc;
  const count = Number(tile.dataset.players);
  players.textContent = count ? t("home.players", { n: count }) : "—";
  players.hidden = !count;
  for (const button of [btnCreate, btnJoin, btnHow]) button.disabled = !playable;

  if (!reduced.matches) {
    info.classList.remove("is-swapping");
    void info.offsetWidth; // restart the CSS animation
    info.classList.add("is-swapping");
  }
}

export const activeGame = () => (active?.dataset.game !== "soon" ? active?.dataset.game : null) ?? "chess";

// ── Pointer tilt (mouse only; off for reduced motion and lite) ───────

function wireTilt(tile) {
  tile.addEventListener("pointermove", (e) => {
    if (e.pointerType !== "mouse" || reduced.matches || isLite() || active !== tile) return;
    const r = tile.getBoundingClientRect();
    tile.style.setProperty("--tx", (((e.clientX - r.left) / r.width) * 2 - 1).toFixed(3));
    tile.style.setProperty("--ty", (((e.clientY - r.top) / r.height) * 2 - 1).toFixed(3));
  });
  tile.addEventListener("pointerleave", () => {
    tile.style.setProperty("--tx", "0");
    tile.style.setProperty("--ty", "0");
  });
}

// ── Clock ───────────────────────────────────────────────────

function startClock() {
  const el = document.getElementById("clock");
  const format = new Intl.DateTimeFormat(culture, { hour: "2-digit", minute: "2-digit" });
  const tick = () => { el.textContent = format.format(new Date()); };
  tick();
  setInterval(tick, 10_000);
}

// ── Dialogs ─────────────────────────────────────────────────

function initCreate() {
  const dialog = document.getElementById("dlg-create");
  const incrementField = document.getElementById("increment-field");
  const time = segmented(document.getElementById("time-options"), (item) => {
    incrementField.hidden = item.dataset.minutes === "0";
    play("tick");
  });
  const increment = segmented(document.getElementById("increment-options"), () => play("tick"));

  dialog.addEventListener("close", () => {
    time.select(document.querySelector("#time-options [data-minutes='0']"));
    increment.select(document.querySelector("#increment-options [data-seconds='0']"));
  });

  document.getElementById("create-form").addEventListener("submit", (e) => {
    e.preventDefault();
    const minutes = Number(time.get()?.dataset.minutes ?? 0);
    const seconds = minutes ? Number(increment.get()?.dataset.seconds ?? 0) : 0;
    play("select");
    location.href = `/${culture}/play/new?game=${encodeURIComponent(activeGame())}&tc=${minutes}-${seconds}`;
  });
}

function initJoin() {
  const input = document.getElementById("join-code");
  const errorEl = document.getElementById("join-error");
  input.addEventListener("input", () => {
    input.value = input.value.toUpperCase().replace(/[^A-Z0-9]/g, "");
    errorEl.hidden = true;
  });
  document.getElementById("dlg-join").addEventListener("close", () => { input.value = ""; errorEl.hidden = true; });
  document.getElementById("join-form").addEventListener("submit", (e) => {
    e.preventDefault();
    const code = input.value.trim().toUpperCase();
    if (!/^[A-HJKMNP-Z2-9]{6}$/.test(code)) {
      errorEl.hidden = false;
      play("error");
      input.focus();
      return;
    }
    play("select");
    location.href = `/${culture}/play/${code}`;
  });
}

function openHow() {
  const game = activeGame();
  document.getElementById("how-game").textContent = t(`home.${game}.title`);
  const lines = [];
  for (let i = 1; t(`how.${game}.${i}`) !== `how.${game}.${i}`; i++) {
    const li = document.createElement("li");
    li.textContent = t(`how.${game}.${i}`);
    lines.push(li);
  }
  document.getElementById("how-list").replaceChildren(...lines);
  openDialog(document.getElementById("dlg-how"), btnHow);
}

function initSettings() {
  const sound = document.getElementById("setting-sound");
  const lite = document.getElementById("setting-lite");
  sound.addEventListener("click", () => { setMuted(!isMuted()); syncSettings(); play("select"); });
  lite.addEventListener("click", () => { setLite(!isLite()); syncSettings(); play("select"); });
}

function syncSettings() {
  document.getElementById("setting-sound").setAttribute("aria-checked", String(!isMuted()));
  document.getElementById("setting-lite").setAttribute("aria-checked", String(isLite()));
}
