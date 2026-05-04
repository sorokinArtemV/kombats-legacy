import { useState } from 'react';
import { useChatStore } from '../store';
import { reconnectChat, retryRejoinGlobalChat } from '../hooks';

const ERROR_MESSAGES: Record<string, string> = {
  rate_limited: 'Too many messages. Please wait.',
  message_too_long: 'Message exceeds the character limit.',
  message_empty: 'Message cannot be empty.',
  recipient_not_found: 'Recipient not found.',
  not_eligible: 'Complete onboarding to use chat.',
  service_unavailable: 'Chat is temporarily unavailable.',
  // Hub is connected but the post-reconnect rejoin RPC failed — distinct
  // copy + a focused rejoin retry button (vs the generic dismiss-only `X`).
  resync_failed: 'Couldn’t rejoin chat after reconnect.',
};

export function ChatErrorDisplay() {
  const lastError = useChatStore((s) => s.lastError);
  const connectionState = useChatStore((s) => s.connectionState);
  // `globalConversationId` is set the first time the user joins the global
  // chat session and persists across transient drops (only `clearStore` on
  // logout resets it). Used as the "we've connected at least once" gate so
  // the transient-reconnect banner doesn't fire during initial app boot
  // (when connectionState is briefly `disconnected` → `connecting`).
  const globalConversationId = useChatStore((s) => s.globalConversationId);
  const [reconnecting, setReconnecting] = useState(false);
  const [rejoining, setRejoining] = useState(false);

  // Terminal disconnect takes precedence — the user is effectively offline and
  // error toasts will not clear by themselves. Offer an explicit retry.
  if (connectionState === 'failed') {
    const handleRetry = async () => {
      setReconnecting(true);
      try {
        await reconnectChat();
      } finally {
        setReconnecting(false);
      }
    };

    return (
      <div
        className="flex items-center justify-between border-b-[0.5px] border-kombats-crimson/40 bg-kombats-crimson/10 px-4 py-1.5"
        role="alert"
      >
        <p className="text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light">
          Chat disconnected. Automatic reconnect exhausted.
        </p>
        <button
          onClick={handleRetry}
          disabled={reconnecting}
          className="ml-2 rounded-sm border-[0.5px] border-kombats-crimson/50 px-2 py-0.5 text-[10px] uppercase tracking-[0.18em] text-kombats-crimson-light transition-colors duration-150 hover:bg-kombats-crimson/15 disabled:opacity-50"
        >
          {reconnecting ? 'Reconnecting…' : 'Reconnect'}
        </button>
      </div>
    );
  }

  // Transient-reconnect banner. Mirrors the BattleScreen ConnectionLostBanner
  // pattern: the banner surfaces immediately when the hub leaves `connected`,
  // not after the auto-reconnect budget exhausts. Without this, the send
  // button disabled state (which reacts in milliseconds) ran ~30s ahead of
  // any visible explanation, leaving the input "frozen" with no context.
  // Gated on a prior successful join so the boot path stays silent. The
  // `failed` state is already short-circuited above with its own banner.
  if (globalConversationId !== null && connectionState !== 'connected') {
    return (
      <div
        className="flex items-center border-b-[0.5px] border-kombats-crimson/40 bg-kombats-crimson/10 px-4 py-1.5"
        role="status"
        aria-live="polite"
      >
        <p className="text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light">
          Chat reconnecting…
        </p>
      </div>
    );
  }

  if (!lastError || lastError.code === 'rate_limited') return null;

  const message = ERROR_MESSAGES[lastError.code] ?? lastError.message;

  if (lastError.code === 'resync_failed') {
    const handleRejoin = async () => {
      setRejoining(true);
      try {
        await retryRejoinGlobalChat();
      } finally {
        setRejoining(false);
      }
    };
    return (
      <div
        className="flex items-center justify-between border-b-[0.5px] border-kombats-crimson/40 bg-kombats-crimson/10 px-4 py-1.5"
        role="alert"
      >
        <p className="text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light">
          {message}
        </p>
        <button
          onClick={handleRejoin}
          disabled={rejoining}
          className="ml-2 rounded-sm border-[0.5px] border-kombats-crimson/50 px-2 py-0.5 text-[10px] uppercase tracking-[0.18em] text-kombats-crimson-light transition-colors duration-150 hover:bg-kombats-crimson/15 disabled:opacity-50"
        >
          {rejoining ? 'Retrying…' : 'Retry'}
        </button>
      </div>
    );
  }

  return (
    <div
      className="flex items-center justify-between border-b-[0.5px] border-kombats-crimson/40 bg-kombats-crimson/10 px-4 py-1.5"
      role="alert"
    >
      <p className="text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light">
        {message}
      </p>
      <button
        onClick={() => useChatStore.setState({ lastError: null })}
        className="ml-2 text-xs text-kombats-crimson-light/70 transition-colors duration-150 hover:text-kombats-crimson-light"
        aria-label="Dismiss"
      >
        &#x2715;
      </button>
    </div>
  );
}
