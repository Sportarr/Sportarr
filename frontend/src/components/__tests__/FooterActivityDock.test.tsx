import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import FooterStatusBar from '../FooterStatusBar';

const transport = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));

beforeEach(() => {
  transport.get.mockReset();
});

function renderFooter(queue: object[]) {
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/queue') return { data: queue };
    if (path === '/search/active') return { data: null };
    if (path === '/search/queue') return { data: {
      pendingCount: 0, activeCount: 0, maxConcurrent: 1,
      pendingSearches: [], activeSearches: [], recentlyCompleted: []
    } };
    if (path === '/task?pageSize=10') return { data: [] };
    throw new Error(`Unexpected request ${path}`);
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter><FooterStatusBar /></MemoryRouter>
    </QueryClientProvider>
  );
  return client;
}

describe('footer activity dock', () => {
  it('shows measured transfer details and links to that queue item', async () => {
    const now = new Date().toISOString();
    renderFooter([{
      id: 2160, eventId: 486555, event: { id: 486555, title: 'Azerbaijan GP · Race' },
      title: 'Formula1.2026.Azerbaijan.Grand.Prix', status: 1,
      progress: 64, downloaded: 64 * 1024 * 1024, size: 100 * 1024 * 1024,
      added: now, lastProgressAt: now, timeRemaining: '02:00:00'
    }]);

    await screen.findByText('Azerbaijan GP · Race');
    expect(screen.getByText('Downloading 64%')).toBeInTheDocument();
    expect(screen.getByText('64 MB / 100 MB')).toBeInTheDocument();
    expect(screen.getByText('2h left')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /view in activity/i }))
      .toHaveAttribute('href', '/activity?queue=2160');
  });

  it('does not present an old unchanged download as live work', async () => {
    renderFooter([{
      id: 2042, eventId: 537286, event: { id: 537286, title: 'MVP MMA 1' },
      title: 'MVP.MMA.2026.05.16.Rousey.vs.Carano', status: 1,
      progress: 99.5, downloaded: 1043333120, size: 1048576000,
      added: '2026-08-29T07:39:39Z', lastProgressAt: null, timeRemaining: null
    }]);

    await screen.findByText('MVP MMA 1');
    expect(screen.queryByText('Downloading...')).not.toBeInTheDocument();
    expect(screen.getByText(/no recent progress/i)).toBeInTheDocument();
  });

  it('does not let an old import hide a newer active download', async () => {
    const now = new Date().toISOString();
    renderFooter([
      { id: 41, title: 'Old import', status: 6, progress: 100, downloaded: 100, size: 100,
        added: '2026-08-29T07:39:39Z', lastProgressAt: '2026-08-29T07:39:39Z', lastUpdate: '2026-08-29T07:39:39Z' },
      { id: 42, title: 'Current download', status: 1, progress: 30, downloaded: 30, size: 100,
        added: now, lastProgressAt: now }
    ]);

    await screen.findByText('Current download');
    expect(screen.queryByText('Old import')).not.toBeInTheDocument();
  });

  it('shows an import that started recently after a long completed download', async () => {
    const now = new Date().toISOString();
    renderFooter([{ id: 43, title: 'Fresh import', status: 6, progress: 100,
      downloaded: 100, size: 100, added: '2026-08-29T07:39:39Z',
      lastProgressAt: '2026-08-29T07:39:39Z', lastUpdate: now }]);

    await screen.findByText('Fresh import');
    expect(screen.getByText('Importing to library')).toBeInTheDocument();
  });

  it('reports all imports completed in the same poll', async () => {
    const now = new Date().toISOString();
    const items = [41, 42].map(id => ({ id, title: `Event ${id}`, status: 6,
      progress: 100, downloaded: 100, size: 100, added: now, lastProgressAt: now }));
    const client = renderFooter(items);
    await screen.findByText('Event 42');

    act(() => client.setQueryData(['activityDockQueue'], items.map(item => ({ ...item, status: 7 }))));
    expect(await screen.findByText('2 events imported')).toBeInTheDocument();
  });
});
