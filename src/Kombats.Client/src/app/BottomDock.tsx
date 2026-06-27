import { useEffect, useMemo, useRef, useState } from 'react';
import { clsx } from 'clsx';
import { useScrollToBottom } from '@/ui/hooks/useScrollToBottom';
import { ChevronDown, ChevronUp, MessageCircle, Swords, Users, X } from 'lucide-react';
import { useGlobalMessages, useOnlinePlayers, useOnlineCount } from '@/modules/chat/hooks';
import { useChatStore } from '@/modules/chat/store';
import type { ChatTabId, OpenDmTab } from '@/modules/chat/store';
import { useAuthStore } from '@/modules/auth/store';
import { useBattleStore } from '@/modules/battle/store';
import { BattleLogList } from '@/modules/battle/components/BattleLogList';
import { RoundMap } from '@/modules/battle/components/RoundMap';
import { MessageInput } from '@/modules/chat/components/MessageInput';
import { DirectMessagePanel } from '@/modules/chat/components/DirectMessagePanel';
import { ChatErrorDisplay } from '@/modules/chat/components/ChatErrorDisplay';
import { PlayerCard } from '@/modules/player/components/PlayerCard';
import { formatTimestamp } from '@/modules/chat/format';
import { getNickColor } from '@/modules/chat/nick-color';
import type { BattleFeedEntry, TurnResolutionLogRealtime } from '@/types/battle';
import type { OnlinePlayerResponse } from '@/types/chat';

// Soft-hairline color — preserves the legacy panel-border cool-silver tint
// (rgba(154, 154, 168, 0.18)) used by the design system's fading dividers.
// Distinct from the pure-white border token so the gradient reads as
// atmospheric, not structural.
const SOFT_HAIRLINE_BG_H =
  'linear-gradient(90deg, transparent 0%, rgba(154, 154, 168, 0.18) 50%, transparent 100%)';
const SOFT_HAIRLINE_BG_V =
  'linear-gradient(180deg, transparent 0%, rgba(154, 154, 168, 0.18) 50%, transparent 100%)';

// Active-tab gold underline glow — solid accent gold with a soft halo.
const ACTIVE_TAB_INDICATOR_SHADOW = '0 0 10px rgba(201, 162, 90, 0.45)';

// Online presence dot — jade with 6px halo (semantic "presence", not a surface).
const ONLINE_DOT_SHADOW = '0 0 6px var(--color-kombats-jade)';

const DEFAULT_HEIGHT = 170;
const MIN_HEIGHT = 140;
const MAX_HEIGHT = 520;
const COLLAPSED_HEIGHT = 32;
const KEYBOARD_STEP = 16;
const HEIGHT_STORAGE_KEY = 'kombats:dock-height';
const COLLAPSED_STORAGE_KEY = 'kombats:dock-collapsed';

function clampHeight(n: number): number {
  return Math.min(MAX_HEIGHT, Math.max(MIN_HEIGHT, Math.round(n)));
}

function loadInitialHeight(): number {
  try {
    const raw = localStorage.getItem(HEIGHT_STORAGE_KEY);
    if (!raw) return DEFAULT_HEIGHT;
    const n = Number(raw);
    if (!Number.isFinite(n)) return DEFAULT_HEIGHT;
    return clampHeight(n);
  } catch {
    return DEFAULT_HEIGHT;
  }
}

function loadInitialCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

function sameTabId(a: ChatTabId, b: ChatTabId): boolean {
  if (a === 'general' || b === 'general') return a === b;
  return a.toLowerCase() === b.toLowerCase();
}

/**
 * Persistent bottom chat dock — mounted by SessionShell on every authenticated
 * screen. Fixed overlay anchored at the bottom of the viewport so the underlying
 * scene/sprite is full-bleed beneath the header (no layout reflow).
 *
 * Tab strip: GENERAL is the permanent first tab; each open DM is its own tab
 * (live-driven from `useChatStore.openDmTabs`). Active tab determines which
 * conversation renders inline in the content area. There is no Sheet-based DM
 * overlay anymore — DMs are tabs, not modals.
 *
 * Dock chrome (resize + collapse) is local UI state persisted in localStorage.
 * Auth-token storage restrictions (DEC-6) do not apply to UI prefs.
 */
export function BottomDock() {
  const [profilePlayerId, setProfilePlayerId] = useState<string | null>(null);

  const [dockHeight, setDockHeight] = useState<number>(loadInitialHeight);
  const [isCollapsed, setIsCollapsed] = useState<boolean>(loadInitialCollapsed);
  const [isDragging, setIsDragging] = useState(false);

  const openDmTabs = useChatStore((s) => s.openDmTabs);
  const activeTabId = useChatStore((s) => s.activeTabId);
  const unreadByPlayerId = useChatStore((s) => s.unreadByPlayerId);
  const openDmTab = useChatStore((s) => s.openDmTab);
  const closeDmTab = useChatStore((s) => s.closeDmTab);
  const setActiveTab = useChatStore((s) => s.setActiveTab);

  // Battle-log tab visibility comes from two signals on the battle store:
  // - `battleId !== null` while BattleShell is mounted (live battle + result
  //   screen). Source of entries is the live `feedEntries`.
  // - `lastBattleLog !== null` after the user has returned to /lobby; this
  //   archive is captured at BattleEnded and survives the BattleShell
  //   unmount that resets the rest of the store. Source of entries is
  //   `lastBattleLog.entries`. Cleared by the next startBattle (so a fresh
  //   battle gets a fresh tab) or by the user clicking the tab's × button.
  const battleId = useBattleStore((s) => s.battleId);
  const liveFeedEntries = useBattleStore((s) => s.feedEntries);
  const lastBattleLog = useBattleStore((s) => s.lastBattleLog);
  const clearLastBattleLog = useBattleStore((s) => s.clearLastBattleLog);
  const liveTurnHistory = useBattleStore((s) => s.turnHistory);
  const lastTurnHistory = useBattleStore((s) => s.lastTurnHistory);
  const clearLastTurnHistory = useBattleStore((s) => s.clearLastTurnHistory);
  const battlePlayerAId = useBattleStore((s) => s.playerAId);
  const battlePlayerBId = useBattleStore((s) => s.playerBId);
  const liveBattlePlayerAName = useBattleStore((s) => s.playerAName);
  const liveBattlePlayerBName = useBattleStore((s) => s.playerBName);
  const battleActive = battleId !== null;
  const archivedActive = !battleActive && lastBattleLog !== null;
  const battleLogVisible = battleActive || archivedActive;
  const battleLogEntries: readonly BattleFeedEntry[] = battleActive
    ? liveFeedEntries
    : (lastBattleLog?.entries ?? []);
  const roundMapEntries: readonly TurnResolutionLogRealtime[] = battleActive
    ? liveTurnHistory
    : (lastTurnHistory ?? []);
  // Names follow the same active/archived split as entries so the RoundMap
  // header keeps showing real nicknames after reset() wipes the live store.
  const battlePlayerAName = battleActive
    ? liveBattlePlayerAName
    : (lastBattleLog?.playerAName ?? null);
  const battlePlayerBName = battleActive
    ? liveBattlePlayerBName
    : (lastBattleLog?.playerBName ?? null);

  // Local UI override that takes precedence over the chat store's activeTabId
  // when set. Kept out of the chat store so its tab-id type doesn't have to
  // grow a non-chat variant. Cleared whenever the user picks GENERAL or a DM,
  // or dismisses the BATTLE LOG tab via its × button.
  const [battleLogActive, setBattleLogActive] = useState(false);
  const isBattleLogActive = battleLogActive && battleLogVisible;

  // Auto-focus the BATTLE LOG tab whenever a battle starts (battleId
  // transitions null → value). The previous-id ref keeps this a one-shot per
  // battle — re-running the selector on unrelated store updates won't re-focus
  // the tab if the user has clicked elsewhere in the meantime. We do NOT
  // un-focus on battle end: the tab stays present (backed by lastBattleLog)
  // and keeping focus matches the user's intent if they were just reading it.
  const prevBattleIdRef = useRef<string | null>(null);
  useEffect(() => {
    const prev = prevBattleIdRef.current;
    prevBattleIdRef.current = battleId;
    if (battleId && !prev) {
      // Intentional: dock auto-switches to Battle Log tab when a battle starts.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setBattleLogActive(true);
    }
  }, [battleId]);

  const handleCloseBattleLog = () => {
    setBattleLogActive(false);
    clearLastBattleLog();
    clearLastTurnHistory();
  };

  // Defensive: if `activeTabId` references a DM that no longer exists in
  // openDmTabs (shouldn't happen — closeDmTab maintains the invariant), fall
  // back to General before reading.
  const activeDmTab: OpenDmTab | undefined =
    activeTabId === 'general'
      ? undefined
      : openDmTabs.find((t) => sameTabId(t.otherPlayerId, activeTabId));
  const effectiveActiveTabId: ChatTabId =
    activeTabId === 'general' || activeDmTab ? activeTabId : 'general';

  // Total live message count — used as the baseline for the collapsed-state
  // unread badge. Selectors return primitives so unrelated store mutations
  // (presence, connection state, errors) don't re-render the dock.
  // `directMessagesTotal` is maintained incrementally by the chat store so
  // this selector no longer scans every conversation on every store update.
  const globalLen = useChatStore((s) => s.globalMessages.length);
  const directLen = useChatStore((s) => s.directMessagesTotal);
  const totalMessages = globalLen + directLen;

  // Snapshot taken on entering the collapsed state. While collapsed, unread =
  // current total - snapshot (clamped to >= 0 because the per-conversation
  // 500-message buffer can trim during long sessions). Cleared on expand.
  //
  // Initial value: when the dock starts collapsed (persisted preference), the
  // baseline is anchored to the live `totalMessages` at mount so we don't
  // count pre-existing messages as unread. The lazy initializer captures
  // `totalMessages` exactly once at mount — no post-mount effect, no
  // exhaustive-deps suppression.
  const [collapseBaseline, setCollapseBaseline] = useState<number | null>(() =>
    loadInitialCollapsed() ? totalMessages : null,
  );

  const collapsedUnreadCount =
    isCollapsed && collapseBaseline !== null ? Math.max(0, totalMessages - collapseBaseline) : 0;

  // Persist height on every change. localStorage writes are cheap and only
  // fire from drag/keyboard interactions, not from render.
  useEffect(() => {
    try {
      localStorage.setItem(HEIGHT_STORAGE_KEY, String(dockHeight));
    } catch {
      // Storage unavailable (private mode, quota) — preference is ephemeral.
    }
  }, [dockHeight]);

  useEffect(() => {
    try {
      localStorage.setItem(COLLAPSED_STORAGE_KEY, String(isCollapsed));
    } catch {
      // Same as above.
    }
  }, [isCollapsed]);

  const handleCollapse = () => {
    setCollapseBaseline(totalMessages);
    setIsCollapsed(true);
  };
  const handleExpand = () => {
    setCollapseBaseline(null);
    setIsCollapsed(false);
  };

  const handleOpenDm = (otherPlayerId: string, displayName: string) => {
    setBattleLogActive(false);
    openDmTab(otherPlayerId, displayName);
    if (isCollapsed) handleExpand();
  };

  const panelHeight = isCollapsed ? COLLAPSED_HEIGHT : dockHeight;

  return (
    <>
      <div
        className="pointer-events-none fixed bottom-4 left-0 right-0 z-30 flex justify-center px-4"
        role="region"
        aria-label="Chat dock"
      >
        <div
          className={clsx(
            'pointer-events-auto relative flex w-full max-w-5xl items-stretch overflow-hidden rounded-[var(--radius-lg)] border-[0.5px] border-border-subtle bg-glass shadow-[var(--shadow-panel-lift)] backdrop-blur-[20px] transition-[height] duration-200 ease-out motion-reduce:transition-none',
            isDragging && '!transition-none',
          )}
          style={{ height: `${panelHeight}px` }}
        >
          {!isCollapsed && (
            <ResizeHandle
              dockHeight={dockHeight}
              setDockHeight={setDockHeight}
              setIsDragging={setIsDragging}
            />
          )}

          {isCollapsed ? (
            <CollapsedBar unreadCount={collapsedUnreadCount} onExpand={handleExpand} />
          ) : (
            <>
              {/* LEFT — chat column */}
              <div className="flex min-w-0 grow basis-3/4 flex-col">
                <ChatErrorDisplay />

                {/* Tab row */}
                <div className="relative flex h-9 shrink-0 items-stretch">
                  <div className="kombats-scroll flex min-w-0 flex-1 items-stretch overflow-x-auto">
                    <ChatTabButton
                      active={!isBattleLogActive && effectiveActiveTabId === 'general'}
                      onClick={() => {
                        setBattleLogActive(false);
                        setActiveTab('general');
                      }}
                      icon={<MessageCircle className="h-3.5 w-3.5" />}
                      label="General"
                    />
                    {battleLogVisible && (
                      <BattleLogTabButton
                        active={isBattleLogActive}
                        onActivate={() => setBattleLogActive(true)}
                        onClose={archivedActive ? handleCloseBattleLog : undefined}
                      />
                    )}
                    {openDmTabs.map((tab) => (
                      <DmTabButton
                        key={tab.otherPlayerId}
                        tab={tab}
                        active={
                          !isBattleLogActive && sameTabId(effectiveActiveTabId, tab.otherPlayerId)
                        }
                        unread={unreadByPlayerId.get(tab.otherPlayerId) ?? 0}
                        onActivate={() => {
                          setBattleLogActive(false);
                          setActiveTab(tab.otherPlayerId);
                        }}
                        onClose={() => closeDmTab(tab.otherPlayerId)}
                      />
                    ))}
                  </div>
                  <div className="flex shrink-0 items-center pr-1">
                    <CollapseButton onClick={handleCollapse} />
                  </div>
                  <SoftHairline edge="bottom" />
                </div>

                {/* Tab content */}
                {isBattleLogActive ? (
                  <div className="kombats-scroll flex min-h-0 flex-1 flex-col overflow-y-auto px-4 py-3">
                    <BattleLogList entries={battleLogEntries} />
                  </div>
                ) : effectiveActiveTabId === 'general' ? (
                  <>
                    <div className="kombats-scroll flex min-h-0 flex-1 flex-col overflow-y-auto px-4 py-3">
                      <GlobalMessagesList />
                    </div>
                    <div className="relative shrink-0 px-3 py-2">
                      <SoftHairline edge="top" />
                      <MessageInput />
                    </div>
                  </>
                ) : (
                  activeDmTab && (
                    <div className="flex min-h-0 flex-1 flex-col">
                      <DirectMessagePanel
                        // Re-mount when switching tabs so cursor/older-message
                        // state inside the panel resets cleanly.
                        key={activeDmTab.otherPlayerId}
                        otherPlayerId={activeDmTab.otherPlayerId}
                        displayName={activeDmTab.displayName}
                      />
                    </div>
                  )
                )}
              </div>

              {/* RIGHT — players column (or RoundMap when BATTLE LOG is active) */}
              <aside className="relative flex w-64 shrink-0 basis-1/4 flex-col">
                <VerticalSoftHairline />
                {isBattleLogActive ? (
                  roundMapEntries.length > 0 || battleActive ? (
                    <RoundMap
                      entries={roundMapEntries}
                      playerAId={battlePlayerAId}
                      playerBId={battlePlayerBId}
                      playerAName={battlePlayerAName}
                      playerBName={battlePlayerBName}
                    />
                  ) : (
                    <div className="flex flex-1 items-center justify-center px-4">
                      <p className="text-center text-[11px] uppercase tracking-[0.18em] text-text-muted">
                        No battle in progress
                      </p>
                    </div>
                  )
                ) : (
                  <>
                    <PlayersHeader />
                    <PlayersList onSendMessage={handleOpenDm} onViewProfile={setProfilePlayerId} />
                  </>
                )}
              </aside>
            </>
          )}
        </div>
      </div>

      {profilePlayerId && (
        <PlayerCard
          playerId={profilePlayerId}
          open={!!profilePlayerId}
          onClose={() => setProfilePlayerId(null)}
          onSendMessage={(otherPlayerId, displayName) => {
            handleOpenDm(otherPlayerId, displayName);
            setProfilePlayerId(null);
          }}
        />
      )}
    </>
  );
}

interface ResizeHandleProps {
  dockHeight: number;
  setDockHeight: (next: number | ((prev: number) => number)) => void;
  setIsDragging: (next: boolean) => void;
}

function ResizeHandle({ dockHeight, setDockHeight, setIsDragging }: ResizeHandleProps) {
  const dragRef = useRef<{ startY: number; startHeight: number } | null>(null);

  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    // Primary button only; ignore secondary/middle clicks.
    if (e.button !== 0) return;
    e.preventDefault();
    dragRef.current = { startY: e.clientY, startHeight: dockHeight };
    e.currentTarget.setPointerCapture(e.pointerId);
    setIsDragging(true);
  };

  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const start = dragRef.current;
    if (!start) return;
    // Drag up (clientY decreases) → grow. Invert the delta so the dock
    // tracks the cursor's vertical motion intuitively.
    const delta = start.startY - e.clientY;
    setDockHeight(clampHeight(start.startHeight + delta));
  };

  const endDrag = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return;
    dragRef.current = null;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId);
    }
    setIsDragging(false);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      setDockHeight((h) => clampHeight(h + KEYBOARD_STEP));
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      setDockHeight((h) => clampHeight(h - KEYBOARD_STEP));
    }
  };

  return (
    <div
      role="separator"
      aria-orientation="vertical"
      aria-label="Resize chat dock"
      aria-valuemin={MIN_HEIGHT}
      aria-valuemax={MAX_HEIGHT}
      aria-valuenow={dockHeight}
      aria-valuetext={`${dockHeight} pixels tall`}
      tabIndex={0}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onLostPointerCapture={endDrag}
      onKeyDown={onKeyDown}
      // Reserve the right edge so the collapse button (positioned in the tab
      // row immediately below) is never occluded by the drag-capture area.
      className="absolute left-0 right-9 top-0 z-10 h-1.5 cursor-row-resize touch-none focus:outline-none focus-visible:bg-accent/30"
    />
  );
}

interface CollapseButtonProps {
  onClick: () => void;
}

function CollapseButton({ onClick }: CollapseButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Collapse chat dock"
      title="Collapse"
      className="flex h-7 w-7 items-center justify-center rounded-sm text-text-muted transition-colors duration-150 hover:text-kombats-gold focus:outline-none focus-visible:text-kombats-gold"
    >
      <ChevronDown className="h-3.5 w-3.5" aria-hidden />
    </button>
  );
}

interface CollapsedBarProps {
  unreadCount: number;
  onExpand: () => void;
}

function CollapsedBar({ unreadCount, onExpand }: CollapsedBarProps) {
  return (
    <div className="flex w-full items-center gap-3 px-4">
      <MessageCircle className="h-3.5 w-3.5 shrink-0 text-accent" aria-hidden />
      <span className="text-[11px] font-medium uppercase tracking-[0.18em] text-text-muted">
        Chat
      </span>
      {unreadCount > 0 && (
        <span
          className="ml-1 inline-flex h-4 min-w-[16px] items-center justify-center rounded-full bg-kombats-crimson px-1.5 text-[10px] font-semibold text-text-primary tabular-nums"
          aria-label={`${unreadCount} unread messages`}
        >
          {unreadCount > 99 ? '99+' : unreadCount}
        </span>
      )}
      <button
        type="button"
        onClick={onExpand}
        aria-label="Expand chat dock"
        title="Expand"
        className="ml-auto flex h-6 w-6 items-center justify-center rounded-sm text-text-muted transition-colors duration-150 hover:text-kombats-gold focus:outline-none focus-visible:text-kombats-gold"
      >
        <ChevronUp className="h-3.5 w-3.5" aria-hidden />
      </button>
    </div>
  );
}

function ChatTabButton({
  active,
  onClick,
  icon,
  label,
}: {
  active: boolean;
  onClick: () => void;
  icon?: React.ReactNode;
  label: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={clsx(
        'relative flex h-full shrink-0 items-center gap-2 px-4 text-[11px] uppercase tracking-[0.18em] transition-colors duration-150 focus:outline-none',
        active
          ? 'text-text-primary'
          : 'text-text-muted hover:text-text-primary focus:text-text-primary',
      )}
    >
      {icon && <span className={clsx('flex', active && 'text-accent')}>{icon}</span>}
      <span>{label}</span>
      {active && (
        <span
          aria-hidden
          className="pointer-events-none absolute bottom-0 left-3 right-3 h-[2px] rounded-full bg-accent"
          style={{ boxShadow: ACTIVE_TAB_INDICATOR_SHADOW }}
        />
      )}
    </button>
  );
}

interface DmTabButtonProps {
  tab: OpenDmTab;
  active: boolean;
  unread: number;
  onActivate: () => void;
  onClose: () => void;
}

function DmTabButton({ tab, active, unread, onActivate, onClose }: DmTabButtonProps) {
  // Pulse only when the tab is inactive AND has unread inbound messages.
  // `addDirectMessage` will not increment unread for the focused sender, so
  // an active tab should already have unread === 0 — the `!active` guard is
  // defensive against any race where a tab is activated mid-frame.
  const shouldPulse = unread > 0 && !active;
  return (
    <div
      className={clsx(
        // 1px transparent border keeps every DM tab's layout identical;
        // `animate-tab-pulse` fades that border to gold and back.
        'relative flex h-full shrink-0 items-center rounded-md border border-transparent pl-3 pr-1.5 transition-colors duration-150',
        active ? 'text-text-primary' : 'text-text-muted hover:text-text-primary',
        shouldPulse && 'animate-tab-pulse',
      )}
    >
      <button
        type="button"
        onClick={onActivate}
        className="flex max-w-[140px] items-center gap-1.5 truncate text-[11px] uppercase tracking-[0.18em] focus:outline-none"
      >
        <span className="truncate normal-case tracking-normal">{tab.displayName}</span>
        {unread > 0 && (
          <span
            className="inline-flex h-4 min-w-[16px] items-center justify-center rounded-full bg-kombats-crimson px-1 text-[10px] font-semibold text-text-primary tabular-nums"
            aria-label={`${unread} unread`}
          >
            {unread > 99 ? '99+' : unread}
          </span>
        )}
      </button>
      <button
        type="button"
        onClick={onClose}
        aria-label={`Close conversation with ${tab.displayName}`}
        title="Close tab"
        className="ml-1 flex h-5 w-5 items-center justify-center rounded-sm text-text-muted transition-colors duration-150 hover:text-kombats-gold focus:outline-none focus-visible:text-kombats-gold"
      >
        <X className="h-3 w-3" aria-hidden />
      </button>
      {active && (
        <span
          aria-hidden
          className="pointer-events-none absolute bottom-0 left-3 right-3 h-[2px] rounded-full bg-accent"
          style={{ boxShadow: ACTIVE_TAB_INDICATOR_SHADOW }}
        />
      )}
    </div>
  );
}

interface BattleLogTabButtonProps {
  active: boolean;
  onActivate: () => void;
  // Present only while the tab is showing the archived `lastBattleLog` after
  // a battle has finished. During an active battle the tab is non-closeable.
  onClose?: () => void;
}

function BattleLogTabButton({ active, onActivate, onClose }: BattleLogTabButtonProps) {
  return (
    <div
      className={clsx(
        'relative flex h-full shrink-0 items-center pl-3',
        onClose ? 'pr-1.5' : 'pr-4',
        active ? 'text-text-primary' : 'text-text-muted hover:text-text-primary',
      )}
    >
      <button
        type="button"
        onClick={onActivate}
        className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em] focus:outline-none"
      >
        <span className={clsx('flex', active && 'text-accent')}>
          <Swords className="h-3.5 w-3.5" aria-hidden />
        </span>
        <span>Battle Log</span>
      </button>
      {onClose && (
        <button
          type="button"
          onClick={onClose}
          aria-label="Close battle log"
          title="Close tab"
          className="ml-1 flex h-5 w-5 items-center justify-center rounded-sm text-text-muted transition-colors duration-150 hover:text-kombats-gold focus:outline-none focus-visible:text-kombats-gold"
        >
          <X className="h-3 w-3" aria-hidden />
        </button>
      )}
      {active && (
        <span
          aria-hidden
          className="pointer-events-none absolute bottom-0 left-3 right-3 h-[2px] rounded-full bg-accent"
          style={{ boxShadow: ACTIVE_TAB_INDICATOR_SHADOW }}
        />
      )}
    </div>
  );
}

function SoftHairline({ edge }: { edge: 'top' | 'bottom' }) {
  return (
    <div
      aria-hidden
      className="pointer-events-none absolute inset-x-3 h-px"
      style={{ [edge]: 0, background: SOFT_HAIRLINE_BG_H }}
    />
  );
}

function VerticalSoftHairline() {
  return (
    <div
      aria-hidden
      className="pointer-events-none absolute bottom-3 left-0 top-3 w-px"
      style={{ background: SOFT_HAIRLINE_BG_V }}
    />
  );
}

function GlobalMessagesList() {
  const messages = useGlobalMessages();
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useScrollToBottom(messagesEndRef, messages.length);

  if (messages.length === 0) {
    return (
      <p className="py-6 text-center text-[11px] uppercase tracking-[0.18em] text-text-muted">
        No messages yet
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {messages.map((msg) => (
        <div key={msg.messageId} className="text-[13px] leading-relaxed">
          <span className="font-medium" style={{ color: getNickColor(msg.sender.playerId) }}>
            {msg.sender.displayName}:
          </span>{' '}
          <span className="text-text-primary">{msg.content}</span>
          <span className="ml-2 text-[10px] text-text-muted tabular-nums">
            {formatTimestamp(msg.sentAt)}
          </span>
        </div>
      ))}
      <div ref={messagesEndRef} />
    </div>
  );
}

function PlayersHeader() {
  const onlineCount = useOnlineCount();
  return (
    <div className="relative flex h-9 shrink-0 items-center gap-2 px-4">
      <Users className="h-3.5 w-3.5 text-accent" aria-hidden />
      <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
        Players in Chat
      </span>
      <span className="ml-auto text-[11px] text-text-muted tabular-nums">{onlineCount}</span>
      <SoftHairline edge="bottom" />
    </div>
  );
}

function PlayersList({
  onSendMessage,
  onViewProfile,
}: {
  onSendMessage: (playerId: string, displayName: string) => void;
  onViewProfile: (playerId: string) => void;
}) {
  const onlinePlayers = useOnlinePlayers();
  const currentIdentityId = useAuthStore((s) => s.userIdentityId);
  const playerList = useMemo<OnlinePlayerResponse[]>(
    () => Array.from(onlinePlayers.values()),
    [onlinePlayers],
  );

  if (playerList.length === 0) {
    return (
      <div className="flex-1 px-2 py-2">
        <p className="px-2 py-4 text-center text-[11px] uppercase tracking-[0.18em] text-text-muted">
          No players online
        </p>
      </div>
    );
  }

  return (
    <ul className="kombats-scroll flex-1 space-y-0.5 overflow-y-auto px-2 py-2">
      {playerList.map((player) => {
        // Case-insensitive compare — Keycloak `sub` and .NET `Guid` both
        // serialize lowercase, but the JWT layer (or HMR-warm cache) has been
        // observed to surface mixed-case values, which would silently hide
        // the DM action for every row. Normalizing both sides keeps the
        // self-only suppression robust.
        const isSelf =
          currentIdentityId !== null &&
          player.playerId.toLowerCase() === currentIdentityId.toLowerCase();
        return (
          <PlayerRow
            key={player.playerId}
            player={player}
            isSelf={isSelf}
            onViewProfile={onViewProfile}
            onSendMessage={onSendMessage}
          />
        );
      })}
    </ul>
  );
}

function PlayerRow({
  player,
  isSelf,
  onViewProfile,
  onSendMessage,
}: {
  player: OnlinePlayerResponse;
  isSelf: boolean;
  onViewProfile: (playerId: string) => void;
  onSendMessage: (playerId: string, displayName: string) => void;
}) {
  return (
    <li className="group flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 transition-colors hover:bg-white/[0.03]">
      <button
        type="button"
        onClick={() => onViewProfile(player.playerId)}
        className="flex min-w-0 flex-1 items-center gap-2 text-left"
      >
        <span
          aria-hidden
          className="h-1.5 w-1.5 shrink-0 rounded-full bg-kombats-jade"
          style={{ boxShadow: ONLINE_DOT_SHADOW }}
        />
        <span className="truncate text-xs" style={{ color: getNickColor(player.playerId) }}>
          {player.displayName}
        </span>
      </button>
      {!isSelf && (
        <button
          type="button"
          onClick={() => onSendMessage(player.playerId, player.displayName)}
          title="Send message"
          aria-label={`Send message to ${player.displayName}`}
          className="hidden rounded-sm px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-[0.18em] text-accent-text transition-colors duration-150 hover:text-kombats-gold group-hover:inline-flex"
        >
          DM
        </button>
      )}
    </li>
  );
}
