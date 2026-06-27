import { httpClient } from '../client';
import type { PlayerCardResponse } from '@/types/player';

export function getCard(playerId: string): Promise<PlayerCardResponse> {
  return httpClient.get<PlayerCardResponse>(`/api/v1/players/${playerId}/card`);
}
