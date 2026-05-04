import { MessageCircle } from 'lucide-react';
import { usePlayerCard } from '../hooks';
import { useAuthStore } from '@/modules/auth/store';
import { KpiTile } from '@/ui/components/KpiTile';
import { Sheet } from '@/ui/components/Sheet';
import { Spinner } from '@/ui/components/Spinner';
import { isApiError } from '@/types/api';

interface PlayerCardProps {
  playerId: string;
  open: boolean;
  onClose: () => void;
  onSendMessage?: (playerId: string, displayName: string) => void;
}

function getErrorMessage(error: unknown): string {
  if (isApiError(error) && error.status === 404) return 'Player not found';
  return "Couldn't load profile";
}

// Cinzel name bloom matching DESIGN_REFERENCE.md §3.4 — gold halo behind
// the display name.
const nameBloomStyle = {
  textShadow: 'var(--shadow-title-soft)',
};

export function PlayerCard({ playerId, open, onClose, onSendMessage }: PlayerCardProps) {
  const { data, isPending, isError, error } = usePlayerCard(playerId, open);
  const currentIdentityId = useAuthStore((s) => s.userIdentityId);
  const isSelf = currentIdentityId !== null && currentIdentityId === playerId;
  const canSendMessage = !!onSendMessage && !isSelf;

  return (
    <Sheet open={open} onClose={onClose} title="Player Profile">
      <div className="px-5 py-5">
        {isPending && (
          <div className="flex items-center justify-center py-10">
            <Spinner />
          </div>
        )}

        {isError && (
          <p className="py-10 text-center text-sm text-kombats-crimson-light">
            {getErrorMessage(error)}
          </p>
        )}

        {data && (
          <div className="flex flex-col gap-5">
            <div className="flex flex-col items-center gap-2 border-b-[0.5px] border-border-divider pb-5">
              <h3
                className="font-display text-[22px] font-semibold uppercase tracking-[0.20em] text-accent-primary"
                style={nameBloomStyle}
              >
                {data.displayName}
              </h3>
              <p className="text-[11px] uppercase tracking-[0.24em] text-text-muted">
                Level {data.level}
              </p>
            </div>

            <div>
              <p className="mb-2 text-[10px] uppercase tracking-[0.24em] text-text-muted">
                Attributes
              </p>
              <div className="grid grid-cols-2 gap-2">
                <StatRow label="Strength" value={data.strength} />
                <StatRow label="Agility" value={data.agility} />
                <StatRow label="Intuition" value={data.intuition} />
                <StatRow label="Vitality" value={data.vitality} />
              </div>
            </div>

            <div>
              <p className="mb-2 text-[10px] uppercase tracking-[0.24em] text-text-muted">Record</p>
              <div className="grid grid-cols-2 gap-2">
                <KpiTile label="Wins" value={data.wins} tone="jade" />
                <KpiTile label="Losses" value={data.losses} tone="crimson" />
              </div>
            </div>

            {canSendMessage && (
              <button
                type="button"
                onClick={() => {
                  onSendMessage!(playerId, data.displayName);
                  onClose();
                }}
                className="flex items-center justify-center gap-2 rounded-sm border-[0.5px] border-kombats-gold/50 px-4 py-2.5 text-[11px] font-medium uppercase tracking-[0.24em] text-kombats-gold transition-colors duration-150 hover:bg-kombats-gold/10 focus:outline-none focus-visible:bg-kombats-gold/10"
              >
                <MessageCircle className="h-3.5 w-3.5" aria-hidden />
                <span>Send Message</span>
              </button>
            )}
          </div>
        )}
      </div>
    </Sheet>
  );
}

function StatRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-center justify-between rounded-sm border-[0.5px] border-border-subtle bg-glass-subtle px-3 py-2">
      <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">{label}</span>
      <span className="text-sm font-medium tabular-nums text-text-primary">{value}</span>
    </div>
  );
}
