import { createBrowserRouter } from 'react-router';
import { AuthCallback } from '@/modules/auth/AuthCallback';
import { UnauthenticatedShell } from './shells/UnauthenticatedShell';
import { OnboardingShell } from './shells/OnboardingShell';
import { SessionShell } from './shells/SessionShell';
import { LobbyShell } from './shells/LobbyShell';
import { BattleShell } from './shells/BattleShell';
import { AuthGuard } from './guards/AuthGuard';
import { OnboardingGuard } from './guards/OnboardingGuard';
import { BattleGuard } from './guards/BattleGuard';
import { GameStateLoader } from './GameStateLoader';
import { AppCrashScreen } from './AppCrashScreen';
import { NotFoundScreen } from './NotFoundScreen';
import { NameSelectionScreen } from '@/modules/onboarding/screens/NameSelectionScreen';
import { InitialStatsScreen } from '@/modules/onboarding/screens/InitialStatsScreen';
import { LobbyScreen } from '@/modules/player/screens/LobbyScreen';
import { BattleScreen } from '@/modules/battle/screens/BattleScreen';
import { BattleResultScreen } from '@/modules/battle/screens/BattleResultScreen';

export const router = createBrowserRouter([
  // Unauthenticated landing
  {
    path: '/',
    element: <UnauthenticatedShell />,
  },

  // OIDC callback
  {
    path: '/auth/callback',
    element: <AuthCallback />,
  },

  // Authenticated routes — guarded by AuthGuard + GameStateLoader
  {
    element: <AuthGuard />,
    children: [
      {
        element: <GameStateLoader />,
        children: [
          {
            element: <OnboardingGuard />,
            children: [
              // Onboarding routes (only reachable when Draft/Named/no character)
              {
                element: <OnboardingShell />,
                errorElement: <AppCrashScreen />,
                children: [
                  { path: '/onboarding/name', element: <NameSelectionScreen /> },
                  { path: '/onboarding/stats', element: <InitialStatsScreen /> },
                ],
              },

              // Post-onboarding: session shell owns chat connection lifecycle
              // above the BattleGuard split so it survives lobby ↔ battle nav
              {
                element: <SessionShell />,
                children: [
                  {
                    element: <BattleGuard />,
                    children: [
                      // Battle routes (only reachable when matched)
                      {
                        element: <BattleShell />,
                        errorElement: <AppCrashScreen />,
                        children: [
                          { path: '/battle/:battleId', element: <BattleScreen /> },
                          {
                            path: '/battle/:battleId/result',
                            element: <BattleResultScreen />,
                          },
                        ],
                      },

                      // Lobby (normal authenticated flow) — single route
                      // that switches its center overlay based on derived
                      // queue UI status, so background/sprite stay mounted
                      // across idle ↔ searching ↔ matched transitions.
                      // No per-group errorElement; lobby crashes bubble up
                      // to the top-level ErrorBoundary in App.tsx.
                      {
                        element: <LobbyShell />,
                        children: [{ path: '/lobby', element: <LobbyScreen /> }],
                      },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  },

  // Catch-all: any unknown URL renders the branded 404 instead of falling
  // through to React Router's default error surface. Placed at the top
  // level so it only matches when no other route does — existing guards
  // (AuthGuard, OnboardingGuard, BattleGuard) still own their redirects
  // for known protected paths.
  {
    path: '*',
    element: <NotFoundScreen />,
  },
]);
