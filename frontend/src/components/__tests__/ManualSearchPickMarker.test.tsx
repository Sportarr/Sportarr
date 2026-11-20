import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import ManualSearchModal from '../ManualSearchModal';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), del: vi.fn() }));
vi.mock('../../utils/api', () => ({
  apiGet: transport.get,
  apiPost: transport.post,
  apiDelete: transport.del,
}));

const TITLE = 'FIFA.World.Cup.2020.09.01.720p.WEB-DL.H264-Loose';

function ok(data: unknown) {
  return { ok: true, json: async () => data };
}

beforeEach(() => {
  transport.get.mockReset().mockResolvedValue(ok([]));
  transport.del.mockReset();
  transport.post.mockReset().mockImplementation(async (url: string) => {
    if (url === '/api/event/7/search') {
      return ok({
        results: [{
          title: TITLE, guid: 'g', downloadUrl: 'http://indexer/x', indexer: 'Fixture', indexerId: 1,
          protocol: 'Usenet', size: 1024, quality: 'WEBDL-720p', publishDate: '2020-09-02T00:00:00Z',
          score: 0, qualityScore: 0, customFormatScore: 0, matchScore: 30, matchedFormats: [],
          approved: true, rejections: [], isBlocklisted: false, isPack: false, part: null,
        }],
        skipped: [],
        searchComplete: true,
        searchDiagnostics: [],
      });
    }
    if (url === '/api/release/grab') return ok({ success: true });
    throw new Error(`Unexpected request: ${url}`);
  });
});

describe('ManualSearchModal grab', () => {
  it('marks the grab as a pick from the manual search list', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ManualSearchModal isOpen onClose={() => {}} eventId={7} eventTitle="Spain vs Belgium" />);
    await screen.findAllByText(TITLE);

    const buttons = screen.getAllByTitle('Download');
    await user.click(buttons[0]);

    await waitFor(() => expect(transport.post).toHaveBeenCalledWith('/api/release/grab', expect.objectContaining({
      title: TITLE,
      eventId: 7,
      pickedFrom: 'manualSearch',
    })));
  });
});
