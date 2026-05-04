import { useEffect, useRef } from 'react';
import { battleHubManager, chatHubManager } from './transport-init';
import { logger } from './logger';

/**
 * Nudge stuck hubs back to life when the browser reports we're back online.
 *
 * SignalR's built-in automatic reconnect budget can be exhausted while the
 * laptop is asleep or the wifi drops out — the managers transition to the
 * terminal `failed` state and will not retry on their own. Without this
 * hook, the user has to either refresh the page or click the chat error
 * banner's Reconnect action to come back; for the battle hub there is no
 * manual-reconnect UI.
 *
 * Scope kept deliberately small: we only act on `failed`. Intentional
 * `disconnected` states (consumer left the battle, user not yet in a
 * session) are left alone. `connecting`/`reconnecting` already have
 * SignalR doing the work.
 *
 * Mounted in the `SessionShell` so it only runs for authenticated users
 * who actually have hubs in play.
 */
export function useNetworkRecovery(): void {
  // Always start `false`. We only want to act on `online` events that follow a
  // real `offline` transition during the session — not on a spurious initial
  // `online` the browser may fire after page load when nothing went down.
  const wasOffline = useRef(false);

  useEffect(() => {
    const handleOffline = () => {
      wasOffline.current = true;
    };

    const handleOnline = () => {
      if (!wasOffline.current) return;
      wasOffline.current = false;

      tryReconnect('battle hub', battleHubManager);
      tryReconnect('chat hub', chatHubManager);
    };

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
    };
  }, []);
}

interface ReconnectableHub {
  readonly connectionState: string;
  connect(): Promise<void>;
}

function tryReconnect(label: string, hub: ReconnectableHub): void {
  if (hub.connectionState !== 'failed') return;
  hub.connect().catch((err) => {
    logger.warn(`Network recovery: ${label} reconnect failed`, err);
  });
}
