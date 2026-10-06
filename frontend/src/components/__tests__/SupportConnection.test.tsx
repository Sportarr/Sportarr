import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import SupportConnection from '../SupportConnection';
import { SupportApiError } from '../../api/supportConnection';

const mocks = vi.hoisted(() => ({
  token: null as string | null,
  start: vi.fn(),
  complete: vi.fn(),
  identity: vi.fn(),
}));

vi.mock('../../api/supportConnection', () => ({
  getSupportToken: () => mocks.token,
  clearSupportToken: vi.fn(),
  saveSupportToken: vi.fn(),
  disconnectSupport: vi.fn(),
  startSupportPairing: mocks.start,
  completeSupportPairing: mocks.complete,
  supportIdentity: mocks.identity,
  SupportApiError: class extends Error {
    constructor(message: string, public status: number) { super(message); }
  },
}));

describe('SupportConnection', () => {
  afterEach(() => {
    mocks.token = null;
    mocks.start.mockReset();
    mocks.complete.mockReset();
    mocks.identity.mockReset();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('starts from the account provider instead of asking for a pairing code', () => {
    render(<SupportConnection />);

    expect(screen.getByRole('button', { name: 'Continue with GitHub' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue with Discord' })).toBeInTheDocument();
    expect(screen.queryByText(/Enter this code/i)).not.toBeInTheDocument();
  });

  it('explains when this app address is not allowed to connect', async () => {
    const popup = { closed: false, close: vi.fn(), location: { href: '' } };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    mocks.start.mockRejectedValue(new SupportApiError('Invalid app origin', 400));
    render(<SupportConnection />);

    fireEvent.click(screen.getByRole('button', { name: 'Continue with GitHub' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('This Sportarr address is not enabled for Support sign-in');
    expect(popup.close).toHaveBeenCalledOnce();
  });

  it('does not continue a pairing cancelled before the server responds', async () => {
    let finishStart!: (value: unknown) => void;
    mocks.start.mockImplementation(() => new Promise((resolve) => { finishStart = resolve; }));
    const popup = { closed: false, close: vi.fn(), location: { href: '' } };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    render(<SupportConnection />);

    fireEvent.click(screen.getByRole('button', { name: 'Continue with GitHub' }));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel sign-in' }));
    await act(async () => finishStart({ pairing_id: 'pair', approve_url: 'https://sportarr.net/support/connect/pair' }));

    expect(popup.close).toHaveBeenCalledOnce();
    expect(popup.location.href).toBe('');
    expect(mocks.complete).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Continue with GitHub' })).toBeEnabled();
  });

  it('stops checking GitHub posting after permission times out', async () => {
    mocks.token = 'connected';
    mocks.identity.mockResolvedValue({ session_id: 'session', connected_providers: ['github'],
      expires_at: '2026-12-01T00:00:00Z', github_posting: false, github_login: 'reporter' });
    const popup = { closed: false, close: vi.fn(), location: { href: '' } };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    render(<SupportConnection />);
    const approve = await screen.findByRole('button', { name: 'Finish GitHub posting permission' });
    vi.useFakeTimers();
    fireEvent.click(approve);

    await act(async () => vi.advanceTimersByTimeAsync(122000));
    expect(screen.queryByText(/GitHub posting was not approved/)).not.toBeInTheDocument();
    await act(async () => vi.advanceTimersByTimeAsync(480000));
    const callsAtTimeout = mocks.identity.mock.calls.length;
    await act(async () => vi.advanceTimersByTimeAsync(10000));

    expect(mocks.identity.mock.calls.length).toBe(callsAtTimeout);
    expect(vi.getTimerCount()).toBe(0);
    expect(screen.getByText(/GitHub posting was not approved/)).toBeInTheDocument();
  });

  it('expires sign-in after ten minutes when the server expiry is unreadable', async () => {
    const popup = { closed: false, close: vi.fn(), location: { href: '' }, postMessage: vi.fn() };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    mocks.start.mockResolvedValue({ pairing_id: 'pair', approve_url: 'https://sportarr.net/support/connect/pair',
      approval_secret: 'proof', expires_at: 'not-a-date' });
    mocks.complete.mockResolvedValue({ status: 'pending' });
    vi.useFakeTimers();
    render(<SupportConnection />);
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Continue with GitHub' }));
      await Promise.resolve();
    });

    await act(async () => vi.advanceTimersByTimeAsync(602000));

    expect(screen.getByRole('alert')).toHaveTextContent('sign-in request expired');
    expect(vi.getTimerCount()).toBe(0);
  });

  it('lets an already-connected user leave GitHub approval pending', async () => {
    mocks.token = 'connected';
    mocks.identity.mockResolvedValue({ session_id: 'session', connected_providers: ['github'],
      expires_at: '2026-12-01T00:00:00Z', github_posting: false, github_login: 'reporter' });
    const popup = { closed: false, close: vi.fn(), location: { href: '' } };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    render(<SupportConnection />);
    const approve = await screen.findByRole('button', { name: 'Finish GitHub posting permission' });
    vi.useFakeTimers();
    fireEvent.click(approve);

    fireEvent.click(screen.getByRole('button', { name: 'Cancel GitHub approval' }));

    expect(popup.close).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Finish GitHub posting permission' })).toBeEnabled();
    expect(vi.getTimerCount()).toBe(0);
  });
});
