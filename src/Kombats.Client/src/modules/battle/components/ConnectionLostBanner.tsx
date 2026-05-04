import { useBattleConnectionState } from '../hooks';
import { Banner } from './Banner';

export function ConnectionLostBanner() {
  // Pull the live connection state so the banner stays truthful across the
  // reconnecting → connected window. Without this, "reconnecting…" could
  // linger briefly after SignalR actually comes back up.
  const connectionState = useBattleConnectionState();
  const message =
    connectionState === 'reconnecting'
      ? 'Connection lost — reconnecting…'
      : connectionState === 'connecting'
        ? 'Reconnecting to the battle…'
        : 'Connection unstable — waiting for server.';
  return <Banner tone="warning">{message}</Banner>;
}
