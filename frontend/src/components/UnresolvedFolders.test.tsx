import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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
    expect(screen.getByText('No matching league found on the metadata service.')).toBeInTheDocument();
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
});
