import { useState, useCallback, type KeyboardEvent } from 'react';
import { clsx } from 'clsx';
import { chatHubManager } from '@/app/transport-init';
import { useAuthStore } from '@/modules/auth/store';
import { useChatRateLimitState } from '../hooks';
import { useChatStore } from '../store';
import type { ChatMessageResponse, ChatSender } from '@/types/chat';

const MAX_MESSAGE_LENGTH = 500;

interface MessageInputProps {
  mode?: 'global' | 'direct';
  recipientPlayerId?: string;
  recipientDisplayName?: string;
  onMessageSent?: () => void;
  className?: string;
}

export function MessageInput({
  mode = 'global',
  recipientPlayerId,
  recipientDisplayName,
  onMessageSent,
  className,
}: MessageInputProps) {
  const [content, setContent] = useState('');
  const [sending, setSending] = useState(false);
  // Local-only send-failure surface. Hub timeouts and transport-level send
  // failures don't carry a matching server-side ChatError event, and routing
  // them through the global ChatErrorDisplay banner would overstate the
  // problem ("Chat is temporarily unavailable" when only the message itself
  // failed). An inline affordance keeps the feedback scoped to the action
  // that produced it; the textarea retains the draft so the user can retry.
  const [sendFailed, setSendFailed] = useState(false);
  const rateLimitState = useChatRateLimitState();
  const connectionState = useChatStore((s) => s.connectionState);

  const canSend =
    content.trim().length > 0 &&
    content.length <= MAX_MESSAGE_LENGTH &&
    !sending &&
    !rateLimitState.isLimited &&
    connectionState === 'connected';

  const handleSend = useCallback(async () => {
    const trimmed = content.trim();
    if (!trimmed || trimmed.length > MAX_MESSAGE_LENGTH || sending) return;

    setSending(true);
    setSendFailed(false);
    try {
      if (mode === 'direct' && recipientPlayerId) {
        const response = await chatHubManager.sendDirectMessage(recipientPlayerId, trimmed);
        // Post-confirm local insert. Server-issued messageId means the
        // eventual `DirectMessageReceived` echo dedupes by id in the chat
        // store. This runs AFTER the server confirms send so failures don't
        // leave a ghost message in the panel. The recipientHint lets the
        // store create the conversation entry keyed on the other player —
        // without it, a brand-new self-initiated DM would set otherPlayer to
        // self and never render in DirectMessagePanel.
        const auth = useAuthStore.getState();
        if (auth.userIdentityId && auth.displayName && recipientDisplayName) {
          const syntheticMsg: ChatMessageResponse = {
            messageId: response.messageId,
            conversationId: response.conversationId,
            sender: { playerId: auth.userIdentityId, displayName: auth.displayName },
            content: trimmed,
            sentAt: response.sentAt,
          };
          const recipientHint: ChatSender = {
            playerId: recipientPlayerId,
            displayName: recipientDisplayName,
          };
          useChatStore
            .getState()
            .addDirectMessage(syntheticMsg, auth.userIdentityId, recipientHint);
        }
      } else {
        await chatHubManager.sendGlobalMessage(trimmed);
      }
      setContent('');
      // Clear any stale message-level error on successful send
      const current = useChatStore.getState();
      if (current.lastError && current.lastError.code !== 'rate_limited') {
        useChatStore.setState({ lastError: null });
      }
      onMessageSent?.();
    } catch {
      setSendFailed(true);
    } finally {
      setSending(false);
    }
  }, [content, sending, mode, recipientPlayerId, recipientDisplayName, onMessageSent]);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        if (canSend) {
          handleSend();
        }
      }
    },
    [canSend, handleSend],
  );

  return (
    <div className={clsx('flex flex-col gap-1.5', className)}>
      <div className="flex items-center gap-2 rounded-full border border-border-subtle bg-glass px-3.5 py-1.5 transition-colors focus-within:border-accent-muted">
        <textarea
          value={content}
          onChange={(e) => {
            setContent(e.target.value);
            if (sendFailed) setSendFailed(false);
          }}
          onKeyDown={handleKeyDown}
          placeholder={
            rateLimitState.isLimited
              ? 'Slow down...'
              : connectionState !== 'connected'
                ? 'Chat disconnected'
                : 'Type a message...'
          }
          disabled={connectionState !== 'connected' || rateLimitState.isLimited}
          rows={1}
          className="flex-1 resize-none bg-transparent text-xs text-text-primary placeholder:text-text-muted outline-none disabled:opacity-50"
        />
        <button
          onClick={handleSend}
          disabled={!canSend}
          className="rounded-full px-3 py-1 text-[10px] font-medium uppercase tracking-[0.18em] text-accent-text transition-colors duration-150 hover:text-kombats-gold disabled:cursor-not-allowed disabled:opacity-40"
        >
          Send
        </button>
      </div>
      {rateLimitState.isLimited && (
        <div className="px-1 text-[10px]">
          <span className="uppercase tracking-[0.18em] text-warning">
            Rate limited — wait a moment
          </span>
        </div>
      )}
      {sendFailed && !rateLimitState.isLimited && (
        <div className="px-1 text-[10px]" role="alert">
          <span className="uppercase tracking-[0.18em] text-kombats-crimson-light">
            Send failed — retry
          </span>
        </div>
      )}
    </div>
  );
}
