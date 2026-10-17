import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../../test/test-utils';
import DownloadClientsSettings from '../DownloadClientsSettings';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn() }));
const settings = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));

vi.mock('../../../api/client', () => ({ default: transport }));
vi.mock('../../../utils/api', () => ({ apiGet: settings.get, apiPut: settings.put }));

beforeEach(() => {
  transport.get.mockReset().mockResolvedValue({ data: [] });
  transport.post.mockReset().mockResolvedValue({ data: {} });
  transport.put.mockReset().mockResolvedValue({ data: {} });
  settings.get.mockReset().mockResolvedValue({ ok: true, json: async () => ({}) });
  settings.put.mockReset().mockResolvedValue({ ok: true, json: async () => ({}) });
});

describe('download client seeding defaults', () => {
  it('removes completed torrents after the client stops at its seed goal', async () => {
    const user = userEvent.setup();
    renderWithProviders(<DownloadClientsSettings />);

    await user.click(await screen.findByRole('button', { name: 'Add Download Client' }));
    await user.click(screen.getByRole('button', { name: /qBittorrent/i }));

    expect(screen.getByRole('checkbox', { name: /Remove Completed Downloads/i })).toBeChecked();
    expect(screen.getByText(/With no goal or while all goals remain unmet, Sportarr leaves the torrent in the client/i)).toBeVisible();
  });

  it('removes completed Usenet downloads by default', async () => {
    const user = userEvent.setup();
    renderWithProviders(<DownloadClientsSettings />);

    await user.click(await screen.findByRole('button', { name: 'Add Download Client' }));
    await user.click(screen.getAllByRole('button', { name: /SABnzbd/i })[0]);

    expect(screen.getByRole('checkbox', { name: /Remove Completed Downloads/i })).toBeChecked();
  });
});
