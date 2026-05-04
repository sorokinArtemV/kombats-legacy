import { httpClient } from '../client';
import type { GameStateResponse, OnboardResponse } from '@/types/api';

export function getState(): Promise<GameStateResponse> {
  return httpClient.get<GameStateResponse>('/api/v1/game/state');
}

export function onboard(): Promise<OnboardResponse> {
  return httpClient.post<OnboardResponse>('/api/v1/game/onboard');
}
