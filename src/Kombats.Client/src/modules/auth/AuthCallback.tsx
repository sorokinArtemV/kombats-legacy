import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router';
import { useAuth } from 'react-oidc-context';
import { SplashScreen } from '@/ui/components/SplashScreen';

export function AuthCallback() {
  const auth = useAuth();
  const navigate = useNavigate();
  // One-shot per mount: oidc-client-ts settles `isLoading`/`activeNavigator`
  // before `isAuthenticated`/`user`/`error` finish flipping, so the effect
  // can fire its terminal navigate twice as those tail props change. The
  // ref is reset on remount, so a later return to /auth/callback (e.g.,
  // post-password-change) still navigates correctly.
  const navigatedRef = useRef(false);

  useEffect(() => {
    if (auth.isLoading || auth.activeNavigator) {
      return;
    }
    if (navigatedRef.current) {
      return;
    }
    navigatedRef.current = true;

    if (auth.isAuthenticated) {
      navigate('/lobby', { replace: true });
    } else {
      navigate('/', { replace: true });
    }
  }, [auth.isLoading, auth.activeNavigator, auth.isAuthenticated, navigate]);

  return <SplashScreen />;
}
