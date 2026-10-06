import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '../../test/test-utils';
import { apiGet } from '../../utils/api';
import Layout from '../Layout';

vi.mock('../../api/hooks', () => ({
  useSystemStatus: () => ({ data: undefined }),
  useSystemHealth: () => ({ data: [] }),
  useActivityCounts: () => ({ data: undefined }),
}));
vi.mock('../../hooks/useNavTarget', () => ({
  useNavTarget: () => '/leagues',
  setNavTarget: vi.fn(),
  setNavTargetFromClick: vi.fn(),
}));
vi.mock('../../hooks/useTheme', () => ({ useResolvedTheme: () => 'dark' }));
vi.mock('../../hooks/useCompactView', () => ({ useIsDesktopLayout: () => false }));
vi.mock('../../contexts/AuthContext', () => ({ useAuth: () => ({ isAuthDisabled: true, logout: vi.fn() }) }));
vi.mock('../../utils/api', () => ({ apiGet: vi.fn(), apiPost: vi.fn() }));
vi.mock('../NavIcon', () => ({ default: () => null }));
vi.mock('../MobileTabBar', () => ({ default: () => null }));
vi.mock('../FooterStatusBar', () => ({ default: () => null }));
vi.mock('../OnboardingWizard', () => ({
  default: ({ isFirstRunGuide }: { isFirstRunGuide?: boolean }) => (
    <div data-testid="setup-guide">{isFirstRunGuide ? 'first run' : 'manual'}</div>
  ),
}));

describe('setup guide entry', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  it('keeps a manually opened guide manual when the automatic status request resolves late', async () => {
    let resolveStatus!: (value: unknown) => void;
    const pendingStatus = new Promise<unknown>((resolve) => { resolveStatus = resolve; });
    vi.mocked(apiGet).mockImplementation(async (url) =>
      url === '/api/onboarding/status' ? pendingStatus as never : { ok: true, json: async () => [] } as never);

    renderWithProviders(<Layout />);
    await waitFor(() => expect(apiGet).toHaveBeenCalledWith('/api/onboarding/status'));
    act(() => window.dispatchEvent(new Event('sportarr:open-setup-guide')));
    expect(screen.getByTestId('setup-guide')).toHaveTextContent('manual');

    await act(async () => resolveStatus({ ok: true, json: async () => ({
      isReady: false, dismissed: false, hasRootFolder: false, hasDownloadClient: false,
      hasEnabledIndexer: false, hasIptvSource: false, monitoredLeagueCount: 0,
    }) }));
    expect(screen.getByTestId('setup-guide')).toHaveTextContent('manual');
  });

  it('uses the recommended naming default for an automatic first-run guide', async () => {
    vi.mocked(apiGet).mockImplementation(async (url) => ({
      ok: true,
      json: async () => url === '/api/onboarding/status' ? {
        isReady: false, dismissed: false, hasRootFolder: false, hasDownloadClient: false,
        hasEnabledIndexer: false, hasIptvSource: false, monitoredLeagueCount: 0,
      } : [],
    }) as never);

    renderWithProviders(<Layout />);
    await waitFor(() => expect(screen.getByTestId('setup-guide')).toHaveTextContent('first run'));
  });

  it('shows Support inside System instead of as a top-level desktop link', () => {
    vi.mocked(apiGet).mockResolvedValue({ ok: false } as never);

    renderWithProviders(<Layout />);
    expect(screen.queryByRole('link', { name: 'Support' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'System' }));
    expect(screen.getByRole('link', { name: 'Support' })).toHaveAttribute('href', '/support');
    expect(screen.queryByRole('link', { name: 'Events' })).not.toBeInTheDocument();
  });
});
