import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import ManualImportModal from '../ManualImportModal';

const transport = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn() }));
vi.mock('../../utils/api', () => ({
  apiGet: transport.get,
  apiPut: transport.put,
  apiPost: transport.post,
}));
vi.mock('../FileMetadataEditor', () => ({ default: () => null }));

const event = {
  id: 7,
  title: 'UFC 9999',
  sport: 'Fighting',
  eventDate: '2020-09-01T20:00:00Z',
  season: '2020',
  hasFile: false,
  usesMultiPart: true,
  files: [],
  league: { id: 4, name: 'UFC', sport: 'Fighting' },
};
const pendingImport = {
  id: 11,
  title: 'UFC.9999.Main.Card.720p.WEB-DL.mkv',
  filePath: '/data/e2e-lib/UFC.9999.Main.Card.720p.WEB-DL.mkv',
  size: 4096,
  quality: 'WEBDL-720p',
  qualityScore: 50,
  suggestedEventId: event.id,
  suggestedEvent: event,
  suggestedPart: 'Main Card',
  suggestionConfidence: 100,
  detected: '2020-09-02T00:00:00Z',
};

function ok(data: unknown) {
  return { ok: true, json: async () => data };
}

beforeEach(() => {
  transport.get.mockReset().mockImplementation(async (path: string) => {
    if (path === '/api/settings') return ok({ mediaManagementSettings: '{}' });
    if (path === '/api/leagues') return ok([event.league]);
    if (path === '/api/pending-imports/11/matches') return ok([]);
    if (path.endsWith('/seasons')) return ok({ seasons: ['2020'] });
    if (path.includes('/events')) return ok({ events: [event] });
    if (path === '/api/library/parts/Fighting') return ok({ parts: [
      { name: 'Full Event', partNumber: 0 },
      { name: 'Prelims', partNumber: 2 },
      { name: 'Main Card', partNumber: 3 },
    ] });
    throw new Error(`Unexpected request: ${path}`);
  });
  transport.put.mockReset().mockResolvedValue(ok({}));
  transport.post.mockReset().mockResolvedValue(ok({}));
});

describe('Manual import part selection', () => {
  it.each(['Full Event', 'Prelims'])('does not send the old part after selecting %s', async (part) => {
    const user = userEvent.setup();
    renderWithProviders(<ManualImportModal pendingImport={pendingImport} onClose={vi.fn()} onSuccess={vi.fn()} />);

    let partSelect: HTMLSelectElement | undefined;
    await waitFor(() => {
      partSelect = screen.getAllByRole('combobox').find((select) =>
        within(select).queryByRole('option', { name: /Full Event/ }) !== null) as HTMLSelectElement | undefined;
      expect(partSelect).toBeDefined();
    });
    await user.selectOptions(partSelect!, part);
    await user.click(screen.getByRole('button', { name: /^Import$/ }));

    await waitFor(() => expect(transport.post).toHaveBeenCalledTimes(1));
    expect(transport.put).toHaveBeenCalledWith('/api/pending-imports/11/suggestion', {
      eventId: 7,
      part,
    });
    const overrides = transport.post.mock.calls[0][1].metadataOverrides;
    expect(overrides).not.toHaveProperty('partName');
    expect(overrides).not.toHaveProperty('partNumber');
  });
});
