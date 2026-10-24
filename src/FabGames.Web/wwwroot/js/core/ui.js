// Small shared UI helpers: toasts, screen-reader announcements and native <dialog> wiring.

import { t } from "./i18n.js";
import { play } from "./audio.js";

const toasts = document.getElementById("toasts");
const live = document.getElementById("sr-live");

export function toast(text, { type = "info", icon = null, sub = null, ms = 3800 } = {}) {
  if (!toasts) return;
  const el = document.createElement("div");
  el.className = `toast toast--${type}`;
  if (icon) {
    el.innerHTML = `<svg class="icon" aria-hidden="true"><use href="/img/icons.svg#i-${icon}"></use></svg>`;
  }
  const body = document.createElement("div");
  body.className = "toast__text";
  body.textContent = text; // always text, never HTML
  if (sub) {
    const small = document.createElement("small");
    small.textContent = sub;
    body.append(small);
  }
  el.append(body);
  toasts.append(el);
  while (toasts.children.length > 3) toasts.firstElementChild.remove();
  setTimeout(() => {
    el.classList.add("is-leaving");
    el.addEventListener("animationend", () => el.remove(), { once: true });
    setTimeout(() => el.remove(), 400);
  }, ms);
}

export function error(code) {
  play("error");
  toast(t(`err.${code}`) === `err.${code}` ? t("err.UNKNOWN") : t(`err.${code}`), { type: "error" });
}

/** Politely announces text to screen readers (moves, check, opponent events). */
export function announce(text) {
  if (!live) return;
  live.textContent = "";
  // A tick later so repeated identical messages are announced again.
  setTimeout(() => { live.textContent = text; }, 30);
}

/** Opens a native dialog and returns focus to the opener when it closes. */
export function openDialog(dialog, opener = document.activeElement) {
  if (!dialog || dialog.open) return;
  dialog.showModal();
  dialog.addEventListener("close", () => {
    if (opener instanceof HTMLElement && document.contains(opener)) opener.focus();
  }, { once: true });
  const first = dialog.querySelector("[autofocus], input, [data-nav]");
  first?.focus();
}

/** Wires [data-close] buttons and backdrop clicks for every dialog on the page. */
export function wireDialogs() {
  for (const dialog of document.querySelectorAll("dialog")) {
    dialog.addEventListener("click", (e) => {
      if (e.target === dialog) dialog.close();
      else if (e.target instanceof Element && e.target.closest("[data-close]")) dialog.close();
    });
  }
}

/** Radio-style group of buttons: returns a getter for the selected element. */
export function segmented(group, onChange) {
  const items = [...group.querySelectorAll("[role=radio]")];
  const select = (item) => {
    for (const el of items) el.setAttribute("aria-checked", String(el === item));
    onChange?.(item);
  };
  for (const item of items) item.addEventListener("click", () => select(item));
  return { select, get: () => items.find((el) => el.getAttribute("aria-checked") === "true") };
}

export function formatClock(ms) {
  const total = Math.max(0, ms);
  const seconds = Math.floor(total / 1000);
  const mm = String(Math.floor(seconds / 60)).padStart(2, "0");
  const ss = String(seconds % 60).padStart(2, "0");
  return total < 10_000 ? `${mm}:${ss}.${Math.floor((total % 1000) / 100)}` : `${mm}:${ss}`;
}
