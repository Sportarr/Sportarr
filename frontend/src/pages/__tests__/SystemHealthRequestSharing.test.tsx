import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import Layout from '../../components/Layout';
import SystemPage from '../SystemPage';
import SystemHealthPage from '../SystemHealthPage';

const transport = vi.hoisted(() => ({ get: vi.fn(), delete: vi.fn() }));
const requests = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));
vi.mock('../../utils/api', () => ({ apiGet: requests.get, apiPost: requests.post }));
vi.mock('../../hooks/useTheme', () => ({ useResolvedTheme: () => 'dark' }));
vi.mock('../../hooks/useCompactView', () => ({ useIsDesktopLayout: () => true }));
vi.mock('../../contexts/AuthContext', () => ({ useAuth: () => ({ isAuthDisabled: true, logout: vi.fn() }) }));
vi.mock('../../components/MobileTabBar', () => ({ default: () => null }));
vi.mock('../../components/FooterStatusBar', () => ({ default: () => null }));
vi.mock('../../components/OnboardingWizard', () => ({ default: () => null }));

const healthFixture = [
  { type: 3, level: 2, message: 'Sample indexer warning', checkedAt: '2026-10-01T00:00:00Z' },
];
let health: Array<(typeof healthFixture)[number] & { dismissed?: boolean }>;

let client: QueryClient;

beforeEach(() => {
  health = healthFixture.map((check) => ({ ...check }));
  requests.get.mockReset().mockImplementation(async (path: string) => ({
    ok: true,
    json: async () => path === '/api/system/health' ? health : { isReady: true, dismissed: true },
  }));
  requests.post.mockReset().mockImplementation(async () => {
    health = health.map((check) => ({ ...check, dismissed: true }));
    return { ok: true };
  });
  transport.delete.mockReset().mockImplementation(async () => {
    health = health.map((check) => ({ ...check, dismissed: false }));
    return { data: {} };
  });
  transport.get.mockReset().mockImplementation(async (path: string) => {
    if (path === '/system/health') return { data: health };
    if (path === '/activity/counts') return { data: { queueCount: 0, pendingImportCount: 0 } };
    if (path === '/system/status') return { data: {
      appName: 'Sportarr', version: '4.1.9.1', buildTime: '2026-10-01T00:00:00Z',
      startTime: '2026-10-01T00:00:00Z', isProduction: true, isDocker: true,
      runtimeVersion: '8.0', databaseType: 'SQLite', databaseVersion: '3.x',
      authentication: 'apikey', appData: '/example/config', osName: 'Linux',
      osVersion: 'sample', branch: 'dev', migrationVersion: 12,
    } };
    throw new Error(`Unexpected request: ${path}`);
  });
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

afterEach(() => client.clear());

function renderScreen(path = '/system/status') {
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/" element={<Layout />}>
            <Route path="system/status" element={<SystemPage />} />
            <Route path="system/health" element={<SystemHealthPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

it('uses one health request for the status page and its persistent warning banner', async () => {
  renderScreen();

  expect(await screen.findByText('Warnings present')).toBeInTheDocument();
  expect(await screen.findByText('Sample indexer warning')).toBeInTheDocument();
  const bannerCalls = requests.get.mock.calls.filter(([path]) => path === '/api/system/health').length;
  const statusCalls = transport.get.mock.calls.filter(([path]) => path === '/system/health').length;
  expect(bannerCalls + statusCalls).toBe(1);
});

it('shows a dismissed warning on Status after hiding its banner', async () => {
  renderScreen();
  await screen.findByText('Sample indexer warning');

  await userEvent.setup().click(screen.getByTitle(/Dismiss this warning/));

  expect(await screen.findByText('Warnings dismissed')).toBeInTheDocument();
  expect(screen.queryByText('Sample indexer warning')).not.toBeInTheDocument();
});

it('restores a warning when the server rejects a banner dismissal', async () => {
  requests.post.mockResolvedValue({ ok: false });
  renderScreen();
  await screen.findByText('Sample indexer warning');

  await userEvent.setup().click(screen.getByTitle(/Dismiss this warning/));
  await waitFor(() => expect(requests.post).toHaveBeenCalled());
  await waitFor(() => expect(transport.get.mock.calls.filter(([path]) => path === '/system/health')).toHaveLength(2));
  expect(screen.getByText('Warnings present')).toBeInTheDocument();
  expect(screen.getByText('Sample indexer warning')).toBeInTheDocument();
});

it('shares the persistent banner request with the Health page', async () => {
  renderScreen('/system/health');

  await waitFor(() => expect(screen.getAllByText('Sample indexer warning')).toHaveLength(2));
  expect(transport.get.mock.calls.filter(([path]) => path === '/system/health')).toHaveLength(1);
});

it('updates the banner when a warning is restored on the Health page', async () => {
  health = health.map((check) => ({ ...check, dismissed: true }));
  renderScreen('/system/health');
  await screen.findByRole('button', { name: 'Restore' });

  await userEvent.setup().click(screen.getByRole('button', { name: 'Restore' }));

  await waitFor(() => expect(screen.getAllByText('Sample indexer warning')).toHaveLength(2));
  expect(client.getQueryData<typeof health>(['system', 'health'])?.[0].dismissed).toBe(false);
});

it('does not present a banner dismissal as the last completed health check', async () => {
  let resolveDismiss!: (value: { ok: boolean }) => void;
  requests.post.mockImplementation(() => new Promise((resolve) => { resolveDismiss = resolve; }));
  renderScreen('/system/health');
  await screen.findAllByText('Sample indexer warning');
  const checkedAt = client.getQueryState(['system', 'health'])!.dataUpdatedAt;
  const clock = vi.spyOn(Date, 'now').mockReturnValue(checkedAt + 10000);

  try {
    await userEvent.setup().click(screen.getByTitle(/Dismiss this warning/));
    await waitFor(() => expect(requests.post).toHaveBeenCalled());
    expect(client.getQueryState(['system', 'health'])!.dataUpdatedAt).toBe(checkedAt);
  } finally {
    clock.mockRestore();
    health = health.map((check) => ({ ...check, dismissed: true }));
    resolveDismiss?.({ ok: true });
  }
});

it('keeps a warning hidden while its dismissal is still being saved', async () => {
  let resolveDismiss!: (value: { ok: boolean }) => void;
  requests.post.mockImplementation(() => new Promise((resolve) => { resolveDismiss = resolve; }));
  renderScreen();
  await screen.findByText('Sample indexer warning');

  await userEvent.setup().click(screen.getByTitle(/Dismiss this warning/));
  await waitFor(() => expect(requests.post).toHaveBeenCalled());
  await act(async () => {
    client.setQueryData(['system', 'health'], health.map((check) => ({
      ...check,
      message: 'Warning returned from a late poll',
    })));
  });

  await screen.findByText('Warnings present');
  expect(screen.queryByText('Warning returned from a late poll')).not.toBeInTheDocument();
  health = health.map((check) => ({ ...check, dismissed: true }));
  await act(async () => resolveDismiss({ ok: true }));
});
