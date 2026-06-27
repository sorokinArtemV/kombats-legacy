import { useState } from 'react';
import { useMatchmaking } from '../hooks';
import { useMatchmakingStore } from '../store';
import { Spinner } from '@/ui/components/Spinner';
import { isApiError } from '@/types/api';

export function QueueButton() {
  const { status, joinQueue } = useMatchmaking();
  const requeueFailed = useMatchmakingStore((s) => s.requeueFailed);
  const setRequeueFailed = useMatchmakingStore((s) => s.setRequeueFailed);
  const [joining, setJoining] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleJoin = async () => {
    setJoining(true);
    setErrorMessage(null);
    try {
      await joinQueue();
    } catch (err: unknown) {
      setErrorMessage(extractMessage(err));
    } finally {
      setJoining(false);
    }
  };

  const disabled = status !== 'idle' || joining;

  return (
    <div className="flex flex-col items-center gap-2">
      <button
        type="button"
        onClick={handleJoin}
        disabled={disabled}
        className="inline-flex items-center justify-center gap-2 rounded-md bg-accent-primary px-10 py-4 text-[15px] font-medium uppercase tracking-[0.18em] text-text-on-accent transition-colors duration-150 hover:bg-kombats-gold-light disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:bg-accent-primary"
      >
        {joining && <Spinner size="sm" />}
        <span>{joining ? 'Joining…' : 'Join Queue'}</span>
      </button>
      {errorMessage && (
        <p
          className="text-center text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light"
          role="alert"
        >
          {errorMessage}
        </p>
      )}
      {/* Set by useRequeueAfterBattle when the bounded retry exhausts on a
          stale-match condition (the previous battle's BattleCompleted
          projection had not landed in time). Lets the user know why their
          re-queue dropped them back here, and dismisses on click or on the
          next successful join (joinQueue clears the flag up-front). */}
      {!errorMessage && requeueFailed && (
        <button
          type="button"
          onClick={() => setRequeueFailed(false)}
          className="rounded-sm px-2 py-1 text-center text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light transition-colors duration-150 hover:text-text-primary focus:outline-none focus:text-text-primary"
          aria-label="Dismiss requeue failure notice"
        >
          Couldn't auto-requeue — try again.
        </button>
      )}
    </div>
  );
}

function extractMessage(err: unknown): string {
  if (isApiError(err)) {
    if (err.status >= 500) return 'Matchmaking is temporarily unavailable.';
    if (err.error?.message) return err.error.message;
  }
  return 'Could not join the queue. Please try again.';
}
