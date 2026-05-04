import { Navigate, Outlet, useLocation } from 'react-router';
import { usePlayerStore } from '@/modules/player/store';
import { decideOnboardingGuard } from './guard-decisions';

export function OnboardingGuard() {
  const character = usePlayerStore((s) => s.character);
  const location = useLocation();

  const decision = decideOnboardingGuard(character, location.pathname);
  if (decision.type === 'navigate') return <Navigate to={decision.to} replace />;
  return <Outlet />;
}
