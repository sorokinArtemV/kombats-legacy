import type { Uuid } from './common';

export interface PlayerCardResponse {
  playerId: Uuid;
  displayName: string;
  level: number;
  strength: number;
  agility: number;
  intuition: number;
  vitality: number;
  wins: number;
  losses: number;
  avatarId: string;
}
