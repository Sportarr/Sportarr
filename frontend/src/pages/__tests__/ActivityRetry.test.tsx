import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Link, MemoryRouter } from 'react-router-dom';
import ActivityPage from '../ActivityPage';
import { UI_SETTINGS_QUERY_KEY } from '../../hooks/useUISettings';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));

const clients: QueryClient[] = [];
beforeEach(() => {
  localStorage.clear();
  transport.get.mockReset();
  transport.post.mockReset().mockResolvedValue({ data: { success: true } });
});
afterEach(() => {
  for (const client of clients.splice(0)) client.clear();
});

async function showQueue(width: number, status = 9, progress = 100, canRetryImport = true, canImportAnyway = false,
  extraRows: Array<{ id: number; eventId: number; title: string; canImportAnyway: boolean; canRetryImport: boolean }> = [],
  canChooseVideo = false, initialEntry = '/activity', expectedTitle = 'Owned team event', expandList = true,
  pendingRows: unknown[] = [], queueOverrides: Record<string, unknown> = {}) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
  const queueItem = {
    id: 41, eventId: 7, event: { id: 7, title: 'Owned team event', organization: 'NFL',
      eventDate: '2020-09-01T20:00:00Z', monitored: true, hasFile: canImportAnyway },
    title: 'NFL.2020.Week1.720p.WEB-DL', downloadId: 'owned-pack-job', downloadClientId: 1,
    downloadClient: { id: 1, name: 'Owned fixture' }, status, progress, canRetryImport, canImportAnyway, canChooseVideo,
    size: 4096, downloaded: progress === 100 ? 4096 : 4000, protocol: 'Torrent',
    quality: canImportAnyway ? 'HDTV-1080p' : 'WEBDL-720p', customFormatScore: canImportAnyway ? 2500 : 0,
    errorMessage: canImportAnyway
      ? 'Not an upgrade for the existing file. Existing quality: WEBDL-2160p. New quality: HDTV-1080p.'
      : status === 9 ? 'Pack member unresolved: No member uniquely identifies this event.' : 'Import failed',
    statusMessages: [], added: '2020-09-02T00:00:00Z', completedAt: '2020-09-02T00:01:00Z',
    ...queueOverrides
  };
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/queue') return { data: [queueItem, ...extraRows.map(extra => ({
      ...queueItem, ...extra, event: { ...queueItem.event, id: extra.eventId, title: extra.title }
    }))] };
    if (path === '/pending-imports') return { data: pendingRows };
    if (/^\/events\/\d+\/files$/.test(path)) return { data: [{ id: 1, quality: 'WEBDL-2160p', customFormatScore: 560, partName: null }] };
    if (path === '/queue/41/video-files') return { data: [
      { relativePath: 'Sprint.mp4', size: 8192 }, { relativePath: 'Race.mp4', size: 16384 }
    ] };
    throw new Error('Unconfigured page request ' + path);
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  client.setQueryData(UI_SETTINGS_QUERY_KEY, { eventViewMode: 'compact' });
  const { container } = render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[initialEntry]}><Link to="/activity?queue=42">Open queue item</Link><ActivityPage /></MemoryRouter></QueryClientProvider>);
  await screen.findAllByText(expectedTitle);
  const branch = width >= 1280
    ? screen.getAllByText(expectedTitle).map(node => node.closest('tr')).find(Boolean)
    : Array.from(container.querySelectorAll<HTMLElement>('[data-view="list"]')).find(node => node.textContent?.includes(expectedTitle));
  expect(branch).not.toBeNull();
  const row = within(branch as HTMLElement);
  expect(row.getAllByText(expectedTitle).length).toBeGreaterThan(0);
  const user = userEvent.setup();
  if (width < 1280 && expandList) await user.click(row.getByRole('button', { name: /details/i }));
  return { user, row, container };
}

it('keeps a linked queue item visible even with a one-row page size', async () => {
  localStorage.setItem('queuePageSize', '1');
  await showQueue(1000, 1, 64, false, false, [
    { id: 42, eventId: 8, title: 'Linked queue item', canImportAnyway: false, canRetryImport: false }
  ], false, '/activity?queue=42', 'Linked queue item');

  expect(screen.getAllByText('Linked queue item').length).toBeGreaterThan(0);
});

it('switches from History to Queue when a dock link selects an item', async () => {
  localStorage.setItem('queuePageSize', '1');
  const { user } = await showQueue(1000, 1, 64, false, false, [
    { id: 42, eventId: 8, title: 'Linked queue item', canImportAnyway: false, canRetryImport: false }
  ]);
  await user.click(screen.getByRole('button', { name: 'History' }));
  await user.click(screen.getByRole('link', { name: 'Open queue item' }));

  expect((await screen.findAllByText('Linked queue item')).length).toBeGreaterThan(0);
  await waitFor(() => expect(document.activeElement).toHaveAttribute('data-queue-id', '42'));

  await user.click(screen.getByRole('button', { name: 'History' }));
  await user.click(screen.getByRole('link', { name: 'Open queue item' }));
  expect((await screen.findAllByText('Linked queue item')).length).toBeGreaterThan(0);
  await waitFor(() => expect(document.activeElement).toHaveAttribute('data-queue-id', '42'));
});

it.each([390, 820])('shows compact queue rows without a table at %ipx', async width => {
  const { user, container } = await showQueue(width, 1, 64, false, false, [], false, '/activity', 'Owned team event', false);
  const row = container.querySelector<HTMLElement>('[data-queue-id="41"][data-view="list"]');

  expect(row).not.toBeNull();
  expect(container.querySelector('[data-queue-id="41"][data-view="table"]')).toBeNull();
  expect(within(row!).getByText('Downloading')).toBeInTheDocument();
  expect(within(row!).getByText('64%')).toBeInTheDocument();
  expect(within(row!).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();

  await user.click(within(row!).getByRole('button', { name: /details/i }));
  expect(within(row!).getByRole('button', { name: 'Remove' })).toBeInTheDocument();
  expect(within(row!).getByText('NFL.2020.Week1.720p.WEB-DL')).toBeInTheDocument();
});

it('shows a pending pack in the compact list without hiding its error', async () => {
  const pending = {
    id: 55, downloadClientId: 1, downloadId: 'pack-job', downloadClient: { id: 1, name: 'Owned fixture' },
    title: 'Formula1.2026.Pack', filePath: '/downloads/pack', size: 4096, qualityScore: 0,
    suggestionConfidence: 0, detected: '2020-09-02T00:00:00Z', isPack: true,
    errorMessage: 'Files need matching before import'
  };
  const { user, container } = await showQueue(390, 1, 64, false, false, [], false,
    '/activity', 'Owned team event', false, [pending]);
  const pendingRow = container.querySelector<HTMLElement>('[data-pending-id="55"][data-view="list"]');

  expect(pendingRow).not.toBeNull();
  expect(within(pendingRow!).getByText('Formula1.2026.Pack')).toBeInTheDocument();
  expect(within(pendingRow!).getByText('Files need matching before import')).toBeInTheDocument();
  expect(within(pendingRow!).queryByRole('button', { name: 'Import Pack' })).not.toBeInTheDocument();

  await user.click(within(pendingRow!).getByRole('button', { name: /details/i }));
  expect(within(pendingRow!).getByRole('button', { name: 'Import Pack' })).toBeInTheDocument();
});

it('shows the import error as well as a separate queue warning in the compact row', async () => {
  const { row } = await showQueue(390, 4, 100, false, false, [], false,
    '/activity', 'Owned team event', false, [], {
      statusMessages: ['The event is no longer monitored'],
      errorMessage: 'Import failed because the file is missing'
    });

  expect(row.getByText('The event is no longer monitored')).toBeInTheDocument();
  expect(row.getByText('Import failed because the file is missing')).toBeInTheDocument();
});

it('clears a queue selection when a linked item replaces the visible row', async () => {
  localStorage.setItem('queuePageSize', '1');
  const { user, row } = await showQueue(390, 1, 64, false, false, [
    { id: 42, eventId: 8, title: 'Linked queue item', canImportAnyway: false, canRetryImport: false }
  ], false, '/activity', 'Owned team event', false);

  await user.click(row.getByRole('checkbox'));
  expect(screen.getByRole('button', { name: 'Remove Selected' })).toBeInTheDocument();
  await user.click(screen.getByRole('link', { name: 'Open queue item' }));

  await screen.findAllByText('Linked queue item');
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove Selected' })).not.toBeInTheDocument());
});

it('clears a pending-import selection when a linked queue item takes its page slot', async () => {
  localStorage.setItem('queuePageSize', '1');
  const pending = {
    id: 55, title: 'Pending pack', filePath: '/downloads/pack', size: 4096,
    detected: '2020-09-02T00:00:00Z', downloadClientId: 1,
    downloadClient: { id: 1, name: 'Owned fixture' }
  };
  const { user, row } = await showQueue(390, 1, 64, false, false, [
    { id: 42, eventId: 8, title: 'Linked queue item', canImportAnyway: false, canRetryImport: false }
  ], false, '/activity', 'Pending pack', false, [pending]);

  await user.click(row.getByRole('checkbox'));
  expect(screen.getByRole('button', { name: 'Remove Selected' })).toBeInTheDocument();
  await user.click(screen.getByRole('link', { name: 'Open queue item' }));

  await screen.findAllByText('Linked queue item');
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove Selected' })).not.toBeInTheDocument());
});

it('explains pending-download removal before removing its client files', async () => {
  const pending = {
    id: 55, title: 'Pending pack', filePath: '/downloads/pack', size: 4096,
    detected: '2020-09-02T00:00:00Z', downloadClientId: 1,
    downloadClient: { id: 1, name: 'Owned fixture' }
  };
  const { user, row } = await showQueue(390, 1, 64, false, false, [], false,
    '/activity', 'Pending pack', true, [pending]);

  await user.click(row.getByRole('button', { name: 'Remove' }));

  expect(transport.post).not.toHaveBeenCalled();
  expect(screen.getByText('Deletes the download and its files from the download client')).toBeInTheDocument();
  expect(screen.getByRole('heading', { name: 'Remove pending download' })).toBeInTheDocument();
  expect(screen.getByRole('option', { name: 'Blocklist without searching' })).toBeInTheDocument();
  expect(screen.queryByRole('option', { name: 'Do not Blocklist' })).not.toBeInTheDocument();
  expect(screen.queryByRole('option', { name: 'Ignore Download' })).not.toBeInTheDocument();
});

describe.each([['desktop', 1440], ['phone', 390]] as const)('Activity retry on %s', (_view, width) => {
  it('uses the existing retry action for a recoverable pack warning', async () => {
    const { user, row } = await showQueue(width);
    await user.click(row.getByRole('button', { name: 'Retry Import' }));
    await waitFor(() => expect(transport.post).toHaveBeenCalledTimes(1));
    expect(transport.post).toHaveBeenCalledWith('/queue/41/retry');
  });

  it('sends a selected recoverable pack warning through bulk retry', async () => {
    const { user, row } = await showQueue(width);
    await user.click(row.getByRole('checkbox'));
    const action = screen.getByRole('button', { name: 'Import Selected' });
    expect(action).toBeEnabled();
    await user.click(action);
    await waitFor(() => expect(transport.post).toHaveBeenCalledTimes(1));
    expect(transport.post).toHaveBeenCalledWith('/queue/41/retry');
  });

  it('keeps an ineligible warning out of individual and bulk retry', async () => {
    const { user, row } = await showQueue(width, 9, 100, false);
    expect(row.queryByRole('button', { name: 'Retry Import' })).not.toBeInTheDocument();
    await user.click(row.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeDisabled();
    expect(transport.post).not.toHaveBeenCalled();
  });

  it('preserves the existing completed failure retry action', async () => {
    const { user, row } = await showQueue(width, 4, 100, true);
    await user.click(row.getByRole('button', { name: 'Retry Import' }));
    await waitFor(() => expect(transport.post).toHaveBeenCalledTimes(1));
    expect(transport.post).toHaveBeenCalledWith('/queue/41/retry');
  });

  it('keeps incomplete failures out of individual and bulk retry', async () => {
    const { user, row } = await showQueue(width, 4, 99, false);
    expect(row.queryByRole('button', { name: 'Retry Import' })).not.toBeInTheDocument();
    await user.click(row.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeDisabled();
    expect(transport.post).not.toHaveBeenCalled();
  });
});

describe.each([['desktop', 1440], ['phone', 390]] as const)('Completed manual import on %s', (_view, width) => {
  it('offers Import and enables bulk import for a completed download', async () => {
    const { user, row } = await showQueue(width, 3, 100, false, false, [], false,
      '/activity', 'Owned team event', true, [], { errorMessage: null });

    const importButton = row.getByRole('button', { name: 'Import' });
    await user.click(row.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeEnabled();
    await user.click(importButton);

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith('/queue/41/import'));
  });

  it('sends completed downloads through bulk import', async () => {
    const { user, row } = await showQueue(width, 3, 100, false, false, [], false,
      '/activity', 'Owned team event', true, [], { errorMessage: null });

    await user.click(row.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: 'Import Selected' }));

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith('/queue/41/import'));
  });

  it('offers import when a client reports Completed at 99.9 percent', async () => {
    const { user, row } = await showQueue(width, 3, 99.9, false, false, [], false,
      '/activity', 'Owned team event', true, [], { errorMessage: null });

    expect(row.getByRole('button', { name: 'Import' })).toBeInTheDocument();
    await user.click(row.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeEnabled();
  });

  it('does not offer import for a download still in progress', async () => {
    const { user, row } = await showQueue(width, 1, 99.9, false, false, [], false,
      '/activity', 'Owned team event', true, [], { errorMessage: null });

    expect(row.queryByRole('button', { name: 'Import' })).not.toBeInTheDocument();
    await user.click(row.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeDisabled();
  });
});

describe.each([['desktop', 1440], ['phone', 390]] as const)('Activity video choice on %s', (_view, width) => {
  it('waits for a file choice before importing an ambiguous download', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, false, [], true);

    await user.click(row.getByRole('button', { name: 'Choose Video' }));
    const dialog = within(await screen.findByRole('dialog'));
    await dialog.findByText('Sprint.mp4');
    expect(dialog.getByText(/ignores automatic upgrade preferences/i)).toBeInTheDocument();
    expect(dialog.getByText(/WEBDL-2160p.*CF \+560/)).toBeInTheDocument();
    expect(dialog.getByText(/WEBDL-720p.*CF \+0/)).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import Selected Video' })).toBeDisabled();
    await user.click(dialog.getByRole('radio', { name: /Sprint.mp4/ }));
    await user.click(dialog.getByRole('button', { name: 'Import Selected Video' }));

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith('/queue/41/import-selected', { relativePath: 'Sprint.mp4' }));
  });

  it('closes the chooser when the failed import needs a queue retry', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, false, [], true);
    transport.post.mockRejectedValueOnce({ response: { data: { error: 'Try again shortly.', retryRequired: true } } });
    await user.click(row.getByRole('button', { name: 'Choose Video' }));
    const dialog = within(await screen.findByRole('dialog'));
    await dialog.findByText('Sprint.mp4');
    await user.click(dialog.getByRole('radio', { name: /Sprint.mp4/ }));
    await user.click(dialog.getByRole('button', { name: 'Import Selected Video' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(transport.post).toHaveBeenCalledTimes(1);
  });

  it('keeps the file list visible but blocks an override if current files cannot load', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, false, [], true);
    const originalGet = transport.get.getMockImplementation()!;
    transport.get.mockImplementation((path: string) => path === '/events/7/files'
      ? Promise.reject(new Error('Unavailable')) : originalGet(path));
    await user.click(row.getByRole('button', { name: 'Choose Video' }));
    const dialog = within(await screen.findByRole('dialog'));

    expect(await dialog.findByText('Sprint.mp4')).toBeInTheDocument();
    expect(dialog.getByText('Could not load the current files. Close this window and try again.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import Selected Video' })).toBeDisabled();
    expect(dialog.queryByText('No video files are available in this download.')).not.toBeInTheDocument();
  });
});

describe.each([['desktop', 1440], ['phone', 390]] as const)('Activity manual replacement on %s', (_view, width) => {
  it('requires confirmation before importing a lower-ranked completed download', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, true);

    await user.click(row.getByRole('button', { name: 'Import Anyway' }));
    expect(transport.post).not.toHaveBeenCalled();
    expect(await screen.findByText(/WEBDL-2160p.*CF \+560/)).toBeInTheDocument();
    expect(screen.getByText(/HDTV-1080p.*CF \+2500/)).toBeInTheDocument();
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Import Anyway' }));

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith('/queue/41/import-anyway'));
  });

  it('keeps a destructive override out of bulk import', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, true);
    await user.click(row.getByRole('checkbox'));
    const action = screen.getByRole('button', { name: 'Import Selected' });
    expect(action).toBeDisabled();
    expect(transport.post).not.toHaveBeenCalled();
  });

  it('does not import when the confirmation is cancelled', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, true);
    await user.click(row.getByRole('button', { name: 'Import Anyway' }));
    await screen.findByText(/Files on event: WEBDL-2160p/);
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    expect(transport.post).not.toHaveBeenCalled();
  });

  it('blocks confirmation when current files cannot be loaded', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, true);
    const originalGet = transport.get.getMockImplementation()!;
    transport.get.mockImplementation((path: string) => path === '/events/7/files'
      ? Promise.reject(new Error('Unavailable')) : originalGet(path));
    await user.click(row.getByRole('button', { name: 'Import Anyway' }));

    expect(await screen.findByText('Could not load the current files. Close this window and try again.')).toBeInTheDocument();
    expect(screen.queryByText('Files on event: None')).not.toBeInTheDocument();
    expect(within(screen.getByRole('dialog')).getByRole('button', { name: 'Import Anyway' })).toBeDisabled();
    expect(transport.post).not.toHaveBeenCalled();
  });

  it('keeps a mixed selection out of bulk import', async () => {
    const { user, row, container } = await showQueue(width, 9, 100, false, true, [
      { id: 42, eventId: 8, title: 'Second event', canImportAnyway: false, canRetryImport: true },
      { id: 43, eventId: 9, title: 'Third event', canImportAnyway: true, canRetryImport: false }
    ]);
    await user.click(row.getAllByRole('checkbox')[0]);
    const branches = width >= 1280
      ? within(screen.getByRole('table')).getAllByRole('row').slice(2)
      : Array.from(container.querySelectorAll<HTMLElement>('[data-view="list"]')).slice(1);
    for (const branch of branches) {
      const checkbox = within(branch as HTMLElement).queryByRole('checkbox');
      if (checkbox) await user.click(checkbox);
    }
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeDisabled();
    expect(transport.post).not.toHaveBeenCalled();
  });

  it('keeps the choice available after a failed request', async () => {
    const { user, row } = await showQueue(width, 9, 100, false, true);
    let failed = false;
    const originalGet = transport.get.getMockImplementation()!;
    transport.get.mockImplementation(async (path: string) => {
      const response = await originalGet(path);
      if (path === '/queue' && failed) {
        response.data[0].status = 4;
        response.data[0].canImportAnyway = false;
      }
      return response;
    });
    transport.post.mockImplementationOnce(() => {
      failed = true;
      return Promise.reject({ response: { data: { error: 'Client unavailable' } } });
    });
    await user.click(row.getByRole('button', { name: 'Import Anyway' }));
    const dialog = within(await screen.findByRole('dialog'));
    await dialog.findByText(/Files on event: WEBDL-2160p/);
    await user.click(dialog.getByRole('button', { name: 'Import Anyway' }));

    expect(await dialog.findByText('Client unavailable')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import Anyway' })).toBeEnabled();
    await waitFor(() => expect(row.queryByRole('button', { name: 'Import Anyway' })).not.toBeInTheDocument());
  });
});
