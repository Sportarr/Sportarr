import { useEffect, useRef, useState } from 'react';
import { CheckCircleIcon } from '@heroicons/react/24/outline';
import {
  clearSupportToken, completeSupportPairing, disconnectSupport, getSupportToken, SupportApiError,
  saveSupportToken, startSupportPairing,
  supportIdentity, type SupportIdentity,
} from '../api/supportConnection';
import { BUTTON_PRIMARY, BUTTON_SECONDARY } from '../utils/designTokens';

interface Props {
  onIdentityChange?: (identity: SupportIdentity | null) => void;
}

export default function SupportConnection({ onIdentityChange }: Props) {
  const [identity, setIdentity] = useState<SupportIdentity | null>(null);
  const [loading, setLoading] = useState(() => !!getSupportToken());
  const [connectionUnavailable, setConnectionUnavailable] = useState(false);
  const [busy, setBusy] = useState(false);
  const [stage, setStage] = useState<'sign-in' | 'github-permission' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const polling = useRef<ReturnType<typeof setInterval> | null>(null);
  const pollInFlight = useRef(false);
  const popupRef = useRef<Window | null>(null);
  const messageHandler = useRef<((event: MessageEvent) => void) | null>(null);
  const attempt = useRef(0);
  const pendingIdentity = useRef<SupportIdentity | null>(null);

  function stopWaiting(closePopup = false) {
    attempt.current += 1;
    if (polling.current) clearInterval(polling.current);
    polling.current = null;
    if (messageHandler.current) window.removeEventListener('message', messageHandler.current);
    messageHandler.current = null;
    if (closePopup && popupRef.current && !popupRef.current.closed) popupRef.current.close();
    popupRef.current = null;
    pollInFlight.current = false;
  }

  useEffect(() => {
    if (getSupportToken()) {
      supportIdentity().then((value) => { setIdentity(value); onIdentityChange?.(value); }).catch((failure) => {
        if (failure instanceof SupportApiError && [401, 403].includes(failure.status)) {
          clearSupportToken();
          onIdentityChange?.(null);
        } else {
          setConnectionUnavailable(true);
        }
      }).finally(() => setLoading(false));
    } else {
      setLoading(false);
      onIdentityChange?.(null);
    }
    return () => { stopWaiting(); };
  }, [onIdentityChange]);

  async function connectAccount(provider: 'github' | 'discord') {
    const currentAttempt = ++attempt.current;
    setBusy(true);
    setStage('sign-in');
    setError(null);
    const popup = window.open('', '_blank');
    if (!popup) {
      setBusy(false);
      setStage(null);
      setError('Your browser blocked the sign-in tab. Allow popups for Sportarr and try again.');
      return;
    }
    popupRef.current = popup;
    try {
      const pairing = await startSupportPairing(provider);
      if (attempt.current !== currentAttempt) return;
      const hubOrigin = new URL(pairing.approve_url).origin;
      const handler = (event: MessageEvent) => {
        if (event.source !== popup || event.origin !== hubOrigin) return;
        if (event.data?.type !== 'sportarr-support-pairing-ready' || event.data.pairing_id !== pairing.pairing_id) return;
        popup.postMessage({ type: 'sportarr-support-pairing-proof', pairing_id: pairing.pairing_id,
          approval_secret: pairing.approval_secret }, hubOrigin);
      };
      messageHandler.current = handler;
      window.addEventListener('message', handler);
      popup.location.href = pairing.approve_url;
      const parsedExpiry = Date.parse(pairing.expires_at);
      const maximumExpiry = Date.now() + 10 * 60 * 1000;
      const expiresAt = Number.isFinite(parsedExpiry) ? Math.min(parsedExpiry, maximumExpiry) : maximumExpiry;
      let failedChecks = 0;
      polling.current = setInterval(async () => {
        if (attempt.current !== currentAttempt) return;
        if (pollInFlight.current) return;
        if (Date.now() >= expiresAt) {
          stopWaiting();
          setBusy(false);
          setStage(null);
          setError('The sign-in request expired. Try again.');
          return;
        }
        pollInFlight.current = true;
        try {
          const status = await completeSupportPairing(pairing.pairing_id, pairing.secret);
          if (attempt.current !== currentAttempt) return;
          failedChecks = 0;
          setError(null);
          if (status.status === 'connected' && status.token) {
            stopWaiting();
            const acceptedAttempt = attempt.current;
            saveSupportToken(status.token);
            try {
              const value = await supportIdentity();
              if (attempt.current !== acceptedAttempt) return;
              if (provider === 'github' && !value.github_posting) {
                void connectGithubPosting(value.session_id, popup, value);
              } else {
                setIdentity(value);
                onIdentityChange?.(value);
                setBusy(false);
                setStage(null);
              }
            } catch (failure) {
              if (attempt.current !== acceptedAttempt) return;
              setBusy(false);
              setStage(null);
              if (failure instanceof SupportApiError && [401, 403].includes(failure.status)) {
                clearSupportToken();
                setError('The connection could not be confirmed. Try again.');
              } else {
                setConnectionUnavailable(true);
              }
            }
          }
        } catch {
          failedChecks += 1;
          if (failedChecks >= 3) setError('Could not check the connection yet. Sportarr is still waiting.');
        } finally {
          pollInFlight.current = false;
        }
      }, 2000);
    } catch (failure) {
      if (attempt.current !== currentAttempt) return;
      stopWaiting(true);
      setBusy(false);
      setStage(null);
      setError(failure instanceof SupportApiError && failure.message === 'Invalid app origin'
        ? 'This Sportarr address is not enabled for Support sign-in yet. Please contact Sportarr support so it can be allowed.'
        : failure instanceof Error ? failure.message : 'Could not start connection.');
    }
  }

  function cancelConnection() {
    stopWaiting(true);
    if (pendingIdentity.current) {
      setIdentity(pendingIdentity.current);
      onIdentityChange?.(pendingIdentity.current);
      pendingIdentity.current = null;
    }
    setBusy(false);
    setStage(null);
    setError(null);
  }

  async function retryIdentity() {
    setLoading(true);
    try {
      const value = await supportIdentity();
      setIdentity(value);
      setConnectionUnavailable(false);
      onIdentityChange?.(value);
    } catch (failure) {
      if (failure instanceof SupportApiError && [401, 403].includes(failure.status)) {
        clearSupportToken();
        setConnectionUnavailable(false);
        onIdentityChange?.(null);
      }
    } finally {
      setLoading(false);
    }
  }

  function connectGithubPosting(sessionId: string, existingPopup?: Window, fallbackIdentity?: SupportIdentity) {
    const currentAttempt = ++attempt.current;
    setBusy(true);
    setStage('github-permission');
    setError(null);
    if (fallbackIdentity) pendingIdentity.current = fallbackIdentity;
    const url = `https://sportarr.net/support/github-authorize/${sessionId}`;
    const popup = existingPopup && !existingPopup.closed ? existingPopup : window.open('', '_blank');
    if (!popup) {
      setBusy(false);
      setStage(null);
      setError('Your browser blocked GitHub approval. Allow popups and try again.');
      if (fallbackIdentity) { setIdentity(fallbackIdentity); onIdentityChange?.(fallbackIdentity); }
      pendingIdentity.current = null;
      return;
    }
    popupRef.current = popup;
    try {
      popup.location.href = url;
      const expiresAt = Date.now() + 10 * 60 * 1000;
      let failedChecks = 0;
      polling.current = setInterval(async () => {
        if (attempt.current !== currentAttempt) return;
        if (pollInFlight.current) return;
        if (Date.now() >= expiresAt) {
          stopWaiting();
          setBusy(false);
          setStage(null);
          if (fallbackIdentity) { setIdentity(fallbackIdentity); onIdentityChange?.(fallbackIdentity); }
          pendingIdentity.current = null;
          setError('GitHub posting was not approved. You can try again or submit through Sportarr.');
          return;
        }
        pollInFlight.current = true;
        try {
          const value = await supportIdentity();
          if (attempt.current !== currentAttempt) return;
          failedChecks = 0;
          if (value.github_posting) {
            stopWaiting();
            setIdentity(value);
            onIdentityChange?.(value);
            setBusy(false);
            setStage(null);
            pendingIdentity.current = null;
          }
        } catch {
          failedChecks += 1;
          if (failedChecks >= 3) setError('Could not check GitHub approval yet. Sportarr is still waiting.');
        } finally {
          pollInFlight.current = false;
        }
      }, 2000);
    } catch (failure) {
      stopWaiting(true);
      setBusy(false);
      setStage(null);
      if (fallbackIdentity) { setIdentity(fallbackIdentity); onIdentityChange?.(fallbackIdentity); }
      pendingIdentity.current = null;
      setError(failure instanceof Error ? failure.message : 'Could not connect GitHub.');
    }
  }

  async function disconnect() {
    stopWaiting(true);
    setBusy(true);
    setStage(null);
    setError(null);
    try {
      await disconnectSupport();
    } catch {
      setError('This browser is signed out, but the server could not confirm revocation. You can revoke it from your account on sportarr.net.');
    }
    clearSupportToken();
    setIdentity(null);
    setBusy(false);
    setStage(null);
    onIdentityChange?.(null);
  }

  if (loading) return <div role="status" className="rounded-lg border border-gray-700 bg-black/30 p-4 text-sm text-gray-300">Checking account connection...</div>;
  if (connectionUnavailable) return <div className="rounded-lg border border-gray-700 bg-black/30 p-4 text-sm text-gray-300">
    <p role="alert">Could not check your account. Your connection is saved.</p>
    <button type="button" className={`${BUTTON_SECONDARY} mt-3`} onClick={() => void retryIdentity()}>Try again</button>
  </div>;

  if (!identity) {
    return (
      <div className="rounded-lg border border-gray-700 bg-black/30 p-4 sm:p-5">
        <h2 className="font-bold text-white">Sign in to Support</h2>
        <p className="mt-1 text-sm text-gray-400">Choose GitHub or Discord. Sign-in opens in a new tab, so your issue and files stay here.</p>
        <div className="mt-4 flex flex-col gap-2 sm:flex-row">
          <button type="button" disabled={busy} onClick={() => void connectAccount('github')} className={`${BUTTON_PRIMARY} w-full sm:w-auto`}>{busy ? stage === 'github-permission' ? 'Finishing GitHub permission...' : 'Waiting for sign-in...' : 'Continue with GitHub'}</button>
          <button type="button" disabled={busy} onClick={() => void connectAccount('discord')} className={`${BUTTON_SECONDARY} w-full sm:w-auto`}>{busy ? 'Please wait...' : 'Continue with Discord'}</button>
        </div>
        {busy && <button type="button" onClick={cancelConnection} className="mt-3 min-h-10 text-sm text-gray-300 underline-offset-2 hover:text-white hover:underline">{stage === 'github-permission' ? 'Continue without GitHub posting' : 'Cancel sign-in'}</button>}
        {error && <p role="alert" className="mt-3 text-sm text-red-300">{error}</p>}
      </div>
    );
  }

  return (
    <div className="rounded-lg border border-gray-700 bg-black/30 p-4 sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="inline-flex items-center gap-2 font-bold text-white"><CheckCircleIcon className="h-5 w-5 text-green-400" />Account connected</h2>
          <p className="mt-1 text-sm text-gray-400">{identity.connected_providers.map((provider) => provider === 'github' ? 'GitHub' : 'Discord').join(' and ')}</p>
        </div>
        {!identity.github_posting && identity.connected_providers.includes('github') && (
          <button id="support-github-posting" type="button" disabled={busy} onClick={() => connectGithubPosting(identity.session_id)} className={BUTTON_SECONDARY}>{busy ? 'Waiting for GitHub...' : 'Finish GitHub posting permission'}</button>
        )}
      </div>
      {identity.github_posting && <p className="mt-3 text-sm text-green-300">New posts will appear as @{identity.github_login} on GitHub.</p>}
      <button type="button" className="mt-3 min-h-10 text-sm text-gray-400 underline-offset-2 hover:text-white hover:underline" onClick={disconnect}>Sign out of Support here</button>
      {busy && <button type="button" onClick={cancelConnection} className="ml-4 mt-3 min-h-10 text-sm text-gray-300 underline-offset-2 hover:text-white hover:underline">Cancel GitHub approval</button>}
      {error && <p role="alert" className="mt-3 text-sm text-red-300">{error}</p>}
    </div>
  );
}
