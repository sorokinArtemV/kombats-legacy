import { httpClient } from '../client';
import type { MessageListResponse } from '@/types/chat';

export function getDirectMessages(
  otherPlayerId: string,
  before?: string,
): Promise<MessageListResponse> {
  const query = before ? `?before=${encodeURIComponent(before)}` : '';
  return httpClient.get<MessageListResponse>(
    `/api/v1/chat/direct/${otherPlayerId}/messages${query}`,
  );
}
