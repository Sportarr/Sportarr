import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ProfilesSettings from '../settings/ProfilesSettings';

const api = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));
vi.mock('../../utils/api', () => ({
  apiGet: api.get,
  apiPut: api.put,
  apiPost: vi.fn(),
  apiDelete: vi.fn(),
}));

const webGroup = {
  name: 'Preferred feeds', quality: 19, id: 2001, allowed: true,
  items: [
    { name: 'WEBDL-2160p', quality: 18, id: 18, allowed: true },
    { name: 'WEBRip-2160p', quality: 17, id: 17, allowed: true },
  ],
};

describe('quality profile group cutoff', () => {
  beforeEach(() => {
    api.get.mockReset();
    api.put.mockReset();
  });

  it('saves a group cutoff using its ID and shows it when reopened', async () => {
    const user = userEvent.setup();
    let profile = {
      id: 1,
      name: 'F1',
      isDefault: true,
      upgradesAllowed: true,
      cutoffQuality: null as number | null,
      items: [webGroup, { name: 'HDTV-1080p', quality: 8, id: 8, allowed: true }],
      formatItems: [],
      formatScoreIncrement: 1,
    };
    api.get.mockImplementation(async (path: string) => ({
      ok: true,
      json: async () => path === '/api/qualityprofile' ? [profile] : [],
    }));
    api.put.mockImplementation(async (_path: string, body: typeof profile) => {
      profile = { ...profile, ...body };
      return { ok: true, json: async () => ({ profile }) };
    });

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ProfilesSettings />
      </QueryClientProvider>
    );

    await user.click(await screen.findByTitle('Edit'));
    const cutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(cutoff).toBeTruthy();
    await user.selectOptions(cutoff!, 'Preferred feeds');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(api.put).toHaveBeenCalled());
    expect(api.put.mock.calls[0][0]).toBe('/api/qualityprofile/1');
    expect(api.put.mock.calls[0][1].cutoffQuality).toBe(2001);
    await waitFor(() => expect(screen.getByText('Cutoff:').parentElement).toHaveTextContent('Preferred feeds'));
    await user.click(await screen.findByTitle('Edit'));
    const reopenedCutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(reopenedCutoff?.value).toBe('2001');
  });

  it('assigns unique cutoff IDs to legacy groups with the same quality value', async () => {
    const user = userEvent.setup();
    let profile = {
      id: 2,
      name: 'Legacy groups',
      isDefault: false,
      upgradesAllowed: true,
      cutoffQuality: 0 as number | null,
      items: [
        { name: 'WEB 1080p', quality: 0, allowed: true, items: [{ name: 'WEBDL-1080p', quality: 0, allowed: true }] },
        { name: 'WEB 720p', quality: 0, allowed: true, items: [{ name: 'WEBDL-720p', quality: 0, allowed: true }] },
      ],
      formatItems: [],
      formatScoreIncrement: 1,
    };
    api.get.mockImplementation(async (path: string) => ({
      ok: true,
      json: async () => path === '/api/qualityprofile' ? [profile] : [],
    }));
    api.put.mockImplementation(async (_path: string, body: typeof profile) => {
      profile = { ...profile, ...body };
      return { ok: true, json: async () => ({ profile }) };
    });

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ProfilesSettings />
      </QueryClientProvider>
    );

    await user.click(await screen.findByTitle('Edit'));
    const cutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(cutoff).toBeTruthy();
    await user.selectOptions(cutoff!, 'WEB 720p');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(api.put).toHaveBeenCalled());
    const saved = api.put.mock.calls[0][1];
    expect(saved.items[0].id).not.toBe(saved.items[1].id);
    expect(saved.cutoffQuality).toBe(saved.items[1].id);
    await waitFor(() => expect(screen.getByText('Cutoff:').parentElement).toHaveTextContent('WEB 720p'));
    await user.click(await screen.findByTitle('Edit'));
    const reopenedCutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(reopenedCutoff?.value).toBe(String(saved.items[1].id));
  });

  it('keeps an unambiguous cutoff and separates groups with duplicate stored IDs', async () => {
    const user = userEvent.setup();
    let profile = {
      id: 3,
      name: 'Existing groups',
      isDefault: false,
      upgradesAllowed: true,
      cutoffQuality: 3 as number | null,
      items: [
        { name: 'First group', quality: 8, id: 1001, allowed: true, items: [{ name: 'HDTV-1080p', quality: 8, allowed: true }] },
        { name: 'Second group', quality: 6, id: 1001, allowed: true, items: [{ name: 'HDTV-720p', quality: 6, allowed: true }] },
        { name: 'SDTV', quality: 3, allowed: true },
      ],
      formatItems: [],
      formatScoreIncrement: 1,
    };
    api.get.mockImplementation(async (path: string) => ({
      ok: true,
      json: async () => path === '/api/qualityprofile' ? [profile] : [],
    }));
    api.put.mockImplementation(async (_path: string, body: typeof profile) => {
      profile = { ...profile, ...body };
      return { ok: true, json: async () => ({ profile }) };
    });

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ProfilesSettings />
      </QueryClientProvider>
    );

    await user.click(await screen.findByTitle('Edit'));
    const cutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(cutoff?.value).toBe('3');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(api.put).toHaveBeenCalledTimes(1));
    expect(api.put.mock.calls[0][1].cutoffQuality).toBe(3);

    await user.click(await screen.findByTitle('Edit'));
    const reopenedCutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    await user.selectOptions(reopenedCutoff!, 'Second group');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(api.put).toHaveBeenCalledTimes(2));
    const saved = api.put.mock.calls[1][1];
    expect(saved.items[0].id).toBe(1001);
    expect(saved.items[1].id).not.toBe(1001);
    expect(saved.cutoffQuality).toBe(saved.items[1].id);
  });

  it('keeps a child cutoff visible when its parent group shares the quality value', async () => {
    const user = userEvent.setup();
    const profile = {
      id: 4,
      name: 'Grouped quality',
      isDefault: false,
      upgradesAllowed: true,
      cutoffQuality: 8,
      items: [{ name: 'HDTV group', quality: 8, id: 1008, allowed: true,
        items: [{ name: 'HDTV-1080p', quality: 8, id: 8, allowed: true }] }],
      formatItems: [],
      formatScoreIncrement: 1,
    };
    api.get.mockImplementation(async (path: string) => ({
      ok: true,
      json: async () => path === '/api/qualityprofile' ? [profile] : [],
    }));

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ProfilesSettings />
      </QueryClientProvider>
    );

    expect(await screen.findByText('Cutoff:')).toBeInTheDocument();
    expect(screen.getByText('Cutoff:').parentElement).toHaveTextContent('HDTV-1080p');
    await user.click(await screen.findByTitle('Edit'));
    const cutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(cutoff?.value).toBe('8');
    expect(cutoff?.selectedOptions[0].textContent).toBe('HDTV-1080p');
  });

  it('keeps a saved zero cutoff visible when several grouped children use zero', async () => {
    const user = userEvent.setup();
    const profile = {
      id: 5,
      name: 'Legacy zero groups',
      isDefault: false,
      upgradesAllowed: true,
      cutoffQuality: 0,
      items: [
        { name: 'A', quality: 0, id: 1000, allowed: true, items: [{ name: 'A1', quality: 0, allowed: true }] },
        { name: 'B', quality: 0, id: 1001, allowed: true, items: [{ name: 'B1', quality: 0, allowed: true }] },
      ],
      formatItems: [],
      formatScoreIncrement: 1,
    };
    api.get.mockImplementation(async (path: string) => ({
      ok: true,
      json: async () => path === '/api/qualityprofile' ? [profile] : [],
    }));

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ProfilesSettings />
      </QueryClientProvider>
    );

    await user.click(await screen.findByTitle('Edit'));
    const cutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(cutoff?.value).toBe('0');
    expect(cutoff?.selectedOptions[0].textContent).toBe('A1');
  });

  it.each([
    {
      name: 'child shares the group quality',
      items: [
        { name: 'A', quality: 0, id: 1000, allowed: true, items: [{ name: 'A1', quality: 0, allowed: true }] },
        { name: 'X', quality: 0, allowed: true },
      ],
      expectedName: 'A1',
      expectedCutoff: 0,
    },
    {
      name: 'child has a different quality',
      items: [
        { name: 'B', quality: 0, id: 1001, allowed: true, items: [{ name: 'B1', quality: 5, allowed: true }] },
        { name: 'X', quality: 0, allowed: true },
      ],
      expectedName: 'B',
      expectedCutoff: 1001,
    },
  ])('shows the backend cutoff for $name', async ({ items, expectedName, expectedCutoff }) => {
    const user = userEvent.setup();
    let profile = {
      id: 6,
      name: 'Mixed legacy group',
      isDefault: false,
      upgradesAllowed: true,
      cutoffQuality: 0,
      items,
      formatItems: [],
      formatScoreIncrement: 1,
    };
    api.get.mockImplementation(async (path: string) => ({
      ok: true,
      json: async () => path === '/api/qualityprofile' ? [profile] : [],
    }));
    api.put.mockImplementation(async (_path: string, body: typeof profile) => {
      profile = { ...profile, ...body };
      return { ok: true, json: async () => ({ profile }) };
    });

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ProfilesSettings />
      </QueryClientProvider>
    );

    expect(await screen.findByText('Cutoff:')).toBeInTheDocument();
    expect(screen.getByText('Cutoff:').parentElement).toHaveTextContent(expectedName);
    await user.click(await screen.findByTitle('Edit'));
    const cutoff = screen.getByText('Upgrade Until').parentElement?.querySelector('select');
    expect(cutoff?.value).toBe(String(expectedCutoff));
    expect(cutoff?.selectedOptions[0].textContent).toBe(expectedName);
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(api.put).toHaveBeenCalled());
    expect(api.put.mock.calls[0][1].cutoffQuality).toBe(expectedCutoff);
  });
});
