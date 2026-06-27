import { clsx } from 'clsx';
import type { ConnectionState } from '@/transport/signalr/connection-state';

interface ConnectionIndicatorProps {
  state: ConnectionState;
  className?: string;
}

const stateConfig: Record<ConnectionState, { color: string; label: string }> = {
  connected: { color: 'bg-success', label: 'Connected' },
  connecting: { color: 'bg-warning', label: 'Connecting...' },
  reconnecting: { color: 'bg-warning', label: 'Reconnecting...' },
  disconnected: { color: 'bg-error', label: 'Disconnected' },
  failed: { color: 'bg-error', label: 'Connection lost' },
};

export function ConnectionIndicator({ state, className }: ConnectionIndicatorProps) {
  const { color, label } = stateConfig[state];

  return (
    <div
      className={clsx('flex items-center gap-1.5', className)}
      role="status"
      aria-live="polite"
      aria-label={`Connection: ${label}`}
    >
      <div className={clsx('h-2 w-2 rounded-full', color)} aria-hidden="true" />
      <span className="text-xs text-text-muted">{label}</span>
    </div>
  );
}
