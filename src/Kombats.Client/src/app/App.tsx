import { RouterProvider } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from '@/modules/auth/AuthProvider';
import { ErrorBoundary } from '@/ui/components/ErrorBoundary';
import { AppCrashScreen } from './AppCrashScreen';
import { logger } from './logger';
import { queryClient } from './query-client';
import { router } from './router';
import './transport-init';

export function App() {
  return (
    <ErrorBoundary
      fallback={<AppCrashScreen />}
      onError={(err) => logger.error('App render error', err)}
    >
      <AuthProvider>
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </AuthProvider>
    </ErrorBoundary>
  );
}
