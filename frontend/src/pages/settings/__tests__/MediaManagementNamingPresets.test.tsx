import { beforeEach, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../../test/test-utils';
import MediaManagementSettings from '../MediaManagementSettings';

const transport = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn(), remove: vi.fn() }));
vi.mock('../../../utils/api', () => ({
  apiGet: transport.get,
  apiPut: transport.put,
  apiPost: transport.post,
  apiDelete: transport.remove,
}));

function ok(data: unknown) {
  return { ok: true, json: async () => data };
}

beforeEach(() => {
  transport.get.mockReset().mockImplementation(async (path: string) => {
    if (path === '/api/settings') return ok({ mediaManagementSettings: JSON.stringify({
      renameEvents: true,
      standardFileFormat: '{Series} - {Event Title} - {Sportarr Id}',
    }) });
    if (path === '/api/rootfolder' || path === '/api/qualityprofile') return ok([]);
    if (path.startsWith('/api/trash/naming-presets')) return ok({
      file: { balanced: {
        format: '{Series} - {Event Title} - {Sportarr Id}',
        description: 'Balanced naming',
        supportsMultiPart: false,
      } },
      folder: {},
    });
    return ok([]);
  });
  transport.put.mockReset().mockResolvedValue(ok({}));
  transport.post.mockReset().mockResolvedValue(ok({}));
  transport.remove.mockReset().mockResolvedValue(ok({}));
});

it('shows the preset matching the saved file format after settings load', async () => {
  renderWithProviders(<MediaManagementSettings />);

  const preset = (await screen.findByRole('option', { name: 'Balanced' })).closest('select');
  await waitFor(() => expect(preset).toHaveValue('balanced'));
  expect(screen.getByDisplayValue('{Series} - {Event Title} - {Sportarr Id}')).toBeInTheDocument();
});

it('shows a custom selection after the file format is edited', async () => {
  const user = userEvent.setup();
  renderWithProviders(<MediaManagementSettings />);

  const preset = (await screen.findByRole('option', { name: 'Balanced' })).closest('select');
  await waitFor(() => expect(preset).toHaveValue('balanced'));
  const format = screen.getByDisplayValue('{Series} - {Event Title} - {Sportarr Id}');
  await user.type(format, ' Extra');

  expect(preset).toHaveValue('');
});

it('loads presets for the saved multi-part setting when settings resolve first', async () => {
  let resolveInitialPresets!: (value: ReturnType<typeof ok>) => void;
  const initialPresets = new Promise<ReturnType<typeof ok>>(resolve => { resolveInitialPresets = resolve; });
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/api/settings') return ok({ mediaManagementSettings: JSON.stringify({
      renameEvents: true,
      enableMultiPartEpisodes: false,
      standardFileFormat: '{Series} - {Season}{Episode} - {Event Title} - {Sportarr Id}',
    }) });
    if (path === '/api/rootfolder' || path === '/api/qualityprofile') return ok([]);
    if (path === '/api/trash/naming-presets?enableMultiPartEpisodes=true') return initialPresets;
    if (path === '/api/trash/naming-presets?enableMultiPartEpisodes=false') return ok({
      file: { balanced: {
        format: '{Series} - {Season}{Episode} - {Event Title} - {Sportarr Id}',
        description: 'Balanced naming',
        supportsMultiPart: true,
      } }, folder: {},
    });
    return ok([]);
  });

  renderWithProviders(<MediaManagementSettings />);
  await waitFor(() => expect(screen.getByDisplayValue(
    '{Series} - {Season}{Episode} - {Event Title} - {Sportarr Id}')).toBeInTheDocument());
  await act(async () => resolveInitialPresets(ok({
    file: { balanced: {
      format: '{Series} - {Season}{Episode}{Part} - {Event Title} - {Sportarr Id}',
      description: 'Balanced naming',
      supportsMultiPart: true,
    } }, folder: {},
  })));

  await waitFor(() => expect(transport.get).toHaveBeenCalledWith(
    '/api/trash/naming-presets?enableMultiPartEpisodes=false'));
  const preset = screen.getByRole('option', { name: /Balanced/ }).closest('select');
  expect(preset).toHaveValue('balanced');
});

it('hides presets for the old multi-part value while the replacement request is pending', async () => {
  let resolveWithoutParts!: (value: ReturnType<typeof ok>) => void;
  const withoutParts = new Promise<ReturnType<typeof ok>>(resolve => { resolveWithoutParts = resolve; });
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/api/settings') return ok({ mediaManagementSettings: JSON.stringify({
      renameEvents: true,
      enableMultiPartEpisodes: true,
      standardFileFormat: '{Series} - {Season}{Episode}{Part} - {Event Title} - {Sportarr Id}',
    }) });
    if (path === '/api/rootfolder' || path === '/api/qualityprofile') return ok([]);
    if (path === '/api/trash/naming-presets?enableMultiPartEpisodes=true') return ok({
      file: { balanced: {
        format: '{Series} - {Season}{Episode}{Part} - {Event Title} - {Sportarr Id}',
        description: 'Balanced naming', supportsMultiPart: true,
      } }, folder: {},
    });
    if (path === '/api/trash/naming-presets?enableMultiPartEpisodes=false') return withoutParts;
    return ok([]);
  });

  const user = userEvent.setup();
  renderWithProviders(<MediaManagementSettings />);
  await screen.findByRole('option', { name: /Balanced/ });
  await user.click(screen.getByRole('checkbox', { name: /Enable Multi-Part Episodes/ }));

  expect(screen.queryByRole('option', { name: /Balanced/ })).not.toBeInTheDocument();
  await act(async () => resolveWithoutParts(ok({
    file: { balanced: {
      format: '{Series} - {Season}{Episode} - {Event Title} - {Sportarr Id}',
      description: 'Balanced naming', supportsMultiPart: true,
    } }, folder: {},
  })));
  const preset = await screen.findByRole('option', { name: /Balanced/ });
  expect(preset.closest('select')).toHaveValue('balanced');
  expect(screen.getByDisplayValue(
    '{Series} - {Season}{Episode} - {Event Title} - {Sportarr Id}')).toBeInTheDocument();
});

it('keeps an older saved preset format as custom instead of claiming it is the revised preset', async () => {
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/api/settings') return ok({ mediaManagementSettings: JSON.stringify({
      renameEvents: true,
      standardFileFormat: '{Event Title} ({Air Date Year}) - {Quality Full} - {Sportarr Id}',
    }) });
    if (path === '/api/rootfolder' || path === '/api/qualityprofile') return ok([]);
    if (path.startsWith('/api/trash/naming-presets')) return ok({
      file: { 'date-based': {
        format: '{Series} - {Season}{Episode} - {Event Title} ({Air Date Year}) - {Quality Full} - {Sportarr Id}',
        description: 'Date-based naming', supportsMultiPart: false,
      } }, folder: {},
    });
    return ok([]);
  });

  renderWithProviders(<MediaManagementSettings />);
  const preset = (await screen.findByRole('option', { name: 'Date Based' })).closest('select');
  expect(preset).toHaveValue('');
  expect(screen.getByRole('option', { name: 'Custom format (choose a preset)' })).toBeInTheDocument();
  expect(screen.getByDisplayValue(
    '{Event Title} ({Air Date Year}) - {Quality Full} - {Sportarr Id}')).toBeInTheDocument();
  expect(transport.put).not.toHaveBeenCalled();
});
