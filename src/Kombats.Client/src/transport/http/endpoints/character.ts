import { httpClient } from '../client';
import type {
  SetCharacterNameRequest,
  AllocateStatsRequest,
  AllocateStatsResponse,
  ChangeAvatarRequest,
  ChangeAvatarResponse,
} from '@/types/api';

export function setName(data: SetCharacterNameRequest): Promise<void> {
  return httpClient.post<void>('/api/v1/character/name', data);
}

export function allocateStats(data: AllocateStatsRequest): Promise<AllocateStatsResponse> {
  return httpClient.post<AllocateStatsResponse>('/api/v1/character/stats', data);
}

export function changeAvatar(data: ChangeAvatarRequest): Promise<ChangeAvatarResponse> {
  return httpClient.post<ChangeAvatarResponse>('/api/v1/character/avatar', data);
}
