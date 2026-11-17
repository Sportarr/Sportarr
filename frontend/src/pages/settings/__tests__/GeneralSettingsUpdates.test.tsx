import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../../test/test-utils';
import { apiGet, apiPost } from '../../../utils/api';
import GeneralSettings from '../GeneralSettings';
import { installBlockReason, isNewFailure, lastResultText, normalizeUpdateMechanism } from '../updateMechanismPresentation';

vi.mock('../../../utils/api', () => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiPut: vi.fn(),
  apiDelete: vi.fn(),
}));

const baseStatus = {
  currentVersion: '4.1.12.1121',
  latestVersion: '4.1.13.1130',
  available: true,
  mechanism: 'builtin',
  automatic: false,
  canInstall: true,
  reason: null,
  releaseUrl: 'https://github.com/Sportarr/Sportarr/releases/tag/v4.1.13.1130',
  installing: false,
};

function mockServer(update: Record<string, unknown>, status: Record<string, unknown> = {}) {
  vi.mocked(apiGet).mockImplementation(async (url: string) => {
    if (url === '/api/update/status') {
      return { ok: true, json: async () => ({ ...baseStatus, ...status }) } as Response;
    }
    return {
      ok: true,
      json: async () => ({
        updateSettings: JSON.stringify({ branch: 'main', automatic: false, scriptPath: '', ...update }),
      }),
    } as Response;
  });
}

describe('Update settings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('offers Built-in, Docker, External and Script', async () => {
    mockServer({ mechanism: 'docker' }, { mechanism: 'docker' });
    renderWithProviders(<GeneralSettings />);

    const select = await screen.findByLabelText('Mechanism');
    const options = Array.from((select as HTMLSelectElement).options).map((o) => o.textContent);
    expect(options).toEqual(['Built-in', 'Docker', 'External', 'Script']);
    expect(screen.getByText('Pull the new image and recreate the container.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Install update' })).not.toBeInTheDocument();
  });

  it('disables automatic updates unless the mechanism is Built-in or Script', async () => {
    const user = userEvent.setup();
    mockServer({ mechanism: 'external' }, { mechanism: 'external' });
    renderWithProviders(<GeneralSettings />);

    const toggle = await screen.findByRole('checkbox', { name: /Automatic Updates/ });
    expect(toggle).toBeDisabled();
    expect(
      screen.getByText('Update with your installer or package manager. Sportarr shows when a new version is out.'),
    ).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Mechanism'), 'script');
    expect(toggle).toBeEnabled();
    expect(screen.getByLabelText('Script Path')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Mechanism'), 'builtin');
    expect(toggle).toBeEnabled();
    expect(screen.queryByLabelText('Script Path')).not.toBeInTheDocument();
  });

  it('installs an available update with the built-in mechanism', async () => {
    const user = userEvent.setup();
    mockServer({ mechanism: 'builtin' });
    vi.mocked(apiPost).mockResolvedValue({
      ok: true,
      json: async () => ({ message: 'Installing Sportarr 4.1.13.1130. Sportarr restarts in a moment.' }),
    } as Response);
    renderWithProviders(<GeneralSettings />);

    expect(await screen.findByText(/4\.1\.13\.1130/)).toBeInTheDocument();
    const button = screen.getByRole('button', { name: 'Install update' });
    expect(button).toBeEnabled();

    await user.click(button);

    expect(apiPost).toHaveBeenCalledWith('/api/update/install', {});
  });

  it('shows the busy reason when the install is refused', async () => {
    const user = userEvent.setup();
    mockServer({ mechanism: 'builtin' });
    vi.mocked(apiPost).mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({ error: 'A recording is in progress. Install the update when it has finished.' }),
    } as Response);
    renderWithProviders(<GeneralSettings />);

    await user.click(await screen.findByRole('button', { name: 'Install update' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'A recording is in progress. Install the update when it has finished.',
    );
    expect(screen.getByRole('button', { name: 'Install update' })).toBeEnabled();
  });

  it('shows why the built-in update cannot install', async () => {
    mockServer({ mechanism: 'builtin' }, {
      canInstall: false,
      reason: 'Sportarr runs in a container. Pull the new image and recreate the container instead.',
    });
    renderWithProviders(<GeneralSettings />);

    expect(
      await screen.findByText('Sportarr runs in a container. Pull the new image and recreate the container instead.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Install update' })).toBeDisabled();
  });

  it('shows a rolled back update from the status', async () => {
    mockServer({ mechanism: 'builtin' }, {
      lastResult: {
        status: 'RolledBack',
        version: '4.1.13.1130',
        message: 'Sportarr 4.1.13.1130 did not start, so the update was rolled back.',
        time: '2026-11-16T07:00:00.0000000Z',
      },
    });
    renderWithProviders(<GeneralSettings />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Update to 4.1.13.1130 failed. Sportarr 4.1.13.1130 did not start, so the update was rolled back.',
    );
  });

  it('reads Apt and unknown values as External', () => {
    expect(normalizeUpdateMechanism('Apt')).toBe('external');
    expect(normalizeUpdateMechanism('something')).toBe('external');
    expect(normalizeUpdateMechanism('BuiltIn')).toBe('builtin');
    expect(normalizeUpdateMechanism('SCRIPT')).toBe('script');
    expect(normalizeUpdateMechanism('')).toBe('docker');
  });

  it('says plainly when the update was cancelled because Sportarr was busy', async () => {
    const cancelled = {
      status: 'Cancelled',
      version: '4.1.13.1130',
      message: 'Sportarr was busy and kept running, so the update was not installed.',
      time: '2026-11-16T08:00:00.0000000Z',
    };
    mockServer({ mechanism: 'builtin', automatic: false }, { lastResult: cancelled });
    renderWithProviders(<GeneralSettings />);

    const notice = await screen.findByRole('status');
    expect(notice).toHaveTextContent(/^The update was not installed because Sportarr was busy\.$/);
    expect(notice.className).toContain('text-amber-400');
    expect(notice.className).not.toContain('text-red-400');
  });

  it('adds that automatic updates try again', () => {
    const cancelled = { status: 'Cancelled', version: '4.1.13.1130', message: 'busy.', time: 't' };
    expect(lastResultText(cancelled, true)).toBe('The update was not installed because Sportarr was busy. It tries again later.');
    expect(lastResultText(cancelled, false)).toBe('The update was not installed because Sportarr was busy.');
    expect(lastResultText({ ...cancelled, status: 'RolledBack', message: 'did not start.' }, true))
      .toBe('Update to 4.1.13.1130 failed. did not start.');
  });

  it('stops waiting at once when the install is cancelled', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      let cancelled = false;
      vi.mocked(apiGet).mockImplementation(async (url: string) => {
        if (url === '/api/update/status') {
          return {
            ok: true,
            json: async () => ({
              ...baseStatus,
              lastResult: cancelled
                ? { status: 'Cancelled', version: '4.1.13.1130', message: 'busy.', time: '2026-11-16T09:00:00Z' }
                : null,
            }),
          } as Response;
        }
        return {
          ok: true,
          json: async () => ({ updateSettings: JSON.stringify({ branch: 'main', automatic: false, scriptPath: '', mechanism: 'builtin' }) }),
        } as Response;
      });
      vi.mocked(apiPost).mockResolvedValue({ ok: true, json: async () => ({ message: 'Installing.' }) } as Response);
      renderWithProviders(<GeneralSettings />);

      await user.click(await screen.findByRole('button', { name: 'Install update' }));
      cancelled = true;
      await vi.advanceTimersByTimeAsync(4000);

      expect(await screen.findByRole('status')).toHaveTextContent('The update was not installed because Sportarr was busy.');
      expect(screen.getByRole('button', { name: 'Install update' })).toBeEnabled();
    } finally {
      vi.useRealTimers();
    }
  });

  it('sees a repeat failure with the same text as new', () => {
    const failure = { status: 'RolledBack', version: '4.1.13.1130', message: 'did not start.', time: '2026-11-16T07:00:00Z' };
    expect(isNewFailure({ ...baseStatus, lastResult: failure }, '2026-11-16T07:00:00Z')).toBe(false);
    expect(isNewFailure({ ...baseStatus, lastResult: { ...failure, time: '2026-11-16T08:00:00Z' } }, '2026-11-16T07:00:00Z')).toBe(true);
    expect(isNewFailure(baseStatus, null)).toBe(false);
  });

  it('asks for a save before installing with a mechanism that is not saved', () => {
    expect(installBlockReason('builtin', { ...baseStatus, mechanism: 'docker' })).toBe('Save your settings first.');
    expect(installBlockReason('builtin', baseStatus)).toBeNull();
  });
});
