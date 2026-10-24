// Shared page start-up: preferences, language switch, sound toggle, dialogs.

import { t } from "./i18n.js";
import { isMuted, setMuted, onMuteChange, play } from "./audio.js";
import { applyLite } from "./atmos.js";
import { wireDialogs } from "./ui.js";
import "./input.js";

export function initApp() {
  applyLite();
  wireDialogs();

  const lang = document.getElementById("lang-links");
  const link = document.getElementById("lang-switch");
  if (lang && link) link.href = lang.dataset.url;

  const sound = document.getElementById("sound-toggle");
  const label = document.getElementById("sound-label");
  const paint = () => {
    if (!sound) return;
    sound.setAttribute("aria-pressed", String(!isMuted()));
    const text = t(isMuted() ? "nav.sound.off" : "nav.sound.on");
    sound.title = text;
    if (label) label.textContent = text;
  };
  paint();
  onMuteChange(paint);
  sound?.addEventListener("click", () => {
    setMuted(!isMuted());
    play("select");
  });
}
