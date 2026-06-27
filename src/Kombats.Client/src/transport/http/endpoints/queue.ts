import { httpClient } from '../client';
import type { QueueStatusResponse, LeaveQueueResponse } from '@/types/api';

// The BFF flattens upstream 200/409 into 200 + body — both successful
// joins and "already matched on a prior battle" returns travel back as a
// QueueStatusResponse the caller must inspect (see useRequeueAfterBattle
// for the post-battle stale-match retry that depends on this body).
export function join(connectionRef: string): Promise<QueueStatusResponse> {
  return httpClient.post<QueueStatusResponse>('/api/v1/queue/join', { connectionRef });
}

export function leave(connectionRef: string): Promise<LeaveQueueResponse> {
  return httpClient.post<LeaveQueueResponse>('/api/v1/queue/leave', { connectionRef });
}

export function getStatus(): Promise<QueueStatusResponse> {
  return httpClient.get<QueueStatusResponse>('/api/v1/queue/status');
}

export function heartbeat(connectionRef: string): Promise<void> {
  return httpClient.post<void>('/api/v1/queue/heartbeat', { connectionRef });
}

// pagehide / beforeunload path: keepalive: true so the browser will finish
// sending the request after the document is torn down. Auth header is
// attached by the http client at call time.
export function leaveBeacon(connectionRef: string): void {
  httpClient.postKeepalive('/api/v1/queue/leave', { connectionRef });
}
