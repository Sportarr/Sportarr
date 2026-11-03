import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ProfilesSettings from '../settings/ProfilesSettings';

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock('../../utils/api', () => ({
  apiGet: api.get,
  apiPut: vi.fn(),
  apiPost: api.post,
  apiDelete: vi.fn(),
}));

describe('delay profile custom format bypass', () => {
  beforeEach(() => {
    api.get.mockReset();
    api.post.mockReset();
    api.get.mockImplementation(async () => ({ ok: true, json: async () => [] }));
    api.post.mockImplementation(async () => ({ ok: true, json: async () => ({}) }));
  });

  it('lets the user turn on the bypass and set the minimum score', async () => {
    const user = userEvent.setup();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <ProfilesSettings />
      </QueryClientProvider>
    );

    await user.click(await screen.findByRole('button', { name: /Add Delay Profile/ }));
    await user.click(screen.getByText('Bypass if Above Custom Format Score'));
    const minimum = screen.getByLabelText('Minimum Custom Format Score');
    await user.clear(minimum);
    await user.type(minimum, '250');
    await user.click(screen.getByRole('button', { name: /Save/ }));

    await waitFor(() => expect(api.post).toHaveBeenCalled());
    const [path, body] = api.post.mock.calls[0];
    expect(path).toBe('/api/delayprofile');
    expect(body.bypassIfAboveCustomFormatScore).toBe(true);
    expect(body.minimumCustomFormatScore).toBe(250);
  });
});
