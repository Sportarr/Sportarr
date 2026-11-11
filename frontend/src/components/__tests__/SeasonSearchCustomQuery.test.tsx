import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import SeasonSearchModal from '../SeasonSearchModal';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), del: vi.fn() }));
vi.mock('../../utils/api', () => ({
  apiGet: transport.get,
  apiPost: transport.post,
  apiDelete: transport.del,
}));

const SEARCH_URL = '/api/leagues/3/seasons/2026/search';

const event = (id: number, title: string) => ({
  eventId: id,
  eventTitle: title,
  eventDate: '2026-03-15T07:00:00Z',
  confidence: 90,
  matchReasons: [],
  hasFile: false,
  monitored: true,
});

const release = (overrides: Record<string, unknown>) => ({
  guid: 'g',
  downloadUrl: 'http://indexer/x',
  indexer: 'Fixture',
  protocol: 'Torrent',
  size: 1024,
  quality: 'WEBDL-1080p',
  seeders: 5,
  leechers: 1,
  publishDate: '2026-03-16T00:00:00Z',
  score: 0,
  qualityScore: 0,
  matchedFormats: [],
  approved: true,
  rejections: [],
  bestConfidence: 90,
  ...overrides,
});

const seasonResults = (searchQuery: string, releases: unknown[]) => ({
  leagueId: 3,
  leagueName: 'Formula 1',
  season: '2026',
  searchQuery,
  eventCount: 2,
  monitoredEventCount: 2,
  downloadedEventCount: 0,
  releases,
  events: [],
});

function ok(data: unknown) {
  return { ok: true, json: async () => data };
}

beforeEach(() => {
  transport.get.mockReset().mockResolvedValue(ok([]));
  transport.del.mockReset();
  transport.post.mockReset().mockImplementation(async (url: string, body: { customQuery?: string }) => {
    if (url === SEARCH_URL && !body.customQuery) {
      return ok(seasonResults('Formula 1 2026', []));
    }
    if (url === SEARCH_URL) {
      return ok(seasonResults(body.customQuery!, [
        release({
          guid: 'pack', title: 'Formula1.2026.Season.Pack.1080p',
          isSeasonPack: true, matchedEventCount: 2,
          matchedEvents: [event(1, 'Australian Grand Prix'), event(2, 'Chinese Grand Prix')],
        }),
        release({
          guid: 'single', title: 'Formula1.2026.Kinai.Nagydij.1080p',
          isSeasonPack: false, matchedEventCount: 1,
          matchedEvents: [event(2, 'Chinese Grand Prix')],
        }),
      ]));
    }
    if (url === '/api/release/grab') return ok({ success: true });
    throw new Error(`Unexpected request: ${url}`);
  });
});

function renderModal() {
  return renderWithProviders(
    <SeasonSearchModal isOpen onClose={() => {}} leagueId={3} leagueName="Formula 1" season="2026" qualityProfileId={5} />,
  );
}

describe('SeasonSearchModal custom search', () => {
  it('searches on open with the quality profile and no custom text', async () => {
    renderModal();

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith(SEARCH_URL, { qualityProfileId: 5 }));
    expect(screen.queryByLabelText('Custom search query')).not.toBeInTheDocument();
  });

  it('pre-fills the custom box with the server query and sends the edited text', async () => {
    const user = userEvent.setup();
    renderModal();
    await screen.findByText('No season packs found for this season.');

    await user.click(screen.getByRole('button', { name: /custom/i }));
    const input = screen.getByLabelText('Custom search query');
    expect(input).toHaveValue('Formula 1 2026');

    await user.clear(input);
    await user.type(input, 'Formula1 2026 Kinai');
    await user.click(screen.getByRole('button', { name: 'Search Custom' }));

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith(SEARCH_URL, {
      qualityProfileId: 5,
      customQuery: 'Formula1 2026 Kinai',
    }));
    expect(await screen.findByText('Found 1 season pack and 1 single-event release')).toBeInTheDocument();
    expect(screen.getByText('Season Pack')).toBeInTheDocument();
    expect(screen.getByText('Single event: Chinese Grand Prix')).toBeInTheDocument();
  });

  it('grabs a single-event release for its one event', async () => {
    const user = userEvent.setup();
    renderModal();
    await screen.findByText('No season packs found for this season.');
    await user.click(screen.getByRole('button', { name: /custom/i }));
    await user.click(screen.getByRole('button', { name: 'Search Custom' }));
    await screen.findByText('Single event: Chinese Grand Prix');

    const buttons = screen.getAllByTitle('Download for Chinese Grand Prix');
    await user.click(buttons[buttons.length - 1]);

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith('/api/release/grab', expect.objectContaining({
      title: 'Formula1.2026.Kinai.Nagydij.1080p',
      eventId: 2,
      isSeasonPack: false,
      matchedEventIds: undefined,
    })));
  });

  it('shows a custom text message when custom text finds nothing', async () => {
    transport.post.mockImplementation(async (_url: string, body: { customQuery?: string }) =>
      ok(seasonResults(body.customQuery ?? 'Formula 1 2026', [])));
    const user = userEvent.setup();
    renderModal();
    expect(await screen.findByText('No season packs found for this season.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Custom search' }));
    await user.click(screen.getByRole('button', { name: 'Search Custom' }));

    expect(await screen.findByText('No releases found for this text.')).toBeInTheDocument();
    expect(screen.queryByText(/use the event-level search/)).not.toBeInTheDocument();
  });
});
