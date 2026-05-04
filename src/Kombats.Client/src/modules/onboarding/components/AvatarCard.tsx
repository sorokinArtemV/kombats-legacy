import { clsx } from 'clsx';
import { getAvatarAsset, SELECTABLE_AVATARS } from '@/modules/player/avatar-assets';

interface AvatarCardProps {
  avatar: (typeof SELECTABLE_AVATARS)[number];
  selected: boolean;
  disabled: boolean;
  onSelect: () => void;
  // Roving-tabindex coordination: only the selected card is in the tab order
  // (`0`); the rest are `-1` and reachable via arrow keys handled by the grid
  // parent. Defaults to native focusable when omitted.
  tabIndex?: number;
  buttonRef?: React.Ref<HTMLButtonElement>;
}

/**
 * One tile in the 5-card avatar grid. Selection is conveyed by two
 * non-glowing signals: (1) opacity contrast — non-selected cards are dimmed,
 * the selected card is at full brightness; (2) a sharp gold underline pinned
 * to the bottom edge of the selected card (clipped by the rounded corners,
 * tab-style). No outer shadows or filter glows — pure pixels.
 */
export function AvatarCard({
  avatar,
  selected,
  disabled,
  onSelect,
  tabIndex,
  buttonRef,
}: AvatarCardProps) {
  return (
    <button
      ref={buttonRef}
      type="button"
      onClick={onSelect}
      aria-pressed={selected}
      aria-label={`Choose avatar ${avatar.name}`}
      disabled={disabled}
      tabIndex={tabIndex}
      className={clsx(
        'group relative block aspect-[2/3] w-full overflow-hidden rounded-md border-[0.5px] border-border-subtle transition-opacity duration-200 focus:outline-none disabled:cursor-not-allowed',
        selected ? 'opacity-100' : 'opacity-55 hover:opacity-90 focus-visible:opacity-90',
      )}
    >
      {/* Background fill so any letterbox edges blend into the panel. */}
      <div
        aria-hidden
        className="absolute inset-0 bg-gradient-to-b from-kombats-smoke-gray/70 via-kombats-ink-navy/80 to-kombats-ink-navy"
      />

      <img
        src={getAvatarAsset(avatar.id)}
        alt=""
        aria-hidden
        draggable={false}
        className="absolute inset-0 h-full w-full object-cover"
        style={{ objectPosition: avatar.focal }}
      />

      {selected && (
        <div aria-hidden className="absolute inset-x-0 bottom-0 h-[2px] bg-accent-primary" />
      )}
    </button>
  );
}
