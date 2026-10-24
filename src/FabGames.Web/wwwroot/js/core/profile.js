// Profiles live in localStorage. The player id lives in sessionStorage so every tab is its own player:
// a refresh keeps the seat, and two tabs of one browser can play each other.

import { local, session } from "./storage.js";

export const AVATARS = Array.from({ length: 12 }, (_, i) => `a${String(i + 1).padStart(2, "0")}`);
export const NAME_MIN = 2;
export const NAME_MAX = 16;

const PROFILES = "fab.profiles";
const CURRENT = "fab.profile.current";
const PLAYER_ID = "fab.playerId";

export const avatarUrl = (avatarId) => `/img/avatars/${AVATARS.includes(avatarId) ? avatarId : AVATARS[0]}.svg`;

export function getProfiles() {
  const list = local.get(PROFILES, []);
  return Array.isArray(list) ? list.filter((p) => p && p.id && p.name) : [];
}

export function getCurrent() {
  const id = session.get(CURRENT) ?? local.get(CURRENT);
  return getProfiles().find((p) => p.id === id) ?? null;
}

export function setCurrent(id) {
  local.set(CURRENT, id);
  session.set(CURRENT, id);
}

export function validName(name) {
  const trimmed = (name ?? "").trim();
  return trimmed.length >= NAME_MIN && trimmed.length <= NAME_MAX && !/[\u0000-\u001f]/.test(trimmed);
}

export function saveProfile({ id, name, avatar }) {
  const profiles = getProfiles();
  const clean = { id: id ?? crypto.randomUUID(), name: name.trim(), avatar };
  const index = profiles.findIndex((p) => p.id === clean.id);
  if (index >= 0) {
    profiles[index] = clean;
  } else {
    profiles.push(clean);
  }
  local.set(PROFILES, profiles);
  return clean;
}

export function deleteProfile(id) {
  local.set(PROFILES, getProfiles().filter((p) => p.id !== id));
  local.remove(statsKey(id));
  if (local.get(CURRENT) === id) {
    local.remove(CURRENT);
  }
  if (session.get(CURRENT) === id) {
    session.remove(CURRENT);
  }
}

/** Secret id used to prove identity to the hub. Generated once per tab; never shown to other players. */
export function playerId() {
  let id = session.get(PLAYER_ID);
  if (typeof id !== "string" || id.length < 16) {
    id = crypto.randomUUID().replaceAll("-", "");
    session.set(PLAYER_ID, id);
  }
  return id;
}

// ── Local-only stats and trophies ──────────────────────────────

const statsKey = (profileId) => `fab.stats.${profileId}`;

export function getStats(profileId) {
  return local.get(statsKey(profileId), { games: 0, wins: 0, draws: 0, trophies: [] });
}

/** Records a finished game and returns the trophy ids unlocked by it. */
export function recordResult(profileId, outcome) {
  const stats = getStats(profileId);
  stats.games += 1;
  if (outcome === "win") stats.wins += 1;
  if (outcome === "draw") stats.draws += 1;

  const unlocked = [];
  const award = (id, condition) => {
    if (condition && !stats.trophies.includes(id)) {
      stats.trophies.push(id);
      unlocked.push(id);
    }
  };
  award("first_win", outcome === "win");
  award("first_draw", outcome === "draw");
  award("ten_games", stats.games >= 10);

  local.set(statsKey(profileId), stats);
  return unlocked;
}
