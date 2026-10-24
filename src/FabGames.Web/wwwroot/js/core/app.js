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
  for (const link of document.querySelectorAll("#lang-switch, .lang-switch")) {
    if (lang) link.href = lang.dataset.url;
  }

  const toggles = [...document.querySelectorAll("#sound-toggle, .sound-toggle")];
  const paint = () => {
    const text = t(isMuted() ? "nav.sound.off" : "nav.sound.on");
    for (const toggle of toggles) {
      toggle.setAttribute("aria-pressed", String(!isMuted()));
      toggle.title = text;
      const label = toggle.querySelector(".sr-only");
      if (label) label.textContent = text;
    }
  };
  paint();
  onMuteChange(paint);
  for (const toggle of toggles) {
    toggle.addEventListener("click", () => {
      setMuted(!isMuted());
      play("select");
    });
  }
}
