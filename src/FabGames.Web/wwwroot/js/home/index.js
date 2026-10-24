// Home: boot → profile select → dashboard.

import { initApp } from "../core/app.js";
import { culture } from "../core/i18n.js";
import { session } from "../core/storage.js";
import { bus, focusFirst } from "../core/input.js";
import { getCurrent, setCurrent } from "../core/profile.js";
import { setBackdrop } from "../core/atmos.js";
import { runBoot } from "./boot.js";
import { initProfiles, render as renderProfiles, focusPreselected } from "./profiles.js";
import { initDashboard, showProfile, enter as enterDashboard, leave as leaveDashboard } from "./dashboard.js";

initApp();

const stages = {
  profiles: document.getElementById("stage-profiles"),
  dashboard: document.getElementById("stage-dashboard"),
};
let current = null;

/** Only same-site room links are honoured, so the ?next parameter cannot be used to redirect elsewhere. */
function safeNext() {
  const next = new URLSearchParams(location.search).get("next");
  return next && new RegExp(`^/${culture}/play/([A-HJKMNP-Z2-9]{6})$`).test(next) ? next : null;
}

function show(name) {
  if (current === name) return;
  if (current === "dashboard") leaveDashboard();
  for (const [key, el] of Object.entries(stages)) {
    el.hidden = key !== name;
    el.classList.remove("is-in");
  }
  const stage = stages[name];
  void stage.offsetWidth;
  stage.classList.add("is-in");
  current = name;
}

function pickProfile(profile) {
  setCurrent(profile.id);
  const next = safeNext();
  if (next) {
    location.href = next;
    return;
  }
  openDashboard(profile);
}

function openDashboard(profile) {
  showProfile(profile);
  show("dashboard");
  enterDashboard();
}

function openProfiles() {
  setBackdrop(null);
  renderProfiles();
  show("profiles");
  focusPreselected();
}

initProfiles(pickProfile);
initDashboard({ onSwitchProfile: openProfiles });

bus.addEventListener("back", () => {
  if (current === "dashboard") openProfiles();
});

const next = safeNext();
if (!session.get("fab.booted") && !next) {
  await runBoot();
}
session.set("fab.booted", true);

const profile = getCurrent();
if (profile && session.get("fab.profile.current") === profile.id && !next) {
  openDashboard(profile);
} else {
  openProfiles();
}

// Keep a sensible focus target if nothing grabbed it.
setTimeout(() => {
  if (document.activeElement === document.body && current) focusFirst(stages[current]);
}, 50);
