// localStorage / sessionStorage can throw (private windows, blocked site data). Everything works without them.

function make(kind) {
  const store = () => {
    try {
      return window[kind];
    } catch {
      return null;
    }
  };
  return {
    get(key, fallback = null) {
      try {
        const raw = store()?.getItem(key);
        return raw === null || raw === undefined ? fallback : JSON.parse(raw);
      } catch {
        return fallback;
      }
    },
    set(key, value) {
      try {
        store()?.setItem(key, JSON.stringify(value));
      } catch {
        // Storage is a convenience, never a requirement.
      }
    },
    remove(key) {
      try {
        store()?.removeItem(key);
      } catch {
        // ignore
      }
    },
  };
}

export const local = make("localStorage");
export const session = make("sessionStorage");
