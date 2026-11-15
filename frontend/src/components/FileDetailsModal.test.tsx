import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import FileDetailsModal from './FileDetailsModal';
import { apiGet } from '../utils/api';
import { folderDefaultsFor } from '../utils/folderDefaults';

vi.mock('../utils/api', () => ({
  apiGet: vi.fn(),
}));

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const leagues = [
  { id: 1, name: 'Formula 1', sport: 'Motorsport' },
  { id: 2, name: 'English Premier League', sport: 'Soccer' },
];

const seasonsByLeague: Record<string, string[]> = {
  '1': ['2023', '2024'],
  '2': ['2023-2024', '2024-2025'],
};

function calls(prefix: string): string[] {
  return vi.mocked(apiGet).mock.calls.map(([url]) => url).filter(url => url.startsWith(prefix));
}

function leagueSelect(): HTMLSelectElement {
  return screen.getByRole('option', { name: 'Select a league...' }).closest('select') as HTMLSelectElement;
}

function seasonSelect(): HTMLSelectElement {
  return screen.getByRole('option', { name: 'All seasons' }).closest('select') as HTMLSelectElement;
}

const baseProps = {
  isOpen: true,
  onClose: vi.fn(),
  onSave: vi.fn(),
  fileName: 'a.mkv',
};

describe('FileDetailsModal preselect', () => {
  beforeEach(() => {
    vi.mocked(apiGet).mockReset();
    vi.mocked(apiGet).mockImplementation(async (url: string) => {
      if (url === '/api/leagues') return json(leagues);
      const seasons = url.match(/^\/api\/library\/leagues\/(\d+)\/seasons$/);
      if (seasons) return json({ seasons: seasonsByLeague[seasons[1]] ?? [] });
      if (/^\/api\/library\/leagues\/\d+\/events/.test(url)) return json({ events: [] });
      return json({}, 404);
    });
  });

  it('prefers a saved mapping to the folder defaults', async () => {
    render(
      <FileDetailsModal
        {...baseProps}
        currentMapping={{ leagueId: 1, season: '2023' }}
        folderDefaults={{ leagueId: 2, season: '2024-2025' }}
      />,
    );

    await waitFor(() => expect(seasonSelect().value).toBe('2023'));
    expect(leagueSelect().value).toBe('1');
    expect(calls('/api/library/leagues/2/')).toEqual([]);
    expect(screen.queryByText("Chosen from the file's folder.")).not.toBeInTheDocument();
  });

  it('preselects nothing for a matched file', async () => {
    const matched = { matchedEventId: 41, folderLeagueId: 2, folderSeason: '2024-2025' };
    render(<FileDetailsModal {...baseProps} folderDefaults={folderDefaultsFor(matched)} />);

    await waitFor(() => expect(screen.getByRole('option', { name: 'Formula 1 (Motorsport)' })).toBeInTheDocument());
    expect(leagueSelect().value).toBe('');
    expect(calls('/api/library/leagues/')).toEqual([]);
  });

  it('keeps the folder season after the league seasons load', async () => {
    render(<FileDetailsModal {...baseProps} folderDefaults={{ leagueId: 2, season: '2024-2025' }} />);

    await waitFor(() => expect(seasonSelect().value).toBe('2024-2025'));
    expect(leagueSelect().value).toBe('2');
    await waitFor(() =>
      expect(calls('/api/library/leagues/2/events')).toContain('/api/library/leagues/2/events?season=2024-2025'));
    expect(await screen.findByText("Chosen from the file's folder.")).toBeInTheDocument();
  });

  it('loads the seasons again when it opens on the same league for another file', async () => {
    const { rerender } = render(
      <FileDetailsModal {...baseProps} folderDefaults={{ leagueId: 2, season: '2023-2024' }} />,
    );
    await waitFor(() => expect(seasonSelect().value).toBe('2023-2024'));
    const loadsOnFirstOpen = calls('/api/library/leagues/2/seasons').length;

    rerender(<FileDetailsModal {...baseProps} isOpen={false} folderDefaults={{ leagueId: 2, season: '2023-2024' }} />);
    rerender(
      <FileDetailsModal {...baseProps} fileName="b.mkv" folderDefaults={{ leagueId: 2, season: '2024-2025' }} />,
    );

    await waitFor(() =>
      expect(calls('/api/library/leagues/2/seasons').length).toBeGreaterThan(loadsOnFirstOpen));
    await waitFor(() => expect(seasonSelect().value).toBe('2024-2025'));
    expect(leagueSelect().value).toBe('2');
  });
});
