const supportBase = 'https://hub.sportarr.net/api/support';
const tokenKey = 'sportarr-support-connection';

export interface SupportIdentity {
  session_id: string;
  connected_providers: string[];
  expires_at: string;
  github_posting: boolean;
  github_login: string | null;
}

export class SupportApiError extends Error {
  constructor(message: string, readonly status: number, readonly retryAfterSeconds: number | null = null) { super(message); }
}

export function getSupportToken() {
  return localStorage.getItem(tokenKey);
}

export function saveSupportToken(token: string) {
  localStorage.setItem(tokenKey, token);
}

export function clearSupportToken() {
  localStorage.removeItem(tokenKey);
}

export async function supportRequest<T>(path: string, init: RequestInit = {}, token = getSupportToken()): Promise<T> {
  const headers = new Headers(init.headers);
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  const response = await fetch(`${supportBase}${path}`, { ...init, headers });
  if (!response.ok) {
    const payload = await response.json().catch(() => ({})) as { detail?: string | { message?: string } | { msg?: string }[] };
    const detail = typeof payload.detail === 'string' ? payload.detail
      : Array.isArray(payload.detail) ? payload.detail[0]?.msg
      : payload.detail?.message;
    const retryHeader = response.headers.get('Retry-After');
    const retryAfterSeconds = retryHeader && /^\d+$/.test(retryHeader)
      ? Number(retryHeader) : null;
    throw new SupportApiError(detail || 'Support is unavailable right now. Try again shortly.',
      response.status, retryAfterSeconds !== null && Number.isSafeInteger(retryAfterSeconds) && retryAfterSeconds > 0
        ? retryAfterSeconds : null);
  }
  return response.json() as Promise<T>;
}

export function supportIdentity() {
  return supportRequest<SupportIdentity>('/me');
}

export function disconnectSupport() {
  return supportRequest<{ status: string }>('/me', { method: 'DELETE' });
}

export function startSupportPairing(provider: 'github' | 'discord') {
  return supportRequest<{ pairing_id: string; secret: string; approval_secret: string; approve_url: string; expires_at: string }>(
    '/pair/start', { method: 'POST', body: JSON.stringify({ origin: window.location.origin, provider }) }, null
  );
}

export function completeSupportPairing(pairingId: string, secret: string) {
  return supportRequest<{ status: 'pending' | 'connected'; token?: string }>(
    '/pair/complete', { method: 'POST', body: JSON.stringify({ pairing_id: pairingId, secret }) }, null
  );
}
