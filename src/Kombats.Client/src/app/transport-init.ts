import { useAuthStore } from '@/modules/auth/store';
import { getAccessToken } from '@/modules/auth/user-manager';
import { configureHttpClient } from '@/transport/http/client';
import { BattleHubManager } from '@/transport/signalr/battle-hub';
import { ChatHubManager } from '@/transport/signalr/chat-hub';

function accessTokenFactory(): string {
  const token = getAccessToken();
  if (!token) {
    // SignalR reads this synchronously at connect time and on each
    // reconnect. Returning '' used to silently produce an unauthenticated
    // connect attempt that the server rejected with a cryptic handshake
    // error; throwing makes the failure explicit to the hub managers'
    // existing error paths (they surface the reconnect state, which
    // drives the battle error phase / chat banner).
    throw new Error('No access token available for SignalR connection');
  }
  return token;
}

function onAuthFailure(): void {
  useAuthStore.getState().clearAuth();
}

// Wire HTTP client
configureHttpClient({ getAccessToken, onAuthFailure });

// Create SignalR manager singletons with injected token factory
export const battleHubManager = new BattleHubManager(accessTokenFactory);
export const chatHubManager = new ChatHubManager(accessTokenFactory);
