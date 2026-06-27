import { Navigate, Outlet } from 'react-router';
import { useAuthStore } from '@/modules/auth/store';
import { SplashScreen } from '@/ui/components/SplashScreen';
import { decideAuthGuard } from './guard-decisions';

export function AuthGuard() {
  const authStatus = useAuthStore((s) => s.authStatus);
  const decision = decideAuthGuard(authStatus);

  if (decision.type === 'loading') {
    return <SplashScreen />;
  }

  if (decision.type === 'navigate') return <Navigate to={decision.to} replace />;
  return <Outlet />;
}
