// Client-side strings come from the same resx files as the views, injected as a JSON block in the layout.

let dict = {};

export const culture = document.documentElement.lang || "es";

try {
  dict = JSON.parse(document.getElementById("i18n")?.textContent ?? "{}");
} catch {
  dict = {};
}

/** Translates a key, replacing `{name}` placeholders. Unknown keys come back as the key itself. */
export function t(key, params) {
  const text = dict[key] ?? key;
  return params ? text.replace(/\{(\w+)\}/g, (match, name) => params[name] ?? match) : text;
}

/** Human message for a server error code such as NOT_YOUR_TURN. */
export function errorText(code) {
  return dict[`err.${code}`] ?? dict["err.UNKNOWN"] ?? String(code);
}

export function has(key) {
  return key in dict;
}
