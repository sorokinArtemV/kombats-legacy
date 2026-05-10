import { config } from '@/config';
import type { ApiError } from '@/types/api';

const BASE_URL = config().bff.baseUrl;

// ---------------------------------------------------------------------------
// Dependency injection — wired by app/ layer at startup
// ---------------------------------------------------------------------------

let _getAccessToken: () => string | null = () => null;
let _onAuthFailure: () => void = () => {};

export function configureHttpClient(deps: {
  getAccessToken: () => string | null;
  onAuthFailure: () => void;
}): void {
  _getAccessToken = deps.getAccessToken;
  _onAuthFailure = deps.onAuthFailure;
}

// ---------------------------------------------------------------------------
// Error parsing
// ---------------------------------------------------------------------------

async function parseErrorResponse(response: Response): Promise<ApiError> {
  try {
    const body = await response.json();
    if (body?.error) {
      return {
        error: {
          code: body.error.code ?? 'unknown',
          message: body.error.message ?? response.statusText,
          details: body.error.details,
        },
        status: response.status,
      };
    }
  } catch {
    // Response body is not JSON — fall through to generic error
  }

  return {
    error: {
      code: 'unknown',
      message: response.statusText || `HTTP ${response.status}`,
    },
    status: response.status,
  };
}

// ---------------------------------------------------------------------------
// Core request function
// ---------------------------------------------------------------------------

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = _getAccessToken();

  // Normalize any RequestInit['headers'] shape (Headers | string[][] | object)
  // into a plain Record so subsequent injections cannot silently no-op on
  // non-object inputs.
  const headers: Record<string, string> = {};
  new Headers(init?.headers).forEach((value, key) => {
    headers[key] = value;
  });

  if (init?.body !== undefined && init?.body !== null) {
    headers['Content-Type'] = 'application/json';
  }

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers,
  });

  if (response.status === 401) {
    _onAuthFailure();
    throw await parseErrorResponse(response);
  }

  if (!response.ok) {
    throw await parseErrorResponse(response);
  }

  // 204 No Content — return undefined as T
  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export const httpClient = {
  get<T>(path: string): Promise<T> {
    return request<T>(path, { method: 'GET' });
  },

  post<T>(path: string, body?: unknown): Promise<T> {
    return request<T>(path, {
      method: 'POST',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  },

  put<T>(path: string, body?: unknown): Promise<T> {
    return request<T>(path, {
      method: 'PUT',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  },

  delete<T>(path: string): Promise<T> {
    return request<T>(path, { method: 'DELETE' });
  },

  // Fire-and-forget POST that survives page unload via the `keepalive` flag.
  // Use this for pagehide/beforeunload paths where a normal fetch would be
  // aborted before the browser tears the document down. Auth headers are
  // attached the same way as a regular request so the BFF can authorize.
  postKeepalive(path: string, body: unknown): void {
    const token = _getAccessToken();
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (token) headers['Authorization'] = `Bearer ${token}`;
    try {
      void fetch(`${BASE_URL}${path}`, {
        method: 'POST',
        body: JSON.stringify(body),
        headers,
        keepalive: true,
      }).catch(() => {});
    } catch {
      // Browser refused the request (e.g. payload exceeds the per-origin
      // keepalive budget). Nothing we can do here — the heartbeat sweep
      // is the safety net.
    }
  },
};
