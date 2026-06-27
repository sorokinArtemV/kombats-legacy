import { isApiError } from '@/types/api';
import { NAME_MIN, NAME_MAX } from './components/NameInput';

const NAME_PATTERN = /^[A-Za-z0-9_\- ]+$/;

export interface NameValidationResult {
  ok: boolean;
  error?: string;
  trimmed: string;
}

export function validateName(raw: string): NameValidationResult {
  const trimmed = raw.trim();
  if (trimmed.length === 0) return { ok: false, trimmed, error: 'Enter a display name' };
  if (trimmed.length < NAME_MIN)
    return { ok: false, trimmed, error: `At least ${NAME_MIN} characters` };
  if (trimmed.length > NAME_MAX)
    return { ok: false, trimmed, error: `At most ${NAME_MAX} characters` };
  if (!NAME_PATTERN.test(trimmed))
    return { ok: false, trimmed, error: 'Letters, numbers, space, - and _ only' };
  return { ok: true, trimmed };
}

/**
 * Maps an unknown error from the onboarding setName/changeAvatar pipeline to
 * a user-facing message. Returns null for non-error inputs so callers can
 * compose with overrides (e.g., a local "character not loaded" pre-check).
 */
export function mapNameMutationError(error: unknown): string {
  if (!isApiError(error)) return 'An unexpected error occurred.';

  if (error.status === 409) return 'This name is already taken.';
  if (error.status === 400) {
    const details = error.error.details;
    if (details) {
      const messages: string[] = [];
      for (const value of Object.values(details)) {
        if (Array.isArray(value)) {
          for (const item of value) {
            if (typeof item === 'string') messages.push(item);
          }
        }
      }
      if (messages.length > 0) return messages.join('. ');
    }
    return error.error.message;
  }
  return error.error.message;
}
