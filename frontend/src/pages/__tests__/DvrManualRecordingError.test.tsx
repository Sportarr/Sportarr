import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { toast } from 'sonner';
import DvrRecordingsSettings from '../settings/DvrRecordingsSettings';
import { UI_SETTINGS_QUERY_KEY } from '../../hooks/useUISettings';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));
vi.mock('sonner', () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

// jsdom has no ResizeObserver; the headlessui "More" menu needs one to open.
(globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
};

// Sportarr#470: a refused manual recording (409 when a stream cap is full) showed
// only axios's "Request failed with status code 409"; the reason is in the body.
const clients: QueryClient[] = [];
beforeEach(() => {
  transport.get.mockReset();
  transport.post.mockReset();
  vi.mocked(toast.error).mockClear();
});
afterEach(() => {
  for (const client of clients.splice(0)) client.clear();
});

it('shows the reason the server gave when a manual recording is refused', async () => {
  const reason = "Cannot schedule recording: IPTV source 'Provider' is at its MaxStreams cap of 1 during this window. " +
    'Either cancel a conflicting recording, raise the cap, or change DvrConflictPolicy to Queue or Preempt.';
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/dvr/ffmpeg/status') return { data: { available: true } };
    if (path === '/dvr/recordings') return { data: [] };
    if (path === '/dvr/recordings/failed-captures') return { data: [] };
    if (path === '/iptv/channels') return { data: [{ id: 3, name: 'Sports One', isEnabled: true, status: 'Online' }] };
    throw new Error('Unconfigured page request ' + path);
  });
  transport.post.mockRejectedValue(Object.assign(new Error('Request failed with status code 409'), {
    response: { status: 409, data: { error: reason } }
  }));
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  client.setQueryData(UI_SETTINGS_QUERY_KEY, { timezone: 'UTC' });
  render(<QueryClientProvider client={client}><MemoryRouter><DvrRecordingsSettings /></MemoryRouter></QueryClientProvider>);

  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: /more/i }));
  await user.click(await screen.findByText('Manual recording'));
  await user.type(screen.getByPlaceholderText(/Patriots vs Cowboys/), 'Michigan at Kansas State');
  await user.click(screen.getByPlaceholderText(/search channels/i));
  await user.click(await screen.findByRole('button', { name: /Sports One/ }));
  const [start, end] = Array.from(document.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]'));
  fireEvent.change(start, { target: { value: '2030-01-01T10:00' } });
  fireEvent.change(end, { target: { value: '2030-01-01T12:00' } });
  await user.click(screen.getByRole('button', { name: 'Schedule Recording' }));

  await waitFor(() => expect(toast.error).toHaveBeenCalledWith('Failed to schedule recording', { description: reason }));
  expect(await screen.findByText(reason)).toBeInTheDocument();
});
