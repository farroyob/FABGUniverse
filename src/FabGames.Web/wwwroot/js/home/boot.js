// The boot sequence: black, bloom, shield, wordmark, "press any button". Runs once per session, can be
// skipped by any key, click or tap, and needs no audio gesture to start (the chime waits for one).

import { playWhenUnlocked, unlock, play } from "../core/audio.js";

const CHIME_AT_MS = 2200;

export function runBoot() {
  const stage = document.getElementById("stage-boot");
  stage.hidden = false;
  stage.focus({ preventScroll: true });

  return new Promise((resolve) => {
    let finished = false;
    const timer = setTimeout(() => playWhenUnlocked("chime"), CHIME_AT_MS);

    const finish = (event) => {
      if (finished) return;
      if (event?.type === "keydown" && ["Shift", "Control", "Alt", "Meta", "Tab"].includes(event.key)) return;
      finished = true;
      clearTimeout(timer);
      for (const type of ["pointerdown", "keydown"]) window.removeEventListener(type, finish, true);
      unlock();
      play("select");
      stage.classList.add("is-skipping");
      setTimeout(() => {
        stage.hidden = true;
        resolve();
      }, 200);
    };

    for (const type of ["pointerdown", "keydown"]) window.addEventListener(type, finish, true);
  });
}
