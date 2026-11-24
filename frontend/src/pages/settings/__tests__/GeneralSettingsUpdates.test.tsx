import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../../test/test-utils';
import { apiGet, apiPost, apiPut } from '../../../utils/api';
import GeneralSettings from '../GeneralSettings';
import { installBlockReason, isNewFailure, lastResultText, updateView } from '../updateMechanismPresentation';

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

const FOLDER_REASON = 'Sportarr cannot write to its install folder C:\\Program Files\\Sportarr.';
const CONTAINER_REASON = 'Sportarr runs in a container. Pull the new image and recreate the container to update.';

// The update block of a native install where the built-in update works.
const workingNative = {
  branch: 'main',
  automaticUpdates: false,
  mechanism: 'builtin',
  automaticAvailable: true,
  automaticUnavailableReason: null,
  scriptPath: null,
};

function mockServer(update: Record<string, unknown>, status: Record<string, unknown> = {}) {
  vi.mocked(apiGet).mockImplementation(async (url: string) => {
    if (url === '/api/update/status') {
      return { ok: true, json: async () => ({ ...baseStatus, ...status }) } as Response;
    }
    return {
      ok: true,
      json: async () => ({
        updateSettings: JSON.stringify({ ...workingNative, ...update }),
      }),
    } as Response;
  });
}

describe('Update settings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows one checkbox and the Install button on a native install that works', async () => {
    const user = userEvent.setup();
    mockServer({});
    vi.mocked(apiPut).mockResolvedValue({ ok: true, json: async () => ({ success: true }) } as Response);
    renderWithProviders(<GeneralSettings />);

    const toggle = await screen.findByRole('checkbox', { name: /Automatic Updates/ });
    expect(toggle).toBeEnabled();
    expect(toggle).not.toBeChecked();
    expect(await screen.findByText(/4\.1\.13\.1130/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Install update' })).toBeEnabled();
    expect(screen.queryByLabelText('Mechanism')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Script Path')).not.toBeInTheDocument();

    // Tick the box and save. Nothing else is needed.
    await user.click(toggle);
    // The header and the floating pill both offer Save after a change.
    await user.click((await screen.findAllByRole('button', { name: 'Save Changes' }))[0]);

    await vi.waitFor(() => expect(apiPut).toHaveBeenCalled());
    const body = vi.mocked(apiPut).mock.calls[0][1] as { updateSettings: string };
    const sent = JSON.parse(body.updateSettings);
    expect(sent.automaticUpdates).toBe(true);
    // v4.1.12 used "automatic" for a tick that did nothing. The page never sends it.
    expect(sent).not.toHaveProperty('automatic');
  });

  it('disables the checkbox and the Install button with one plain reason when the install is blocked', async () => {
    mockServer(
      { automaticAvailable: false, automaticUnavailableReason: FOLDER_REASON },
      { canInstall: false, reason: FOLDER_REASON },
    );
    renderWithProviders(<GeneralSettings />);

    const toggle = await screen.findByRole('checkbox', { name: /Automatic Updates/ });
    expect(toggle).toBeDisabled();
    expect(await screen.findByText(/4\.1\.13\.1130/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Install update' })).toBeDisabled();
    expect(screen.getAllByText(FOLDER_REASON)).toHaveLength(1);
    expect(screen.queryByLabelText('Mechanism')).not.toBeInTheDocument();
  });

  it('takes availability from the status, which checks again, over a stale settings answer', async () => {
    mockServer(
      { automaticAvailable: false, automaticUnavailableReason: 'An older reason.' },
      { automaticAvailable: true, automaticUnavailableReason: null },
    );
    renderWithProviders(<GeneralSettings />);

    expect(await screen.findByText(/4\.1\.13\.1130/)).toBeInTheDocument();
    expect(screen.queryByText('An older reason.')).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Automatic Updates/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Install update' })).toBeEnabled();
  });

  it('shows the fresh reason from the status next to the fresh versions', async () => {
    mockServer(
      { automaticAvailable: true, automaticUnavailableReason: null },
      { automaticAvailable: false, automaticUnavailableReason: FOLDER_REASON, canInstall: false, reason: FOLDER_REASON },
    );
    renderWithProviders(<GeneralSettings />);

    expect(await screen.findByText(FOLDER_REASON)).toBeInTheDocument();
    expect(screen.getAllByText(FOLDER_REASON)).toHaveLength(1);
    expect(screen.getByRole('checkbox', { name: /Automatic Updates/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Install update' })).toBeDisabled();
  });

  it('only reports in a container', async () => {
    mockServer(
      { mechanism: 'docker', automaticAvailable: false, automaticUnavailableReason: CONTAINER_REASON },
      { mechanism: 'docker', canInstall: false, reason: CONTAINER_REASON },
    );
    renderWithProviders(<GeneralSettings />);

    expect(await screen.findByText('Pull the new image and recreate the container to update.')).toBeInTheDocument();
    expect(await screen.findByText(/4\.1\.13\.1130/)).toBeInTheDocument();
    expect(screen.getByText(/4\.1\.12\.1121/)).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /Automatic Updates/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Install update' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Mechanism')).not.toBeInTheDocument();
    expect(screen.queryByText(CONTAINER_REASON)).not.toBeInTheDocument();
  });

  it('says in one line that a custom update script is in use and shows its path', async () => {
    mockServer({ mechanism: 'script', scriptPath: '/opt/sportarr-update/run.sh' }, {
      mechanism: 'script',
      canInstall: false,
      reason: 'A custom update script is in use. It runs when Automatic Updates is on.',
    });
    renderWithProviders(<GeneralSettings />);

    expect(await screen.findByText('A custom update script is in use.')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Automatic Updates/ })).toBeEnabled();
    expect(screen.getByText('Runs /opt/sportarr-update/run.sh when a new version is out.')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: /script/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Install update' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Script Path')).not.toBeInTheDocument();
  });

  it('lets a ticked box be turned off and on again on a blocked install until it is saved', async () => {
    const user = userEvent.setup();
    mockServer({ automaticUpdates: true, automaticAvailable: false, automaticUnavailableReason: FOLDER_REASON });
    renderWithProviders(<GeneralSettings />);

    const toggle = await screen.findByRole('checkbox', { name: /Automatic Updates/ });
    expect(toggle).toBeChecked();
    expect(toggle).toBeEnabled();
    await user.click(toggle);
    expect(toggle).not.toBeChecked();
    expect(toggle).toBeEnabled();
    await user.click(toggle);
    expect(toggle).toBeChecked();
  });

  it('locks the box on a blocked install once the off state is saved', async () => {
    const user = userEvent.setup();
    mockServer({ automaticUpdates: true, automaticAvailable: false, automaticUnavailableReason: FOLDER_REASON });
    vi.mocked(apiPut).mockResolvedValue({ ok: true, json: async () => ({ success: true }) } as Response);
    renderWithProviders(<GeneralSettings />);

    const toggle = await screen.findByRole('checkbox', { name: /Automatic Updates/ });
    await user.click(toggle);
    await user.click((await screen.findAllByRole('button', { name: 'Save Changes' }))[0]);

    await vi.waitFor(() => expect(toggle).toBeDisabled());
    expect(toggle).not.toBeChecked();
  });

  it('picks the view from the update block', () => {
    expect(updateView({ mechanism: 'docker', automaticAvailable: false })).toBe('docker');
    expect(updateView({ mechanism: 'script', automaticAvailable: true })).toBe('script');
    expect(updateView({ mechanism: 'builtin', automaticAvailable: true })).toBe('native');
    expect(updateView({ mechanism: 'builtin', automaticAvailable: false })).toBe('blocked');
    // A block from an older server has no availability. It shows as blocked.
    expect(updateView({ mechanism: 'builtin' })).toBe('blocked');
    expect(updateView({ mechanism: '' })).toBe('docker');
  });

  it('installs an available update', async () => {
    const user = userEvent.setup();
    mockServer({});
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
    mockServer({});
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

  it('shows why the update cannot install now', async () => {
    mockServer({}, { canInstall: false, reason: 'Sportarr is up to date.' });
    renderWithProviders(<GeneralSettings />);

    expect(await screen.findByText('Sportarr is up to date.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Install update' })).toBeDisabled();
  });

  it('shows a rolled back update from the status', async () => {
    mockServer({}, {
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

  it('says plainly when the update was cancelled because Sportarr was busy', async () => {
    const cancelled = {
      status: 'Cancelled',
      version: '4.1.13.1130',
      message: 'Sportarr was busy and kept running, so the update was not installed.',
      time: '2026-11-16T08:00:00.0000000Z',
    };
    mockServer({}, { lastResult: cancelled });
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
          json: async () => ({ updateSettings: JSON.stringify(workingNative) }),
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

  it('waits for the status before it allows an install', () => {
    expect(installBlockReason(null)).toBe('Checking for updates.');
    expect(installBlockReason(baseStatus)).toBeNull();
    expect(installBlockReason({ ...baseStatus, canInstall: false, reason: 'Sportarr is up to date.' })).toBe('Sportarr is up to date.');
  });
});
