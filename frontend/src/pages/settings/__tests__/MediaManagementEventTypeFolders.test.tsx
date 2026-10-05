import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
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
    if (path === '/api/settings') return ok({ mediaManagementSettings: '{}' });
    if (path === '/api/rootfolder') return ok([]);
    if (path === '/api/qualityprofile') return ok([]);
    if (path.startsWith('/api/trash/naming-presets')) return ok({ file: {}, folder: {} });
    return ok([]);
  });
  transport.put.mockReset().mockResolvedValue(ok({}));
  transport.post.mockReset().mockResolvedValue(ok({}));
  transport.remove.mockReset().mockResolvedValue(ok({}));
});

describe('Media Management event type folders', () => {
  it('shows the optional group level after season folders and saves the choice', async () => {
    const user = userEvent.setup();
    renderWithProviders(<MediaManagementSettings />);

    const checkbox = await screen.findByRole('checkbox', { name: /Group Events by Type or Session/i });
    expect(checkbox).not.toBeChecked();
    await user.click(checkbox);

    expect(screen.getByText('PPV/')).toBeVisible();
    expect(screen.getByText('/UFC/Season 2024/PPV/UFC 310/')).toBeVisible();
    expect(screen.getByText(/WWE\/Season 2026\/RAW\//)).toBeVisible();
    expect(screen.getByText(/Formula 1\/Season 2026\/Race\//)).toBeVisible();
    await user.click(screen.getAllByRole('button', { name: 'Save Settings' })[0]);

    await waitFor(() => {
      expect(transport.put).toHaveBeenCalledWith('/api/settings', expect.objectContaining({
        mediaManagementSettings: expect.stringContaining('"createEventTypeFolders":true'),
      }));
    });
  });

  it('hides and clears grouping when season folders are turned off', async () => {
    const user = userEvent.setup();
    transport.get.mockImplementation(async (path: string) => {
      if (path === '/api/settings') return ok({ mediaManagementSettings: '{"createEventTypeFolders":true}' });
      if (path.startsWith('/api/trash/naming-presets')) return ok({ file: {}, folder: {} });
      return ok([]);
    });
    renderWithProviders(<MediaManagementSettings />);

    // The checkbox renders with the default (unchecked) before the saved
    // settings arrive, so finding it is not the same as it being loaded.
    await waitFor(() =>
      expect(screen.getByRole('checkbox', { name: /Group Events by Type or Session/i })).toBeChecked());
    await user.click(screen.getByRole('checkbox', { name: /Create Season Folders/i }));

    expect(screen.queryByRole('checkbox', { name: /Group Events by Type or Session/i })).not.toBeInTheDocument();
    await user.click(screen.getAllByRole('button', { name: 'Save Settings' })[0]);

    await waitFor(() => {
      expect(transport.put).toHaveBeenCalledWith('/api/settings', expect.objectContaining({
        mediaManagementSettings: expect.stringContaining('"createEventTypeFolders":false'),
      }));
    });
  });
});
