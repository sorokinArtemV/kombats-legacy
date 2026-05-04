import { Outlet } from 'react-router';
import { useGameState } from '@/modules/player/hooks';
import { useAutoOnboard } from '@/modules/onboarding/hooks';
import { SplashScreen } from '@/ui/components/SplashScreen';

function errorMessage(error: unknown): string | undefined {
  return error instanceof Error ? error.message : undefined;
}

export function GameStateLoader() {
  const { isPending, isError, error, refetch } = useGameState();
  const onboard = useAutoOnboard();

  if (isPending) {
    return <SplashScreen />;
  }

  if (isError) {
    return (
      <div
        className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-primary"
        role="alert"
      >
        <p className="text-error">Failed to load game state</p>
        <p className="text-sm text-text-muted">
          {errorMessage(error) ?? 'An unexpected error occurred.'}
        </p>
        <button
          onClick={() => refetch()}
          className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-text-primary transition-colors hover:bg-accent-hover"
        >
          Retry
        </button>
      </div>
    );
  }

  // Auto-onboard in progress
  if (onboard.isPending) {
    return <SplashScreen />;
  }

  // Auto-onboard failed
  if (onboard.isError) {
    return (
      <div
        className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-primary"
        role="alert"
      >
        <p className="text-error">Failed to create character</p>
        <p className="text-sm text-text-muted">
          {errorMessage(onboard.error) ?? 'An unexpected error occurred.'}
        </p>
        <button
          onClick={onboard.retry}
          className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-text-primary transition-colors hover:bg-accent-hover"
        >
          Retry
        </button>
      </div>
    );
  }

  return <Outlet />;
}
