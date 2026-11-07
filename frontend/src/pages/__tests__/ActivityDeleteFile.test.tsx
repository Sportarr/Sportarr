import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import ActivityPage from '../ActivityPage';
import { UI_SETTINGS_QUERY_KEY } from '../../hooks/useUISettings';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));

const grab = {
  kind: 'grab', id: 9, eventId: 7, eventTitle: 'Arsenal vs Chelsea', leagueName: 'English Premier League',
  title: 'EPL.2026.11.01.Arsenal.vs.Chelsea.720p.WEB.h264', indexer: 'Fixture', protocol: 'Torrent',
  size: 4096, quality: 'WEBDL-720p', qualityScore: 0, customFormatScore: 0, grabbedAt: '2026-11-01T18:00:00Z',
  wasImported: true, importedAt: '2026-11-01T19:00:00Z', fileExists: true, eventFileId: 3,
  destinationPath: '/library/EPL/Arsenal vs Chelsea.mkv', regrabCount: 0, hasDownloadUrl: true, hasTorrentHash: true,
};

const clients: QueryClient[] = [];
let confirmSpy: ReturnType<typeof vi.spyOn>;
beforeEach(() => {
  localStorage.clear();
  transport.get.mockReset().mockImplementation(async (path: string) => {
    if (path === '/queue' || path === '/pending-imports') return { data: [] };
    if (path.startsWith('/grab-history')) return { data: { history: [grab], totalPages: 1 } };
    return { data: [] };
  });
  transport.delete.mockReset().mockResolvedValue({ data: { success: true } });
  // An embedded web view with no dialog support answers false at once.
  confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);
});
afterEach(() => {
  confirmSpy.mockRestore();
  for (const client of clients.splice(0)) client.clear();
});

it('deletes a history file through the page dialog, not the browser confirm box', async () => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1400 });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  client.setQueryData(UI_SETTINGS_QUERY_KEY, { eventViewMode: 'compact' });
  render(<QueryClientProvider client={client}><MemoryRouter><ActivityPage /></MemoryRouter></QueryClientProvider>);
  const user = userEvent.setup();

  await user.click(await screen.findByRole('button', { name: 'History' }));
  const [trash] = await screen.findAllByTitle('Delete this file from disk (entry stays for re-grabbing)');
  await user.click(trash);

  const dialog = await screen.findByRole('dialog');
  expect(within(dialog).getByText(/Arsenal vs Chelsea\.mkv/)).toBeInTheDocument();
  await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

  await waitFor(() => expect(transport.delete).toHaveBeenCalledWith('/events/7/files/3'));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(confirmSpy).not.toHaveBeenCalled();
});

it('keeps the dialog open with the error when the delete fails', async () => {
  transport.delete.mockRejectedValue({ response: { data: { error: 'File is locked' } } });
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1400 });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  client.setQueryData(UI_SETTINGS_QUERY_KEY, { eventViewMode: 'compact' });
  render(<QueryClientProvider client={client}><MemoryRouter><ActivityPage /></MemoryRouter></QueryClientProvider>);
  const user = userEvent.setup();

  await user.click(await screen.findByRole('button', { name: 'History' }));
  const [trash] = await screen.findAllByTitle('Delete this file from disk (entry stays for re-grabbing)');
  await user.click(trash);
  const dialog = await screen.findByRole('dialog');
  await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

  expect(await within(dialog).findByText('File is locked')).toBeInTheDocument();
  expect(screen.getByRole('dialog')).toBeInTheDocument();
});
