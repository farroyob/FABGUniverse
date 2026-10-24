// The shared backdrop: crossfading key art with slow drift, pointer parallax and a small dust field.

import { local } from "./storage.js";

const atmos = document.querySelector(".atmos");
const layers = [...document.querySelectorAll(".atmos__bg")];
const canvas = document.querySelector(".atmos__dust");
const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
const COVERS = ["chess", "checkers"];

let front = -1;
let currentCover = null;

export function isLite() {
  return local.get("fab.lite", false) === true;
}

export function setLite(value) {
  local.set("fab.lite", Boolean(value));
  applyLite();
}

export function applyLite() {
  document.documentElement.toggleAttribute("data-lite", isLite());
  syncDust();
}

/** Crossfades the backdrop to a game's key art (or fades it out when `cover` is null). */
export function setBackdrop(cover) {
  if (!atmos || cover === currentCover) return;
  currentCover = cover;
  const previous = layers[front];
  if (!cover || !COVERS.includes(cover)) {
    previous?.classList.remove("is-on");
    front = -1;
    return;
  }
  const next = layers[(front + 1) % layers.length] ?? layers[0];
  for (const name of COVERS) next.classList.toggle(`atmos__bg--${name}`, name === cover);
  // Two frames so the new image is laid out before the opacity transition starts.
  requestAnimationFrame(() => requestAnimationFrame(() => {
    next.classList.add("is-on");
    if (previous && previous !== next) previous.classList.remove("is-on");
  }));
  front = layers.indexOf(next);
}

// ── Parallax ────────────────────────────────────────────────

window.addEventListener("pointermove", (e) => {
  if (!atmos || e.pointerType !== "mouse" || reduced.matches || isLite()) return;
  atmos.style.setProperty("--px", ((e.clientX / innerWidth) * 2 - 1).toFixed(3));
  atmos.style.setProperty("--py", ((e.clientY / innerHeight) * 2 - 1).toFixed(3));
}, { passive: true });

// ── Dust ────────────────────────────────────────────────────

const ctx = canvas?.getContext("2d");
const PARTICLES = 36;
let particles = [];
let raf = 0;
let last = 0;

function seed() {
  particles = Array.from({ length: PARTICLES }, () => ({
    x: Math.random(),
    y: Math.random(),
    r: 0.6 + Math.random() * 1.6,
    vx: (Math.random() - 0.5) * 0.004,
    vy: -0.002 - Math.random() * 0.006,
    a: 0.15 + Math.random() * 0.55,
    warm: Math.random() < 0.18,
  }));
}

function frame(now) {
  raf = requestAnimationFrame(frame);
  if (now - last < 33) return; // ~30 fps is plenty for dust
  const dt = Math.min(0.1, (now - last) / 1000);
  last = now;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  for (const p of particles) {
    p.x += p.vx * dt * 20;
    p.y += p.vy * dt * 20;
    if (p.y < -0.02) { p.y = 1.02; p.x = Math.random(); }
    if (p.x < -0.02) p.x = 1.02;
    if (p.x > 1.02) p.x = -0.02;
    ctx.globalAlpha = p.a;
    ctx.fillStyle = p.warm ? "#e8a15c" : "#9dbeff";
    ctx.beginPath();
    ctx.arc(p.x * canvas.width, p.y * canvas.height, p.r, 0, Math.PI * 2);
    ctx.fill();
  }
}

function syncDust() {
  const on = ctx && !reduced.matches && !isLite() && !document.hidden;
  if (on && !raf) {
    if (particles.length === 0) seed();
    raf = requestAnimationFrame(frame);
  } else if (!on && raf) {
    cancelAnimationFrame(raf);
    raf = 0;
  }
}

document.addEventListener("visibilitychange", syncDust);
reduced.addEventListener?.("change", syncDust);
applyLite();
