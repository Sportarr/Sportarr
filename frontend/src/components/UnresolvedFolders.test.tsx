import { render as rtlRender, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import UnresolvedFolders, { type UnresolvedFolder } from './UnresolvedFolders';
import { apiGet, apiPost } from '../utils/api';

vi.mock('../utils/api', () => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let queryClient: QueryClient;

const render = (ui: ReactElement) =>
  rtlRender(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const withSuggestion: UnresolvedFolder = {
  path: '/library/Formula 1',
  name: 'Formula 1',
  suggestedLeagueExternalId: 'lg-000278',
  suggestedLeagueName: 'Formula 1',
  suggestedSport: 'Motorsport',
};

const withoutSuggestion: UnresolvedFolder = {
  path: '/library/Home Videos',
  name: 'Home Videos',
  suggestedLeagueExternalId: null,
  suggestedLeagueName: null,
  suggestedSport: null,
};

describe('UnresolvedFolders', () => {
  beforeEach(() => {
    queryClient = new QueryClient();
    vi.mocked(apiGet).mockReset();
    vi.mocked(apiPost).mockReset();
    vi.mocked(apiGet).mockImplementation(async (url: string) => {
      if (url === '/api/rootfolder') {
        return json([
          { id: 1, path: '/other', defaultQualityProfileId: null },
          { id: 2, path: '/library', defaultQualityProfileId: null },
        ]);
      }
      if (url === '/api/qualityprofile') return json([{ id: 7 }, { id: 8 }]);
      return json({}, 404);
    });
  });

  it('renders nothing when every folder has a league', () => {
    const { container } = render(<UnresolvedFolders folders={[]} onLeagueAdded={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('offers Add when the metadata service has a suggestion and plain text when it has none', () => {
    render(<UnresolvedFolders folders={[withSuggestion, withoutSuggestion]} onLeagueAdded={vi.fn()} />);

    expect(screen.getByText('Folder "Formula 1" is not one of your leagues.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add Formula 1' })).toBeInTheDocument();
    expect(screen.getByText('Folder "Home Videos" is not one of your leagues.')).toBeInTheDocument();
    expect(screen.getByText('No matching league found. Add the league from the Leagues page, then scan again.')).toBeInTheDocument();
    expect(screen.getAllByRole('button')).toHaveLength(1);
  });

  it('adds the suggested league, then calls the rescan', async () => {
    vi.mocked(apiPost).mockResolvedValue(json({ id: 5, monitored: true }, 201));
    const onLeagueAdded = vi.fn().mockResolvedValue(undefined);
    render(<UnresolvedFolders folders={[withSuggestion]} onLeagueAdded={onLeagueAdded} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add Formula 1' }));

    await waitFor(() => expect(onLeagueAdded).toHaveBeenCalledTimes(1));
    expect(apiPost).toHaveBeenCalledTimes(1);
    const [url, body] = vi.mocked(apiPost).mock.calls[0];
    expect(url).toBe('/api/leagues');
    expect(body).toMatchObject({
      externalId: 'lg-000278',
      name: 'Formula 1',
      sport: 'Motorsport',
      monitored: true,
      monitorType: 'Future',
      rootFolderId: 2,
      qualityProfileId: 7,
    });
    expect(vi.mocked(apiPost).mock.invocationCallOrder[0])
      .toBeLessThan(onLeagueAdded.mock.invocationCallOrder[0]);
  });

  it('shows the server error on the row and does not rescan when the add fails', async () => {
    vi.mocked(apiPost).mockResolvedValue(json({ error: 'League already exists in library' }, 400));
    const onLeagueAdded = vi.fn();
    render(<UnresolvedFolders folders={[withSuggestion]} onLeagueAdded={onLeagueAdded} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add Formula 1' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('League already exists in library');
    expect(onLeagueAdded).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Add Formula 1' })).toBeEnabled();
  });
  it('uses the hub sport format to monitor a league that has no teams', async () => {
    vi.mocked(apiPost).mockResolvedValue(json({ id: 6, monitored: true }, 201));
    const folder: UnresolvedFolder = {
      path: '/library/Tour de France',
      name: 'Tour de France',
      suggestedLeagueExternalId: 'lg-000777',
      suggestedLeagueName: 'Tour de France',
      suggestedSport: 'Cycling',
      suggestedSportFormat: 'EventSport',
    };
    const onLeagueAdded = vi.fn().mockResolvedValue(undefined);
    render(<UnresolvedFolders folders={[folder]} onLeagueAdded={onLeagueAdded} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add Tour de France' }));

    await waitFor(() => expect(onLeagueAdded).toHaveBeenCalledTimes(1));
    expect(vi.mocked(apiPost).mock.calls[0][1]).toMatchObject({
      sport: 'Cycling',
      sportFormat: 'EventSport',
      monitored: true,
    });
  });

  it('matches a Windows root folder whatever the letter case', async () => {
    vi.mocked(apiGet).mockImplementation(async (url: string) => {
      if (url === '/api/rootfolder') {
        return json([{ id: 3, path: 'D:\\Sports\\', defaultQualityProfileId: 9 }]);
      }
      if (url === '/api/qualityprofile') return json([{ id: 7 }]);
      return json({}, 404);
    });
    vi.mocked(apiPost).mockResolvedValue(json({ id: 5, monitored: true }, 201));
    const folder = { ...withSuggestion, path: 'd:\\sports\\Formula 1' };
    const onLeagueAdded = vi.fn().mockResolvedValue(undefined);
    render(<UnresolvedFolders folders={[folder]} onLeagueAdded={onLeagueAdded} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add Formula 1' }));

    await waitFor(() => expect(onLeagueAdded).toHaveBeenCalledTimes(1));
    expect(vi.mocked(apiPost).mock.calls[0][1]).toMatchObject({ rootFolderId: 3, qualityProfileId: 9 });
  });

  it('keeps the letter case of a path that is not a Windows path', async () => {
    vi.mocked(apiGet).mockImplementation(async (url: string) => {
      if (url === '/api/rootfolder') return json([{ id: 4, path: '/Library', defaultQualityProfileId: 9 }]);
      if (url === '/api/qualityprofile') return json([{ id: 7 }]);
      return json({}, 404);
    });
    vi.mocked(apiPost).mockResolvedValue(json({ id: 5, monitored: true }, 201));
    const onLeagueAdded = vi.fn().mockResolvedValue(undefined);
    render(<UnresolvedFolders folders={[withSuggestion]} onLeagueAdded={onLeagueAdded} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add Formula 1' }));

    await waitFor(() => expect(onLeagueAdded).toHaveBeenCalledTimes(1));
    expect(vi.mocked(apiPost).mock.calls[0][1]).toMatchObject({ rootFolderId: null, qualityProfileId: 7 });
  });

  it('refreshes the league lists after an add', async () => {
    vi.mocked(apiPost).mockResolvedValue(json({ id: 5, monitored: true }, 201));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const onLeagueAdded = vi.fn().mockResolvedValue(undefined);
    render(<UnresolvedFolders folders={[withSuggestion]} onLeagueAdded={onLeagueAdded} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add Formula 1' }));

    await waitFor(() => expect(onLeagueAdded).toHaveBeenCalledTimes(1));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['leagues'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['sportarr-leagues'] });
  });

  it('does not refresh the league lists when the add fails', async () => {
    vi.mocked(apiPost).mockResolvedValue(json({ error: 'League already exists in library' }, 400));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    render(<UnresolvedFolders folders={[withSuggestion]} onLeagueAdded={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add Formula 1' }));

    await screen.findByRole('alert');
    expect(invalidate).not.toHaveBeenCalled();
  });
});
