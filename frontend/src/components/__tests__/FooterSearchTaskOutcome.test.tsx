import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import FooterStatusBar from '../FooterStatusBar';

const transport = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));

const oldTask = {
  id: 1, name: 'RSS Sync', commandName: 'RssSync', status: 'Completed', progress: 100,
  message: 'Task completed successfully', started: '2026-08-29T07:39:00Z', ended: '2026-08-29T07:39:39Z',
  result: null,
};

beforeEach(() => {
  transport.get.mockReset();
});

function renderFooter() {
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/queue') return { data: [] };
    if (path === '/search/active') return { data: null };
    if (path === '/search/queue') return { data: {
      pendingCount: 0, activeCount: 0, maxConcurrent: 1,
      pendingSearches: [], activeSearches: [], recentlyCompleted: []
    } };
    if (path === '/task?pageSize=10') return { data: [oldTask] };
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

function finishedSearch(result: string | null, message: string) {
  const now = new Date().toISOString();
  return {
    id: 2, name: 'Search: UFC 300', commandName: 'EventSearch', status: 'Completed', progress: 100,
    message, started: now, ended: now, result,
  };
}

describe('footer event search task outcome', () => {
  it('does not show a plain success for a search that grabbed nothing', async () => {
    const client = renderFooter();
    await vi.waitFor(() => expect(client.getQueryData(['tasks', 10])).toBeDefined());

    const message = 'Found 2 releases, but none clearly match this event. Use Manual Search to pick one.';
    act(() => client.setQueryData(['tasks', 10], [finishedSearch('{"grabbed":false}', message), oldTask]));

    expect(await screen.findByText('Search: UFC 300')).toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
    expect(screen.getByText('Nothing grabbed')).toHaveClass('text-yellow-400');
    expect(screen.queryByText('Task completed')).not.toBeInTheDocument();
  });

  it('still shows success for a search that grabbed a release', async () => {
    const client = renderFooter();
    await vi.waitFor(() => expect(client.getQueryData(['tasks', 10])).toBeDefined());

    act(() => client.setQueryData(['tasks', 10],
      [finishedSearch('{"grabbed":true}', 'Found 2 releases - Downloaded: UFC.300.Main.Card'), oldTask]));

    expect(await screen.findByText('Search: UFC 300')).toBeInTheDocument();
    expect(screen.getByText('Task completed')).toHaveClass('text-green-400');
    expect(screen.queryByText('Nothing grabbed')).not.toBeInTheDocument();
  });
});
