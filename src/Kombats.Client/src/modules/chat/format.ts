import { format } from 'date-fns';

/**
 * Render a chat message `sentAt` ISO timestamp as a short local time (HH:mm).
 * Returns the empty string on malformed input — chat messages render the
 * timestamp inline, and an empty slot is less disruptive than a crash or
 * a "Invalid Date" artifact.
 */
export function formatTimestamp(sentAt: string): string {
  try {
    return format(new Date(sentAt), 'HH:mm');
  } catch {
    return '';
  }
}
