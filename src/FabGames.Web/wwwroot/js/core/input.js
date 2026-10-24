// One input model for mouse, touch, keyboard and gamepad. It tracks the last modality (so the focus ring
// follows it), moves focus spatially between [data-nav] elements and turns Esc / gamepad B into "back".

import { play } from "./audio.js";

export const bus = new EventTarget();

const root = document.documentElement;
let modality = "mouse";

function setModality(next) {
  if (modality === next) return;
  modality = next;
  root.dataset.input = next;
}

export const currentModality = () => modality;

// ── Modality tracking ───────────────────────────────────────

window.addEventListener("pointerdown", (e) => setModality(e.pointerType === "touch" ? "touch" : "mouse"), true);
window.addEventListener("pointermove", (e) => {
  if (e.pointerType === "mouse" && (e.movementX || e.movementY)) setModality("mouse");
}, true);
window.addEventListener("keydown", (e) => {
  if (!["Shift", "Control", "Alt", "Meta"].includes(e.key)) setModality("keyboard");
}, true);

// ── Spatial navigation ──────────────────────────────────────

const DIRS = { ArrowLeft: "left", ArrowRight: "right", ArrowUp: "up", ArrowDown: "down" };

function scopeRoot() {
  const dialog = document.querySelector("dialog[open]");
  if (dialog) return dialog;
  const roots = [...document.querySelectorAll("[data-nav-root]")].filter((el) => !el.hidden);
  return roots.at(-1) ?? document.body;
}

function candidates(scope) {
  return [...scope.querySelectorAll("[data-nav]")].filter((el) => {
    if (el.disabled || el.hidden || el.closest("[hidden]") || el.getAttribute("aria-disabled") === "true") return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  });
}

const center = (el) => {
  const r = el.getBoundingClientRect();
  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
};

/** Moves focus one step in a direction. Returns the newly focused element, if any. */
export function move(dir) {
  const scope = scopeRoot();
  const list = candidates(scope);
  if (list.length === 0) return null;

  const current = scope.contains(document.activeElement) && list.includes(document.activeElement)
    ? document.activeElement
    : null;
  if (!current) {
    list[0].focus();
    return list[0];
  }

  const from = center(current);
  let best = null;
  let bestScore = Infinity;
  for (const el of list) {
    if (el === current) continue;
    const to = center(el);
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const forward = dir === "right" ? dx : dir === "left" ? -dx : dir === "down" ? dy : -dy;
    if (forward <= 4) continue;
    const lateral = dir === "left" || dir === "right" ? Math.abs(dy) : Math.abs(dx);
    const score = forward + lateral * 2.2;
    if (score < bestScore) {
      bestScore = score;
      best = el;
    }
  }

  if (best) {
    best.focus({ preventScroll: false });
    best.scrollIntoView?.({ block: "nearest", inline: "center" });
    play("tick");
  }
  return best;
}

export function focusFirst(scope = scopeRoot()) {
  const first = candidates(scope)[0];
  first?.focus({ preventScroll: true });
  return first ?? null;
}

const isTyping = (el) =>
  el instanceof HTMLInputElement && !["button", "checkbox", "radio"].includes(el.type) || el instanceof HTMLTextAreaElement;

window.addEventListener("keydown", (e) => {
  if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return;
  if (e.target instanceof Element && e.target.closest("[data-own-keys]")) return;

  if (e.key in DIRS && !isTyping(e.target)) {
    if (move(DIRS[e.key])) e.preventDefault();
    return;
  }

  if (e.key === "Escape" && !document.querySelector("dialog[open]")) {
    bus.dispatchEvent(new CustomEvent("back"));
  }
});

// ── Gamepad ─────────────────────────────────────────────────

const REPEAT_MS = 220;
const lastFire = new Map();
const held = new Set();
let pollHandle = 0;

function fire(key, fn) {
  const now = performance.now();
  if (now - (lastFire.get(key) ?? 0) < REPEAT_MS) return;
  lastFire.set(key, now);
  fn();
}

function edge(key, pressed, fn) {
  if (pressed && !held.has(key)) {
    held.add(key);
    fn();
  } else if (!pressed) {
    held.delete(key);
  }
}

function poll() {
  const pads = navigator.getGamepads?.() ?? [];
  for (const pad of pads) {
    if (!pad) continue;
    const b = (i) => pad.buttons[i]?.pressed;
    const [lx = 0, ly = 0] = pad.axes;

    const dir =
      b(14) || lx < -0.6 ? "left"
      : b(15) || lx > 0.6 ? "right"
      : b(12) || ly < -0.6 ? "up"
      : b(13) || ly > 0.6 ? "down"
      : null;
    if (dir) {
      setModality("gamepad");
      fire(dir, () => {
        const own = document.activeElement?.closest?.("[data-own-keys]");
        if (own) own.dispatchEvent(new CustomEvent("gamepad-dir", { detail: dir }));
        else move(dir);
      });
    }

    edge("a", b(0), () => {
      setModality("gamepad");
      const target = document.activeElement;
      if (target?.closest?.("[data-own-keys]")) target.dispatchEvent(new CustomEvent("gamepad-select"));
      else if (target && target !== document.body) target.click();
      else focusFirst();
    });
    edge("b", b(1), () => {
      setModality("gamepad");
      const own = document.activeElement?.closest?.("[data-own-keys]");
      if (own) own.dispatchEvent(new CustomEvent("gamepad-cancel"));
      const dialog = document.querySelector("dialog[open]");
      if (dialog) dialog.close();
      else bus.dispatchEvent(new CustomEvent("back"));
    });
  }
  pollHandle = requestAnimationFrame(poll);
}

function startPolling() {
  if (!pollHandle && !document.hidden) pollHandle = requestAnimationFrame(poll);
}

window.addEventListener("gamepadconnected", startPolling);
window.addEventListener("gamepaddisconnected", () => {
  if (![...(navigator.getGamepads?.() ?? [])].some(Boolean)) {
    cancelAnimationFrame(pollHandle);
    pollHandle = 0;
  }
});
document.addEventListener("visibilitychange", () => {
  if (document.hidden) {
    cancelAnimationFrame(pollHandle);
    pollHandle = 0;
  } else if ([...(navigator.getGamepads?.() ?? [])].some(Boolean)) {
    startPolling();
  }
});
if ([...(navigator.getGamepads?.() ?? [])].some(Boolean)) startPolling();
