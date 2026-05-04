import { useEffect, type RefObject } from 'react';

/**
 * Scrolls the referenced element into view (smooth) whenever `signal` changes
 * to a truthy value. Skips when `signal` is falsy (`0`, `''`, `null`,
 * `undefined`) so empty lists do not produce a useless scroll.
 *
 * Used by feed-shaped lists where a tail anchor element marks the bottom and
 * the parent passes either a message count or a tail-entry key as the signal.
 */
export function useScrollToBottom(
  ref: RefObject<HTMLElement | null>,
  signal: number | string | null | undefined,
): void {
  useEffect(() => {
    if (!signal) return;
    ref.current?.scrollIntoView({ behavior: 'smooth' });
  }, [signal, ref]);
}
