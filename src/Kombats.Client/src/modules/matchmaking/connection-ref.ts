// Per-tab connection reference for heartbeat-based queue presence. Generated
// lazily on first use, kept for the lifetime of this JS context (one browser
// tab / one page load). A reload creates a fresh ref by design — the prior
// ref is left to expire on the server alongside the prior session's queue
// entry.

let cachedRef: string | null = null;

export function getQueueConnectionRef(): string {
  if (cachedRef !== null) return cachedRef;

  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    cachedRef = crypto.randomUUID();
  } else {
    cachedRef = `tab-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  }
  return cachedRef;
}
