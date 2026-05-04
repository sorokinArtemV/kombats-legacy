import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import { App } from './app/App';
import { logger } from './app/logger';
import { userManager } from './modules/auth/user-manager';

// Silent-renew iframe callback: when UserManager does a prompt=none SSO check
// via a hidden iframe, the OP redirects that iframe back to /silent-renew.
// Here we process the auth response and signal the parent frame — we must NOT
// mount the full app inside the iframe (it would navigate, overwrite the URL,
// and break the callback parsing).
if (window.location.pathname === '/silent-renew') {
  userManager.signinSilentCallback().catch((err) => {
    logger.warn('signinSilentCallback failed', err);
  });
} else {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}
