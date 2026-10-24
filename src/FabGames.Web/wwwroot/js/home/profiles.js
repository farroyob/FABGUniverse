// Profile select: a row of avatar bubbles, plus a form to create a profile.

import { t } from "../core/i18n.js";
import { play } from "../core/audio.js";
import { AVATARS, avatarUrl, deleteProfile, getCurrent, getProfiles, saveProfile, validName } from "../core/profile.js";
import { focusFirst } from "../core/input.js";

const list = document.getElementById("profile-list");
const form = document.getElementById("profile-form");
const nameInput = document.getElementById("profile-name");
const errorEl = document.getElementById("profile-error");
const grid = document.getElementById("avatar-grid");
const cancel = document.getElementById("profile-cancel");

let onPick = () => {};
let selectedAvatar = AVATARS[0];

export function initProfiles(pick) {
  onPick = pick;

  grid.replaceChildren(...AVATARS.map((id) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "avatar-option";
    button.dataset.nav = "";
    button.dataset.avatar = id;
    button.setAttribute("role", "radio");
    button.setAttribute("aria-checked", "false");
    button.setAttribute("aria-label", id.replace("a", "#"));
    button.innerHTML = `<img src="${avatarUrl(id)}" alt="" width="56" height="56">`;
    button.addEventListener("click", () => selectAvatar(id));
    return button;
  }));

  form.addEventListener("submit", (e) => {
    e.preventDefault();
    if (!validName(nameInput.value)) {
      errorEl.hidden = false;
      nameInput.focus();
      play("error");
      return;
    }
    const profile = saveProfile({ name: nameInput.value, avatar: selectedAvatar });
    play("select");
    hideForm();
    onPick(profile);
  });

  nameInput.addEventListener("input", () => { errorEl.hidden = true; });
  cancel.addEventListener("click", () => { hideForm(); render(); });
}

function selectAvatar(id) {
  selectedAvatar = id;
  for (const el of grid.children) el.setAttribute("aria-checked", String(el.dataset.avatar === id));
  play("tick");
}

function showForm() {
  list.hidden = true;
  form.hidden = false;
  document.getElementById("profiles-title").hidden = true;
  nameInput.value = "";
  errorEl.hidden = true;
  cancel.hidden = getProfiles().length === 0;
  selectAvatar(AVATARS[Math.floor(Math.random() * AVATARS.length)]);
  nameInput.focus();
}

function hideForm() {
  form.hidden = true;
  list.hidden = false;
  document.getElementById("profiles-title").hidden = false;
}

export function render() {
  const profiles = getProfiles();
  if (profiles.length === 0) {
    showForm();
    return;
  }
  hideForm();

  const items = profiles.map((profile) => {
    const li = document.createElement("li");
    li.className = "profile-item";

    const pick = document.createElement("button");
    pick.type = "button";
    pick.className = "profile-bubble";
    pick.dataset.nav = "";
    pick.dataset.id = profile.id;
    pick.innerHTML = `<span class="profile-bubble__disc"><img src="${avatarUrl(profile.avatar)}" alt="" width="112" height="112"></span><span class="profile-bubble__name"></span>`;
    pick.querySelector(".profile-bubble__name").textContent = profile.name;
    pick.addEventListener("click", () => { play("select"); onPick(profile); });

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "profile-delete";
    remove.dataset.nav = "";
    remove.title = t("profile.delete");
    remove.setAttribute("aria-label", `${t("profile.delete")}: ${profile.name}`);
    remove.innerHTML = '<svg class="icon" aria-hidden="true"><use href="/img/icons.svg#i-trash"></use></svg>';
    remove.addEventListener("click", () => {
      if (!window.confirm(`${t("profile.delete")}: ${profile.name}?`)) return;
      deleteProfile(profile.id);
      play("back");
      render();
      focusPreselected();
    });

    li.append(pick, remove);
    return li;
  });

  const add = document.createElement("li");
  const addButton = document.createElement("button");
  addButton.type = "button";
  addButton.className = "profile-bubble profile-bubble--new";
  addButton.dataset.nav = "";
  addButton.innerHTML = `<span class="profile-bubble__disc"><svg class="icon" aria-hidden="true"><use href="/img/icons.svg#i-plus"></use></svg></span><span class="profile-bubble__name">${t("profile.new")}</span>`;
  addButton.addEventListener("click", () => { play("select"); showForm(); });
  add.append(addButton);

  list.replaceChildren(...items, add);
}

/** The last used profile gets the focus, like a console remembering who played last. */
export function focusPreselected() {
  const current = getCurrent();
  const target = current && list.querySelector(`.profile-bubble[data-id="${CSS.escape(current.id)}"]`);
  if (target) {
    // Show the ring even before any keyboard input: the last profile is visibly preselected.
    target.classList.add("is-focus");
    target.addEventListener("blur", () => target.classList.remove("is-focus"), { once: true });
    target.focus({ preventScroll: true });
  } else if (!form.hidden) nameInput.focus();
  else focusFirst(document.getElementById("stage-profiles"));
}
