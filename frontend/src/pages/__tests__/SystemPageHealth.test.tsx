import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import SystemPage from '../SystemPage';
import type { SystemStatus } from '../../types';

const transport = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));

const status: SystemStatus = {
  appName: 'Sportarr',
  version: '4.1.9.1',
  buildTime: '2026-10-01T00:00:00Z',
  isDebug: false,
  isProduction: true,
  isDocker: true,
  runtimeVersion: '8.0',
  databaseType: 'SQLite',
  databaseVersion: '3.x',
  authentication: 'apikey',
  startTime: '2026-10-01T00:00:00Z',
  appData: '/config',
  osName: 'Linux',
  osVersion: '1',
  branch: 'dev',
  migrationVersion: 12,
  urlBase: '',
};

let client: QueryClient;

beforeEach(() => {
  transport.get.mockReset();
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

afterEach(() => client.clear());

function renderStatus(health: () => Promise<unknown>) {
  transport.get.mockImplementation((path: string) => {
    if (path === '/system/status') return Promise.resolve({ data: status });
    if (path === '/system/health') return health();
    throw new Error(`Unexpected request: ${path}`);
  });

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <SystemPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

it('shows the highest reported health severity instead of a fixed running state', async () => {
  renderStatus(() => Promise.resolve({ data: [
    { type: 0, level: 0, message: 'Root folder is accessible', checkedAt: '2026-10-01T00:00:00Z' },
    { type: 3, level: 2, message: 'Indexer unavailable', checkedAt: '2026-10-01T00:00:00Z' },
  ] }));

  expect(await screen.findByText('Warnings present')).toBeInTheDocument();
  expect(screen.queryByText('Running')).not.toBeInTheDocument();
});

it('does not report healthy when health checks cannot be loaded', async () => {
  renderStatus(() => Promise.reject(new Error('Health endpoint unavailable')));

  expect(await screen.findByText('Unable to check')).toBeInTheDocument();
  expect(screen.queryByText('Running')).not.toBeInTheDocument();
});

it('does not report healthy when the health endpoint returns no checks', async () => {
  renderStatus(() => Promise.resolve({ data: [] }));

  expect(await screen.findByText('No checks reported')).toBeInTheDocument();
});

it('distinguishes a dismissed warning from an active warning', async () => {
  renderStatus(() => Promise.resolve({ data: [
    { type: 3, level: 2, dismissed: true, message: 'Indexer warning', checkedAt: '2026-10-01T00:00:00Z' },
  ] }));

  expect(await screen.findByText('Warnings dismissed')).toBeInTheDocument();
  expect(screen.queryByText('Warnings present')).not.toBeInTheDocument();
});

it('shows an active notice ahead of a dismissed warning', async () => {
  renderStatus(() => Promise.resolve({ data: [
    { type: 3, level: 2, dismissed: true, message: 'Indexer warning', checkedAt: '2026-10-01T00:00:00Z' },
    { type: 6, level: 1, message: 'Update available', checkedAt: '2026-10-01T00:00:00Z' },
  ] }));

  expect(await screen.findByText('Notices present')).toBeInTheDocument();
  expect(screen.queryByText('Warnings dismissed')).not.toBeInTheDocument();
});
