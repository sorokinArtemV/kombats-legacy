import type { QueueStatusResponse } from '@/types/api';

// UI-facing projection of the queue state. `idle` + `searching` + `matched`
// + `battleTransition` are the four states the matchmaking screens care
// about — derived from the authoritative `usePlayerStore.queueStatus` and
// the local `battleTransitioning` flag.

export type QueueUiStatus = 'idle' | 'searching' | 'matched' | 'battleTransition';

export function deriveQueueUiStatus(
  queueStatus: QueueStatusResponse | null,
  battleTransitioning: boolean,
): QueueUiStatus {
  // Explicit transitioning flag wins — set by the hook when it has just
  // observed a battleId but before the authoritative queue status has been
  // written (short window after `leaveQueue` returns a battleId).
  if (battleTransitioning) return 'battleTransition';

  if (!queueStatus) return 'idle';

  switch (queueStatus.status) {
    case 'Searching':
      return 'searching';
    case 'Matched':
      return queueStatus.battleId ? 'battleTransition' : 'matched';
    case 'Idle':
    case 'NotQueued':
    default:
      return 'idle';
  }
}
