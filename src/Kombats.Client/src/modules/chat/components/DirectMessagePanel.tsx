import { useRef, useEffect, useMemo } from 'react';
import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { chatKeys } from '@/app/query-client';
import * as chatApi from '@/transport/http/endpoints/chat';
import { useChatStore, sameId } from '../store';
import { Spinner } from '@/ui/components/Spinner';
import { useScrollToBottom } from '@/ui/hooks/useScrollToBottom';
import { MessageInput } from './MessageInput';
import { formatTimestamp } from '../format';
import { getNickColor } from '../nick-color';
import type { ChatMessageResponse, MessageListResponse } from '@/types/chat';

interface DirectMessagePanelProps {
  otherPlayerId: string;
  displayName: string;
}

export function DirectMessagePanel({ otherPlayerId, displayName }: DirectMessagePanelProps) {
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const queryClient = useQueryClient();
  const realtimeConversations = useChatStore((s) => s.directConversations);

  // Cursor-paginated history. Each page is one BFF response; the next page
  // loads OLDER messages using the oldest sentAt of the current accumulated
  // pages as the `before` cursor. Living in the query cache means revisiting
  // a panel after switching tabs no longer drops the user's scroll-back.
  const historyQuery = useInfiniteQuery<MessageListResponse, Error>({
    queryKey: chatKeys.directMessages(otherPlayerId),
    queryFn: ({ pageParam }) =>
      chatApi.getDirectMessages(otherPlayerId, pageParam as string | undefined),
    initialPageParam: undefined,
    getNextPageParam: (lastPage) => {
      if (!lastPage.hasMore || lastPage.messages.length === 0) return undefined;
      // The cursor for the NEXT older page is the oldest sentAt in the
      // most-recently-fetched page. Compute the min explicitly — the BFF
      // response order is not guaranteed.
      let oldest = lastPage.messages[0].sentAt;
      for (let i = 1; i < lastPage.messages.length; i++) {
        const candidate = lastPage.messages[i].sentAt;
        if (candidate < oldest) oldest = candidate;
      }
      return oldest;
    },
    staleTime: 10_000,
  });

  // Flatten the page list, then merge with realtime DM messages and dedupe.
  const historyMessages = useMemo(() => {
    if (!historyQuery.data) return undefined;
    const out: ChatMessageResponse[] = [];
    for (const page of historyQuery.data.pages) {
      for (const msg of page.messages) {
        out.push(msg);
      }
    }
    return out;
  }, [historyQuery.data]);

  const messages = useMemo(
    () => mergeMessages(historyMessages ?? [], realtimeConversations, otherPlayerId),
    [historyMessages, realtimeConversations, otherPlayerId],
  );

  useScrollToBottom(messagesEndRef, messages.length);

  // Backfill the DM history only when the hub recovers from a true outage —
  // i.e. on a `disconnected → connected` transition. Reconnect flapping
  // (`reconnecting → connected → reconnecting → connected`) and the initial
  // `disconnected → connecting → connected` boot path both used to fire a
  // refetch on every entry into `connected`; keep the invalidate scoped to
  // the recovery edge so a flapping hub doesn't storm the BFF with history
  // requests. The conversation switch (`otherPlayerId` change) gets its own
  // fresh query via the query key, so no invalidate is needed there.
  const connectionState = useChatStore((s) => s.connectionState);
  const previousConnectionStateRef = useRef(connectionState);
  useEffect(() => {
    const previous = previousConnectionStateRef.current;
    previousConnectionStateRef.current = connectionState;
    if (connectionState === 'connected' && previous === 'disconnected') {
      queryClient.invalidateQueries({ queryKey: chatKeys.directMessages(otherPlayerId) });
    }
  }, [connectionState, otherPlayerId, queryClient]);

  const handleLoadMore = () => {
    if (historyQuery.isFetchingNextPage || !historyQuery.hasNextPage) return;
    historyQuery.fetchNextPage();
  };

  return (
    <div className="flex h-full flex-col">
      <div className="kombats-scroll flex-1 overflow-y-auto px-4 py-3">
        {historyQuery.isPending ? (
          <div className="flex items-center justify-center py-8">
            <Spinner size="sm" />
          </div>
        ) : messages.length === 0 ? (
          <p className="py-8 text-center text-[11px] uppercase tracking-[0.18em] text-text-muted">
            No messages yet. Say hello!
          </p>
        ) : (
          <div className="flex flex-col">
            {historyQuery.hasNextPage && (
              <div className="mb-2 flex flex-col items-center">
                <button
                  onClick={handleLoadMore}
                  disabled={historyQuery.isFetchingNextPage}
                  className="self-center rounded-sm px-3 py-1 text-[10px] uppercase tracking-[0.18em] text-text-muted transition-colors duration-150 hover:text-kombats-gold disabled:opacity-50"
                >
                  {historyQuery.isFetchingNextPage ? 'Loading...' : 'Load older messages'}
                </button>
                {historyQuery.isFetchNextPageError && (
                  <p
                    className="mt-1 text-[10px] uppercase tracking-[0.18em] text-kombats-crimson-light"
                    role="alert"
                  >
                    Couldn&rsquo;t load older messages — retry
                  </p>
                )}
              </div>
            )}
            {messages.map((msg) => (
              <div key={msg.messageId} className="flex items-baseline py-0.5">
                <span
                  className="shrink-0 text-xs font-semibold"
                  style={{ color: getNickColor(msg.sender.playerId) }}
                >
                  {msg.sender.displayName}
                </span>
                <span className="ml-2 min-w-0 flex-1 break-words text-sm text-text-primary">
                  {msg.content}
                </span>
                <span className="ml-2 shrink-0 text-[11px] text-text-muted tabular-nums">
                  {formatTimestamp(msg.sentAt)}
                </span>
              </div>
            ))}
            <div ref={messagesEndRef} />
          </div>
        )}
      </div>

      <div className="border-t-[0.5px] border-border-subtle px-3 py-2">
        <MessageInput
          mode="direct"
          recipientPlayerId={otherPlayerId}
          recipientDisplayName={displayName}
        />
      </div>
    </div>
  );
}

function mergeMessages(
  httpMessages: ChatMessageResponse[],
  realtimeConversations: Map<
    string,
    { messages: ChatMessageResponse[]; otherPlayer: { playerId: string } }
  >,
  otherPlayerId: string,
): ChatMessageResponse[] {
  const seen = new Set<string>();
  const merged: ChatMessageResponse[] = [];

  for (const msg of httpMessages) {
    if (!seen.has(msg.messageId)) {
      seen.add(msg.messageId);
      merged.push(msg);
    }
  }

  for (const conv of realtimeConversations.values()) {
    if (sameId(conv.otherPlayer.playerId, otherPlayerId)) {
      for (const msg of conv.messages) {
        if (!seen.has(msg.messageId)) {
          seen.add(msg.messageId);
          merged.push(msg);
        }
      }
    }
  }

  merged.sort((a, b) => a.sentAt.localeCompare(b.sentAt));
  return merged;
}
