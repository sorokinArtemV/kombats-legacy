import './index.css';
import { loadConfig } from './config';

// Bootstrap order matters: /config.json must be loaded BEFORE any module
// that reads `config()` is evaluated (user-manager, http client, SignalR
// hubs). We satisfy that by static-importing only `./index.css` and
// `./config`, then dynamic-importing the rest after `await loadConfig()`.
//
// /silent-renew also goes through loadConfig() — the iframe pays one small
// fetch so it can reuse the same UserManager singleton; signinSilentCallback
// itself reads OIDC state from the URL hash, not from config.
async function bootstrap(): Promise<void> {
  try {
    await loadConfig();
  } catch (e) {
    document.body.innerHTML =
      '<div style="font-family:sans-serif;padding:2em;color:#c00">' +
      '<h1>Configuration error</h1>' +
      `<p>${(e as Error).message}</p>` +
      '</div>';
    return;
  }

  if (window.location.pathname === '/silent-renew') {
    const [{ userManager }, { logger }] = await Promise.all([
      import('./modules/auth/user-manager'),
      import('./app/logger'),
    ]);
    userManager.signinSilentCallback().catch((err) => {
      logger.warn('signinSilentCallback failed', err);
    });
    return;
  }

  const [{ StrictMode }, { createRoot }, { App }] = await Promise.all([
    import('react'),
    import('react-dom/client'),
    import('./app/App'),
  ]);

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

void bootstrap();
