import type { Uuid } from './common';

// ---------------------------------------------------------------------------
// Error
// ---------------------------------------------------------------------------

export interface ApiError {
  error: {
    code: string;
    message: string;
    details?: Record<string, unknown>;
  };
  status: number;
}

/**
 * Shared structural type guard for `ApiError`. Centralized here so every
 * caller agrees on what "looks like an API error" means — previously the
 * two matchmaking call-sites each defined their own identical copy.
 */
export function isApiError(err: unknown): err is ApiError {
  return (
    typeof err === 'object' &&
    err !== null &&
    'status' in err &&
    typeof (err as ApiError).status === 'number'
  );
}

// ---------------------------------------------------------------------------
// Game state
// ---------------------------------------------------------------------------

export interface GameStateResponse {
  character: CharacterResponse | null;
  queueStatus: QueueStatusResponse | null;
  isCharacterCreated: boolean;
  degradedServices: string[] | null;
}

export type OnboardingState = 'Draft' | 'Named' | 'Ready' | 'Unknown';

export interface CharacterResponse {
  characterId: Uuid;
  onboardingState: OnboardingState;
  name: string | null;
  strength: number;
  agility: number;
  intuition: number;
  vitality: number;
  unspentPoints: number;
  revision: number;
  totalXp: number;
  level: number;
  avatarId: string;
}

export type OnboardResponse = CharacterResponse;

// ---------------------------------------------------------------------------
// Queue / matchmaking
// ---------------------------------------------------------------------------

export type QueueStatus = 'Idle' | 'Searching' | 'Matched' | 'NotQueued';
export type MatchState =
  | 'Queued'
  | 'BattleCreateRequested'
  | 'BattleCreated'
  | 'Completed'
  | 'TimedOut';

export interface QueueStatusResponse {
  status: QueueStatus;
  matchId: Uuid | null;
  battleId: Uuid | null;
  matchState: MatchState | null;
}

export interface LeaveQueueResponse {
  leftQueue: boolean;
  matchId: Uuid | null;
  battleId: Uuid | null;
}

// ---------------------------------------------------------------------------
// Character mutations
// ---------------------------------------------------------------------------

export interface SetCharacterNameRequest {
  name: string;
}

export interface AllocateStatsRequest {
  expectedRevision: number;
  strength: number;
  agility: number;
  intuition: number;
  vitality: number;
}

export interface AllocateStatsResponse {
  strength: number;
  agility: number;
  intuition: number;
  vitality: number;
  unspentPoints: number;
  revision: number;
}

export interface ChangeAvatarRequest {
  expectedRevision: number;
  avatarId: string;
}

export interface ChangeAvatarResponse {
  avatarId: string;
  revision: number;
}
