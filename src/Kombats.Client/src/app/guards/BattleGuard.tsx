import { Navigate, Outlet, useLocation } from 'react-router';
import { usePlayerStore } from '@/modules/player/store';
import { useBattleStore } from '@/modules/battle/store';
import { decideBattleGuard } from './guard-decisions';

export function BattleGuard() {
  const queueStatus = usePlayerStore((s) => s.queueStatus);
  const battlePhase = useBattleStore((s) => s.phase);
  const storeBattleId = useBattleStore((s) => s.battleId);
  const location = useLocation();

  const decision = decideBattleGuard(queueStatus, battlePhase, storeBattleId, location.pathname);

  if (decision.type === 'navigate') return <Navigate to={decision.to} replace />;
  return <Outlet />;
}
