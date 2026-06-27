import { create } from 'zustand';
import type { ConnectionState } from '@/transport/signalr/connection-state';
import type {
  ChatMessageResponse,
  OnlinePlayerResponse,
  ChatErrorEvent,
  ChatSender,
} from '@/types/chat';
import type { Uuid } from '@/types/common';

const MAX_GLOBAL_MESSAGES = 500;
// Per-conversation cap on real-time DM buffer. HTTP history backfills older
// messages when a panel is (re)opened, so trimming the live buffer does not
// lose data — it only bounds memory/render cost over long sessions.
const MAX_DIRECT_MESSAGES_PER_CONVERSATION = 500;

interface RateLimitState {
  isLimited: boolean;
  retryAfterMs: number | null;
  limitedAt: number | null;
}

interface DirectConversation {
  conversationId: Uuid;
  otherPlayer: OnlinePlayerResponse;
  messages: ChatMessageResponse[];
  lastMessageAt: string | null;
}

export interface OpenDmTab {
  otherPlayerId: Uuid;
  displayName: string;
}

export type ChatTabId = 'general' | Uuid;

interface ChatState {
  connectionState: ConnectionState;
  globalConversationId: Uuid | null;
  globalMessages: ChatMessageResponse[];
  directConversations: Map<Uuid, DirectConversation>;
  // Running total of all messages across every direct conversation. Maintained
  // incrementally inside addDirectMessage so the BottomDock unread-baseline
  // selector can read a primitive instead of scanning the conversation map on
  // every chat-store mutation (presence, connection state, errors). Reset to
  // zero by clearStore; otherwise grows monotonically except when the
  // per-conversation 500-message buffer trims older entries.
  directMessagesTotal: number;
  onlinePlayers: Map<Uuid, OnlinePlayerResponse>;
  onlineCount: number;
  rateLimitState: RateLimitState;
  suppressedOpponentId: Uuid | null;
  lastError: ChatErrorEvent | null;

  // Tab strip state — drives the BottomDock chat tabs.
  // `activeTabId === 'general'` shows global chat; otherwise it's the
  // `otherPlayerId` of the focused DM tab. `unreadByPlayerId` is keyed by the
  // other player's id and only populated for DM senders, never for echoed
  // outbound messages.
  openDmTabs: OpenDmTab[];
  activeTabId: ChatTabId;
  unreadByPlayerId: Map<Uuid, number>;

  setConnectionState: (state: ConnectionState) => void;
  setGlobalSession: (conversationId: Uuid, players: OnlinePlayerResponse[]) => void;
  addGlobalMessage: (msg: ChatMessageResponse) => void;
  addOnlinePlayer: (player: OnlinePlayerResponse) => void;
  removeOnlinePlayer: (playerId: Uuid) => void;
  addDirectMessage: (
    msg: ChatMessageResponse,
    currentUserId: Uuid | null,
    recipientHint?: ChatSender,
  ) => { suppressed: boolean };
  setSuppressedOpponent: (playerId: Uuid) => void;
  clearSuppressedOpponent: () => void;
  handleChatError: (error: ChatErrorEvent) => void;
  handleConnectionLost: () => void;
  clearRateLimit: () => void;
  clearStore: () => void;

  openDmTab: (otherPlayerId: Uuid, displayName: string) => void;
  closeDmTab: (otherPlayerId: Uuid) => void;
  setActiveTab: (tabId: ChatTabId) => void;
}

function isDuplicate(messages: ChatMessageResponse[], messageId: Uuid): boolean {
  return messages.some((m) => m.messageId === messageId);
}

// Keycloak `sub` and .NET `Guid` both serialize lowercase, but we've seen
// mixed-case values surface from the JWT layer. Treat tab keys / unread keys /
// active-tab comparisons as case-insensitive throughout.
export function sameId(a: string | null | undefined, b: string | null | undefined): boolean {
  if (!a || !b) return false;
  return a.toLowerCase() === b.toLowerCase();
}

export const useChatStore = create<ChatState>()((set, get) => ({
  connectionState: 'disconnected',
  globalConversationId: null,
  globalMessages: [],
  directConversations: new Map(),
  directMessagesTotal: 0,
  onlinePlayers: new Map(),
  onlineCount: 0,
  rateLimitState: { isLimited: false, retryAfterMs: null, limitedAt: null },
  suppressedOpponentId: null,
  lastError: null,

  openDmTabs: [],
  activeTabId: 'general',
  unreadByPlayerId: new Map(),

  setConnectionState: (connectionState) => set({ connectionState }),

  setGlobalSession: (conversationId, players) => {
    const playerMap = new Map<Uuid, OnlinePlayerResponse>();
    for (const player of players) {
      playerMap.set(player.playerId, player);
    }
    // Global chat is live-only: globalMessages is never seeded from server
    // backlog. Existing in-session messages are preserved across reconnects.
    // onlineCount always derived from Map.size — no mixing with server totalOnline.
    set({
      globalConversationId: conversationId,
      onlinePlayers: playerMap,
      onlineCount: playerMap.size,
    });
  },

  addGlobalMessage: (msg) => {
    const state = get();
    if (isDuplicate(state.globalMessages, msg.messageId)) return;

    const updated = [...state.globalMessages, msg];
    set({
      globalMessages:
        updated.length > MAX_GLOBAL_MESSAGES
          ? updated.slice(updated.length - MAX_GLOBAL_MESSAGES)
          : updated,
    });
  },

  addDirectMessage: (msg, currentUserId, recipientHint) => {
    const state = get();
    const senderId = msg.sender.playerId;
    const suppressed = senderId === state.suppressedOpponentId;

    const existing = state.directConversations.get(msg.conversationId);
    if (existing && isDuplicate(existing.messages, msg.messageId)) {
      return { suppressed };
    }

    const updated = new Map(state.directConversations);
    // Track the per-conversation length delta so directMessagesTotal stays
    // in sync with the trimmed message buffers without rescanning every
    // conversation. Trim windows yield a delta of zero once a conversation
    // is at the 500-message cap.
    let messageCountDelta: number;
    if (existing) {
      const appended = [...existing.messages, msg];
      const trimmed =
        appended.length > MAX_DIRECT_MESSAGES_PER_CONVERSATION
          ? appended.slice(appended.length - MAX_DIRECT_MESSAGES_PER_CONVERSATION)
          : appended;
      messageCountDelta = trimmed.length - existing.messages.length;
      updated.set(msg.conversationId, {
        ...existing,
        messages: trimmed,
        lastMessageAt: msg.sentAt,
      });
    } else {
      messageCountDelta = 1;
      // For an outbound echo creating a fresh conversation entry, `senderId`
      // is the current user's own id — naively storing it as `otherPlayer`
      // makes DirectMessagePanel's filter (otherPlayer.playerId vs the
      // recipient's id) miss forever. The optional `recipientHint` carries
      // the OTHER party's identity so a self-initiated DM keys correctly.
      const isSelfSender = sameId(senderId, currentUserId);
      const otherPlayer =
        isSelfSender && recipientHint
          ? { playerId: recipientHint.playerId, displayName: recipientHint.displayName }
          : { playerId: senderId, displayName: msg.sender.displayName };
      updated.set(msg.conversationId, {
        conversationId: msg.conversationId,
        otherPlayer,
        messages: [msg],
        lastMessageAt: msg.sentAt,
      });
    }

    // Per-tab unread badge. Echoes of outbound DMs route through this same
    // handler (server fans out to both participants' identity groups), so we
    // skip self-sent messages. We also skip the increment when the recipient
    // tab is currently active — the user is already looking at it.
    // `currentUserId` is supplied by the caller (chat-hub event wiring) to
    // keep the chat store decoupled from the auth store at the type level.
    const isInbound = !sameId(senderId, currentUserId);
    const isFocusedOnSender = sameId(state.activeTabId, senderId);

    let nextUnread = state.unreadByPlayerId;
    if (isInbound && !isFocusedOnSender) {
      const current = state.unreadByPlayerId.get(senderId) ?? 0;
      nextUnread = new Map(state.unreadByPlayerId);
      nextUnread.set(senderId, current + 1);
    }

    // Auto-open a DM tab for inbound senders that don't already have one,
    // so a fresh DM (or one received after the user closed the tab) surfaces
    // in the dock without requiring the recipient to find the sender in the
    // players list. The new tab is NOT focused — the pulse animation driven
    // by `unreadByPlayerId` keeps drawing attention until the user clicks it.
    let nextTabs = state.openDmTabs;
    if (isInbound) {
      const hasTab = state.openDmTabs.some((t) => sameId(t.otherPlayerId, senderId));
      if (!hasTab) {
        nextTabs = [
          ...state.openDmTabs,
          { otherPlayerId: senderId, displayName: msg.sender.displayName },
        ];
      }
    }

    set({
      directConversations: updated,
      ...(messageCountDelta !== 0
        ? { directMessagesTotal: state.directMessagesTotal + messageCountDelta }
        : {}),
      ...(nextUnread !== state.unreadByPlayerId ? { unreadByPlayerId: nextUnread } : {}),
      ...(nextTabs !== state.openDmTabs ? { openDmTabs: nextTabs } : {}),
    });
    return { suppressed };
  },

  setSuppressedOpponent: (playerId) => set({ suppressedOpponentId: playerId }),

  clearSuppressedOpponent: () => set({ suppressedOpponentId: null }),

  addOnlinePlayer: (player) => {
    const state = get();
    const updated = new Map(state.onlinePlayers);
    updated.set(player.playerId, player);
    set({ onlinePlayers: updated, onlineCount: updated.size });
  },

  removeOnlinePlayer: (playerId) => {
    const state = get();
    const updated = new Map(state.onlinePlayers);
    updated.delete(playerId);
    set({ onlinePlayers: updated, onlineCount: updated.size });
  },

  handleChatError: (error) => {
    if (error.code === 'rate_limited') {
      set({
        rateLimitState: {
          isLimited: true,
          retryAfterMs: error.retryAfterMs,
          limitedAt: Date.now(),
        },
        lastError: error,
      });
    } else {
      set({ lastError: error });
    }
  },

  handleConnectionLost: () => {
    set({ connectionState: 'disconnected' });
  },

  clearRateLimit: () => {
    set({
      rateLimitState: { isLimited: false, retryAfterMs: null, limitedAt: null },
    });
  },

  clearStore: () =>
    set({
      connectionState: 'disconnected',
      globalConversationId: null,
      globalMessages: [],
      directConversations: new Map(),
      directMessagesTotal: 0,
      onlinePlayers: new Map(),
      onlineCount: 0,
      rateLimitState: { isLimited: false, retryAfterMs: null, limitedAt: null },
      suppressedOpponentId: null,
      lastError: null,
      openDmTabs: [],
      activeTabId: 'general',
      unreadByPlayerId: new Map(),
    }),

  openDmTab: (otherPlayerId, displayName) => {
    const state = get();
    const existingIdx = state.openDmTabs.findIndex((t) => sameId(t.otherPlayerId, otherPlayerId));

    let nextTabs = state.openDmTabs;
    if (existingIdx >= 0) {
      // Refresh the cached display name in case it changed since the tab was
      // first opened (rare, but cheap and keeps the strip honest).
      const existing = state.openDmTabs[existingIdx];
      if (existing.displayName !== displayName) {
        nextTabs = [...state.openDmTabs];
        nextTabs[existingIdx] = { otherPlayerId, displayName };
      }
    } else {
      nextTabs = [...state.openDmTabs, { otherPlayerId, displayName }];
    }

    // Focusing the tab clears its unread badge.
    let nextUnread = state.unreadByPlayerId;
    if (state.unreadByPlayerId.has(otherPlayerId)) {
      nextUnread = new Map(state.unreadByPlayerId);
      nextUnread.delete(otherPlayerId);
    }

    set({
      openDmTabs: nextTabs,
      activeTabId: otherPlayerId,
      unreadByPlayerId: nextUnread,
    });
  },

  closeDmTab: (otherPlayerId) => {
    const state = get();
    const nextTabs = state.openDmTabs.filter((t) => !sameId(t.otherPlayerId, otherPlayerId));
    if (nextTabs.length === state.openDmTabs.length) return;

    let nextUnread = state.unreadByPlayerId;
    if (state.unreadByPlayerId.has(otherPlayerId)) {
      nextUnread = new Map(state.unreadByPlayerId);
      nextUnread.delete(otherPlayerId);
    }

    // Closing the focused tab falls back to General — never leave activeTabId
    // pointing at a tab that no longer exists.
    const nextActive: ChatTabId = sameId(state.activeTabId, otherPlayerId)
      ? 'general'
      : state.activeTabId;

    set({
      openDmTabs: nextTabs,
      activeTabId: nextActive,
      unreadByPlayerId: nextUnread,
    });
  },

  setActiveTab: (tabId) => {
    const state = get();
    if (tabId === 'general') {
      set({ activeTabId: 'general' });
      return;
    }

    let nextUnread = state.unreadByPlayerId;
    if (state.unreadByPlayerId.has(tabId)) {
      nextUnread = new Map(state.unreadByPlayerId);
      nextUnread.delete(tabId);
    }

    set({ activeTabId: tabId, unreadByPlayerId: nextUnread });
  },
}));
