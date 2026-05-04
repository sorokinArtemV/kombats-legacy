import type { Uuid, DateTimeOffset } from './common';

// ---------------------------------------------------------------------------
// Shared sub-types
// ---------------------------------------------------------------------------

export interface ChatSender {
  playerId: Uuid;
  displayName: string;
}

// ---------------------------------------------------------------------------
// HTTP response types
// ---------------------------------------------------------------------------

export interface ChatMessageResponse {
  messageId: Uuid;
  conversationId: Uuid;
  sender: ChatSender;
  content: string;
  sentAt: DateTimeOffset;
}

export interface MessageListResponse {
  messages: ChatMessageResponse[];
  hasMore: boolean;
}

export interface OnlinePlayerResponse {
  playerId: Uuid;
  displayName: string;
}

// ---------------------------------------------------------------------------
// SignalR hub responses
// ---------------------------------------------------------------------------

export interface JoinGlobalChatResponse {
  conversationId: Uuid;
  /** Discarded by client — global chat is live-only. */
  recentMessages: ChatMessageResponse[];
  onlinePlayers: OnlinePlayerResponse[];
  totalOnline: number;
}

export interface SendDirectMessageResponse {
  conversationId: Uuid;
  messageId: Uuid;
  sentAt: DateTimeOffset;
}

// ---------------------------------------------------------------------------
// SignalR events (server → client)
// ---------------------------------------------------------------------------

export interface GlobalMessageEvent {
  messageId: Uuid;
  sender: ChatSender;
  content: string;
  sentAt: DateTimeOffset;
}

export interface DirectMessageEvent {
  messageId: Uuid;
  conversationId: Uuid;
  sender: ChatSender;
  content: string;
  sentAt: DateTimeOffset;
}

export interface PlayerOnlineEvent {
  playerId: Uuid;
  displayName: string;
}

export interface PlayerOfflineEvent {
  playerId: Uuid;
}

export interface ChatErrorEvent {
  code: ChatErrorCode;
  message: string;
  retryAfterMs: number | null;
}

export type ChatErrorCode =
  | 'rate_limited'
  | 'message_too_long'
  | 'message_empty'
  | 'recipient_not_found'
  | 'not_eligible'
  | 'service_unavailable'
  // Client-only: synthesized when a post-reconnect rejoin of the global chat
  // session fails. Distinct from `service_unavailable` so the banner can
  // surface a focused "rejoin" retry instead of suggesting the entire chat
  // service is down — the hub itself is still connected at this point.
  | 'resync_failed';
