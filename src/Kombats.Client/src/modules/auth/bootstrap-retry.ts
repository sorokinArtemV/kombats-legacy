import { useAuthStore } from './store';

// Module-level shared state for the bootstrap guard + retry path. Exposed
// via accessors so the orchestrator in `AuthProvider` (which owns the
// bootstrap effect) and `retryBootstrap()` (the UnauthenticatedShell
// escape hatch) can coordinate without leaking component-local state onto
// the shared auth store.
//
// Separated from `AuthProvider.tsx` because the react-refresh lint rule
// forbids mixing component exports with non-component exports in one
// module.

export const BOOTSTRAP_RETRY_EVENT = 'kombats:retry-bootstrap';

let bootstrapPromise: Promise<void> | null = null;
let bootstrapAttempt = 0;

export function getBootstrapPromise(): Promise<void> | null {
  return bootstrapPromise;
}

export function setBootstrapPromise(p: Promise<void> | null): void {
  bootstrapPromise = p;
}

export function getBootstrapAttempt(): number {
  return bootstrapAttempt;
}

/**
 * Invoked by the UnauthenticatedShell retry banner when bootstrap has
 * timed out. Resets the module-level guard, bumps the attempt counter
 * (so any stale `.finally` handlers no-op on stale writes), clears the
 * stored error, and fires a custom event the `AuthSync` effect listens
 * for so it re-enters the bootstrap path.
 */
export function retryBootstrap(): void {
  bootstrapPromise = null;
  bootstrapAttempt += 1;
  useAuthStore.getState().setAuthError(null);
  window.dispatchEvent(new CustomEvent(BOOTSTRAP_RETRY_EVENT));
}
