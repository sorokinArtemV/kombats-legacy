import { useEffect } from 'react';
import { chatHubManager } from '@/app/transport-init';
import { useAuthStore } from '@/modules/auth/store';
import { useChatStore } from './store';
import type {
  GlobalMessageEvent,
  DirectMessageEvent,
  PlayerOnlineEvent,
  PlayerOfflineEvent,
  ChatErrorEvent,
  ChatMessageResponse,
} from '@/types/chat';

function toStoredMessage(event: GlobalMessageEvent, conversationId: string): ChatMessageResponse {
  return {
    messageId: event.messageId,
    conversationId,
    sender: event.sender,
    content: event.content,
    sentAt: event.sentAt,
  };
}

function directEventToMessage(event: DirectMessageEvent): ChatMessageResponse {
  return {
    messageId: event.messageId,
    conversationId: event.conversationId,
    sender: event.sender,
    content: event.content,
    sentAt: event.sentAt,
  };
}

async function joinGlobalSession(): Promise<void> {
  // Server-side group join is required to receive live messages. The response
  // also carries a recent-messages backlog, which we intentionally discard:
  // global chat is live-only — no history on entry, no restoration on refresh.
  const response = await chatHubManager.joinGlobalChat();
  useChatStore.getState().setGlobalSession(response.conversationId, response.onlinePlayers);
}

export function useChatConnection(): void {
  useEffect(() => {
    let disposed = false;
    const store = useChatStore.getState();

    chatHubManager.setEventHandlers({
      onGlobalMessageReceived: (data: GlobalMessageEvent) => {
        if (disposed) return;
        const conversationId = useChatStore.getState().globalConversationId;
        if (!conversationId) return;
        useChatStore.getState().addGlobalMessage(toStoredMessage(data, conversationId));
      },
      onDirectMessageReceived: (data: DirectMessageEvent) => {
        if (disposed) return;
        // Read the local identity at the call site rather than from inside
        // the chat-store setter so the chat module doesn't import the auth
        // store. Wiring stores at the hook layer keeps cross-store coupling
        // out of the state-transition logic.
        const currentUserId = useAuthStore.getState().userIdentityId;
        useChatStore.getState().addDirectMessage(directEventToMessage(data), currentUserId);
      },
      onPlayerOnline: (data: PlayerOnlineEvent) => {
        if (disposed) return;
        useChatStore.getState().addOnlinePlayer({
          playerId: data.playerId,
          displayName: data.displayName,
        });
      },
      onPlayerOffline: (data: PlayerOfflineEvent) => {
        if (disposed) return;
        useChatStore.getState().removeOnlinePlayer(data.playerId);
      },
      onChatError: (data: ChatErrorEvent) => {
        if (disposed) return;
        useChatStore.getState().handleChatError(data);
      },
      onChatConnectionLost: () => {
        if (disposed) return;
        useChatStore.getState().handleConnectionLost();
      },
      onConnectionStateChanged: (state) => {
        if (disposed) return;
        useChatStore.getState().setConnectionState(state);

        // On reconnect, rejoin global chat to resync state
        if (state === 'connected' && useChatStore.getState().globalConversationId !== null) {
          joinGlobalSession().catch(() => {
            // The hub is connected but the rejoin RPC failed — distinct from
            // a hub-down `service_unavailable`. The banner reads this code to
            // offer a focused rejoin retry instead of a generic "chat is
            // unavailable" message.
            if (disposed) return;
            useChatStore.getState().handleChatError({
              code: 'resync_failed',
              message: 'Chat resync failed after reconnect.',
              retryAfterMs: null,
            });
          });
        }

        // Clear non-rate-limit errors on reconnect
        if (state === 'connected') {
          const current = useChatStore.getState();
          if (current.lastError && current.lastError.code !== 'rate_limited') {
            useChatStore.setState({ lastError: null });
          }
        }
      },
    });

    store.setConnectionState('connecting');

    chatHubManager
      .connect()
      .then(() => {
        if (disposed) return;
        return joinGlobalSession();
      })
      .catch(() => {
        if (disposed) return;
        useChatStore.getState().setConnectionState('disconnected');
      });

    return () => {
      disposed = true;
      chatHubManager.setEventHandlers({});
      chatHubManager.disconnect().catch(() => {
        // Cleanup — ignore disconnect errors
      });
      // Targeted reset only. The full chat-store wipe lives in the logout
      // flow (clearSessionState calls useChatStore.clearStore() before
      // removeUser fires), so transient SessionShell remounts — HMR, React
      // Refresh boundaries — no longer dump global chat history.
      useChatStore.getState().setConnectionState('disconnected');
    };
  }, []);
}

/**
 * Manual rejoin of the global chat session after a `resync_failed` error.
 * The hub itself is still connected at this point — only the
 * `joinGlobalChat` RPC failed — so we don't need to disconnect/reconnect.
 * On success the banner clears via the standard `lastError` path; on failure
 * the same `resync_failed` error is re-set so the retry button stays visible.
 */
export async function retryRejoinGlobalChat(): Promise<void> {
  try {
    await joinGlobalSession();
    const current = useChatStore.getState();
    if (current.lastError && current.lastError.code === 'resync_failed') {
      useChatStore.setState({ lastError: null });
    }
  } catch {
    useChatStore.getState().handleChatError({
      code: 'resync_failed',
      message: 'Chat resync failed after reconnect.',
      retryAfterMs: null,
    });
  }
}

/**
 * Manual reconnect for the chat hub. Used by the UI when the connection has
 * entered the terminal `failed` state (automatic reconnect exhausted) and
 * the user asks to retry.
 */
export async function reconnectChat(): Promise<void> {
  try {
    await chatHubManager.disconnect();
  } catch {
    // Ignore — we're about to reconnect anyway
  }
  useChatStore.getState().setConnectionState('connecting');
  try {
    await chatHubManager.connect();
    await joinGlobalSession();
  } catch {
    useChatStore.getState().setConnectionState('failed');
  }
}

export function useGlobalMessages() {
  return useChatStore((s) => s.globalMessages);
}

export function useOnlinePlayers() {
  return useChatStore((s) => s.onlinePlayers);
}

export function useOnlineCount() {
  return useChatStore((s) => s.onlineCount);
}

export function useChatConnectionState() {
  return useChatStore((s) => s.connectionState);
}

export function useChatRateLimitState() {
  return useChatStore((s) => s.rateLimitState);
}

export function useDirectConversations() {
  return useChatStore((s) => s.directConversations);
}

export function useChatLastError() {
  return useChatStore((s) => s.lastError);
}
