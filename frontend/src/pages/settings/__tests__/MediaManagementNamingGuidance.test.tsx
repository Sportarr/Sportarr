import { beforeEach, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../../test/test-utils';
import MediaManagementSettings from '../MediaManagementSettings';

const transport = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn(), remove: vi.fn() }));
vi.mock('../../../utils/api', () => ({
  apiGet: transport.get,
  apiPut: transport.put,
  apiPost: transport.post,
  apiDelete: transport.remove,
}));

let renameEvents = true;
let standardFileFormat = '';

function ok(data: unknown) {
  return { ok: true, json: async () => data };
}

beforeEach(() => {
  renameEvents = true;
  standardFileFormat = '{Series} - {Season}{Episode} - {Event Title} - {Sportarr Id}';
  transport.get.mockReset().mockImplementation(async (path: string) => {
    if (path === '/api/settings') return ok({ mediaManagementSettings: JSON.stringify({ renameEvents, standardFileFormat }) });
    if (path.startsWith('/api/trash/naming-presets')) return ok({ file: {}, folder: {} });
    return ok([]);
  });
  transport.put.mockReset().mockResolvedValue(ok({}));
  transport.post.mockReset().mockResolvedValue(ok({}));
  transport.remove.mockReset().mockResolvedValue(ok({}));
});

it('explains why TV-style numbers and the Sportarr ID work together', async () => {
  renderWithProviders(<MediaManagementSettings />);

  expect(await screen.findByText(/TV libraries recognize events as episodes/i)).toBeVisible();
  expect(screen.getByText(/like a TVDB or IMDb ID/i)).toBeVisible();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

it.each([
  ['{Series} - {Event Title} - {Sportarr Id}', /Add \{Season\}\{Episode\}/i],
  ['{Series} - {Season} - {Event Title} - {Sportarr Id}', /Add \{Season\}\{Episode\}/i],
  ['{Series} - {Season} - {Episode} - {Event Title} - {Sportarr Id}', /Add \{Season\}\{Episode\}/i],
  ['{Series} - {Season}{Episode} - {Event Title}', /Add \{Sportarr Id\}/i],
  ['{Series} - {Event Title}', /Add \{Season\}\{Episode\} and \{Sportarr Id\}/i],
  ['{Original Filename}', /Original release names vary/i],
])('warns when the format is %s', async (format, warning) => {
  standardFileFormat = format;
  renderWithProviders(<MediaManagementSettings />);

  expect((await screen.findByText(warning)).closest('[role="alert"]')).toBeInTheDocument();
});

it('warns that disabled renaming leaves matching to the source filename', async () => {
  renameEvents = false;
  renderWithProviders(<MediaManagementSettings />);

  expect(await screen.findByRole('alert')).toHaveTextContent(/Renaming is off/i);
});
