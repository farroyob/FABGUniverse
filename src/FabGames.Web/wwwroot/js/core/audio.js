// Every sound is synthesized with the Web Audio API: no audio files, no licensing. The context is only
// created after the first user gesture, and the mute choice is remembered.

import { local } from "./storage.js";

const MUTE_KEY = "fab.muted";

let ctx = null;
let master = null;
let noise = null;
let muted = local.get(MUTE_KEY, false) === true;
let pendingWelcome = false;

const listeners = new Set();

export const isMuted = () => muted;

export function onMuteChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function setMuted(value) {
  muted = Boolean(value);
  local.set(MUTE_KEY, muted);
  if (master) {
    master.gain.setTargetAtTime(muted ? 0 : 0.55, ctx.currentTime, 0.02);
  }
  listeners.forEach((fn) => fn(muted));
}

/** Creates or resumes the audio context. Call from inside a user gesture. */
export function unlock() {
  if (!ctx) {
    const Ctor = window.AudioContext ?? window.webkitAudioContext;
    if (!Ctor) return false;
    ctx = new Ctor();
    master = ctx.createGain();
    master.gain.value = muted ? 0 : 0.55;
    const soften = ctx.createBiquadFilter();
    soften.type = "lowpass";
    soften.frequency.value = 9000;
    master.connect(soften).connect(ctx.destination);

    const length = ctx.sampleRate * 0.4;
    noise = ctx.createBuffer(1, length, ctx.sampleRate);
    const data = noise.getChannelData(0);
    for (let i = 0; i < length; i++) data[i] = Math.random() * 2 - 1;
  }
  if (ctx.state === "suspended") ctx.resume();
  if (pendingWelcome) {
    pendingWelcome = false;
    play("chime");
  }
  return true;
}

for (const type of ["pointerdown", "keydown", "touchstart"]) {
  window.addEventListener(type, unlock, { once: false, passive: true, capture: true });
}

/** The boot chime is due at 2.2 s, usually before any gesture. Play it as soon as audio is allowed. */
export function playWhenUnlocked(name) {
  if (ctx && ctx.state === "running") play(name);
  else if (name === "chime") pendingWelcome = true;
}

function tone({ freq, to = freq, type = "sine", start = 0, attack = 0.005, hold = 0.05, release = 0.12, gain = 0.2 }) {
  const t0 = ctx.currentTime + start;
  const osc = ctx.createOscillator();
  const amp = ctx.createGain();
  osc.type = type;
  osc.frequency.setValueAtTime(freq, t0);
  if (to !== freq) osc.frequency.exponentialRampToValueAtTime(to, t0 + attack + hold + release);
  amp.gain.setValueAtTime(0.0001, t0);
  amp.gain.exponentialRampToValueAtTime(gain, t0 + attack);
  amp.gain.setValueAtTime(gain, t0 + attack + hold);
  amp.gain.exponentialRampToValueAtTime(0.0001, t0 + attack + hold + release);
  osc.connect(amp).connect(master);
  osc.start(t0);
  osc.stop(t0 + attack + hold + release + 0.05);
}

function burst({ start = 0, dur = 0.08, freq = 1800, q = 0.9, gain = 0.25, type = "bandpass" }) {
  const t0 = ctx.currentTime + start;
  const src = ctx.createBufferSource();
  src.buffer = noise;
  const filter = ctx.createBiquadFilter();
  filter.type = type;
  filter.frequency.value = freq;
  filter.Q.value = q;
  const amp = ctx.createGain();
  amp.gain.setValueAtTime(gain, t0);
  amp.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  src.connect(filter).connect(amp).connect(master);
  src.start(t0);
  src.stop(t0 + dur + 0.02);
}

const SOUNDS = {
  tick: () => tone({ freq: 2100, to: 1700, hold: 0.004, release: 0.04, gain: 0.05 }),
  select: () => {
    tone({ freq: 740, to: 988, type: "triangle", hold: 0.02, release: 0.09, gain: 0.12 });
  },
  back: () => tone({ freq: 620, to: 440, type: "triangle", hold: 0.01, release: 0.09, gain: 0.1 }),
  move: () => {
    tone({ freq: 220, to: 120, hold: 0.01, release: 0.12, gain: 0.3 });
    burst({ dur: 0.05, freq: 2600, gain: 0.12 });
  },
  capture: () => {
    tone({ freq: 160, to: 70, hold: 0.02, release: 0.22, gain: 0.34 });
    burst({ dur: 0.14, freq: 1400, q: 0.6, gain: 0.3 });
  },
  check: () => {
    tone({ freq: 880, hold: 0.06, release: 0.1, gain: 0.14, type: "triangle" });
    tone({ freq: 622, start: 0.12, hold: 0.08, release: 0.2, gain: 0.14, type: "triangle" });
  },
  error: () => tone({ freq: 150, to: 110, type: "sawtooth", hold: 0.05, release: 0.1, gain: 0.08 }),
  notify: () => {
    tone({ freq: 660, hold: 0.03, release: 0.12, gain: 0.1 });
    tone({ freq: 880, start: 0.09, hold: 0.03, release: 0.16, gain: 0.1 });
  },
  chime: () => {
    // Two soft stacked fifths.
    for (const [freq, start] of [[523.25, 0], [783.99, 0.02], [1046.5, 0.5], [1567.98, 0.52]]) {
      tone({ freq, start, attack: 0.04, hold: 0.1, release: 1.4, gain: 0.09 });
    }
  },
  victory: () => {
    [523.25, 659.25, 783.99, 1046.5].forEach((freq, i) =>
      tone({ freq, start: i * 0.13, attack: 0.02, hold: 0.1, release: 0.7, gain: 0.14, type: "triangle" }));
  },
  defeat: () => {
    [392, 349.23, 311.13, 261.63].forEach((freq, i) =>
      tone({ freq, start: i * 0.2, attack: 0.03, hold: 0.12, release: 0.8, gain: 0.12, type: "triangle" }));
  },
  draw: () => {
    tone({ freq: 523.25, hold: 0.1, release: 0.6, gain: 0.12, type: "triangle" });
    tone({ freq: 523.25, start: 0.25, hold: 0.1, release: 0.8, gain: 0.12, type: "triangle" });
  },
};

export function play(name) {
  if (muted || !ctx || ctx.state !== "running") return;
  SOUNDS[name]?.();
}
