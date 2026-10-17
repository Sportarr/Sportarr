import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useEffect } from 'react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import SupportIssuePage from '../SupportIssuePage';
import { clearSupportToken, SupportApiError } from '../../api/supportConnection';

const mocks = vi.hoisted(() => ({ request: vi.fn(), readIssue: vi.fn(), githubPosting: true }));

vi.mock('../../api/support', () => ({ readSupportIssue: mocks.readIssue }));
vi.mock('../../api/supportConnection', () => ({
  supportRequest: mocks.request,
  clearSupportToken: vi.fn(),
  getSupportToken: vi.fn(() => 'synthetic-token'),
  SupportApiError: class extends Error {
    constructor(message: string, public status: number) { super(message); }
  },
}));
vi.mock('../../components/SupportConnection', () => ({
  default: ({ onIdentityChange }: { onIdentityChange: (identity: unknown) => void }) => {
    useEffect(() => onIdentityChange({ session_id: 'synthetic', connected_providers: ['github'],
      github_posting: mocks.githubPosting, github_login: 'synthetic-user', expires_at: '2026-12-01T00:00:00Z' }), [onIdentityChange]);
    return <div>Account connected</div>;
  },
}));

function showIssue() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/support/issues/412']}>
    <Link to="/support/issues/413">Next issue</Link>
    <Link to="/support/issues/412">Original issue</Link>
    <Routes><Route path="/support/issues/:number" element={<SupportIssuePage />} /></Routes>
  </MemoryRouter></QueryClientProvider>);
}

describe('Pending support reply', () => {
  afterEach(() => vi.restoreAllMocks());
  beforeEach(() => {
    localStorage.clear();
    mocks.githubPosting = true;
    mocks.request.mockReset();
    mocks.readIssue.mockResolvedValue({ issue: {
      number: 412, title: 'Synthetic report', body: '### What happened\n\nThe import stopped.',
      url: 'https://github.com/Sportarr/Sportarr/issues/412', state: 'open', state_reason: null,
      author: 'synthetic-user', labels: ['bug'], comment_count: 0,
      reactions: { total: 0, thumbs_up: 0, thumbs_down: 0 },
      created_at: '2026-10-01T00:00:00Z', updated_at: '2026-10-01T00:00:00Z',
    }, comments: [], comment_page: 1 });
  });

  it('blocks a duplicate reply after the issue page reloads', async () => {
    mocks.request.mockResolvedValue({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: null, status: 'pending' });
    const first = showIssue();
    await screen.findByRole('heading', { name: 'Reply to this issue' });
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'The same problem still happens.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(localStorage.getItem('sportarr-support-pending-reply:412'))
      .toContain('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'));
    first.unmount();

    showIssue();
    await screen.findByRole('heading', { name: 'Reply to this issue' });
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    expect(screen.getByLabelText('Your reply')).toHaveValue('The same problem still happens.');
    expect(mocks.request).toHaveBeenCalledTimes(1);
  });

  it('keeps one request key when the page unmounts before a reply is confirmed', async () => {
    let finishFirst!: (value: object) => void;
    mocks.request.mockImplementationOnce(() => new Promise((resolve) => { finishFirst = resolve; }))
      .mockResolvedValueOnce({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        github_comment_id: 123, status: 'published' });
    const first = showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'The same problem still happens.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    const originalKey = JSON.parse(mocks.request.mock.calls[0][1].body).idempotency_key;
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toContain(originalKey);
    first.unmount();

    showIssue();
    await screen.findByText('Account connected');
    expect(screen.getByLabelText('Your reply')).toHaveValue('The same problem still happens.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Check saved reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(2));
    expect(JSON.parse(mocks.request.mock.calls[1][1].body).idempotency_key).toBe(originalKey);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled());

    await act(async () => finishFirst({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: null, status: 'pending' }));
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toBeNull();
  });

  it('keeps the queued reply blocked until the original account reconnects', async () => {
    localStorage.setItem('sportarr-support-pending-reply:412', JSON.stringify({
      id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', body: 'The import still fails.',
    }));
    mocks.request.mockRejectedValue(new SupportApiError('Reply not found', 404));
    const interval = vi.spyOn(window, 'setInterval');
    showIssue();
    await screen.findByRole('heading', { name: 'Reply to this issue' });
    await waitFor(() => expect(interval.mock.calls.some(([, delay]) => delay === 10000)).toBe(true));
    const poll = interval.mock.calls.find(([, delay]) => delay === 10000)?.[0];

    await act(async () => { if (typeof poll === 'function') poll(); });

    expect(await screen.findByText(/Sign out of Support here and reconnect the account/)).toBeInTheDocument();
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    expect(screen.getByLabelText('Your reply')).toHaveValue('The import still fails.');
    interval.mockRestore();
  });

  it('keeps a saved request blocked after an unconfirmed check is rejected', async () => {
    mocks.request.mockRejectedValueOnce(new Error('Connection failed'))
      .mockRejectedValueOnce(new SupportApiError('GitHub posting is unavailable. Start a new reply request', 409));
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'The first reply text.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await screen.findByText(/Sportarr could not confirm this reply/);
    const firstKey = JSON.parse(mocks.request.mock.calls[0][1].body).idempotency_key;
    expect(screen.getByLabelText('Your reply')).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Check saved reply' }));
    await screen.findByText('GitHub posting is unavailable. Start a new reply request');
    expect(screen.getByLabelText('Your reply')).toBeDisabled();
    expect(screen.getByLabelText('Your reply')).toHaveValue('The first reply text.');
    expect(screen.queryByRole('dialog', { name: 'Choose reply author' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    expect(mocks.request).toHaveBeenCalledTimes(2);
    expect(JSON.parse(mocks.request.mock.calls[1][1].body).idempotency_key).toBe(firstKey);
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toContain(firstKey);
    fireEvent.click(screen.getByRole('button', { name: 'Start another reply' }));
    expect(screen.getByRole('dialog', { name: 'Check before starting another reply' })).toBeInTheDocument();
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toContain(firstKey);
    fireEvent.click(screen.getByRole('button', { name: 'I checked GitHub, start again' }));
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toBeNull();
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled();
  });

  it.each([401, 429])('keeps a saved reply after a check returns HTTP %i', async (status) => {
    mocks.request.mockRejectedValueOnce(new Error('Connection failed'))
      .mockRejectedValueOnce(new SupportApiError('Check unavailable', status));
    const first = showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'The same problem still happens.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await screen.findByText(/Sportarr could not confirm this reply/);
    const firstKey = JSON.parse(mocks.request.mock.calls[0][1].body).idempotency_key;
    fireEvent.click(screen.getByRole('button', { name: 'Check saved reply' }));
    await screen.findByText('Check unavailable');
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toContain(firstKey);
    first.unmount();

    showIssue();
    await screen.findByText('Account connected');
    expect(screen.getByLabelText('Your reply')).toHaveValue('The same problem still happens.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    expect(mocks.request).toHaveBeenCalledTimes(2);
  });

  it('does not let an older tab overwrite a reply saved by another tab', async () => {
    mocks.request.mockRejectedValue(new Error('Connection failed'));
    const first = showIssue();
    const second = showIssue();
    await waitFor(() => expect(within(first.container).getByText('Account connected')).toBeInTheDocument());
    await waitFor(() => expect(within(second.container).getByText('Account connected')).toBeInTheDocument());
    fireEvent.change(within(first.container).getByRole('textbox'), { target: { value: 'Reply from first tab.' } });
    fireEvent.click(within(first.container).getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    const saved = localStorage.getItem('sportarr-support-pending-reply:412');
    fireEvent.change(within(second.container).getByRole('textbox'), { target: { value: 'Reply from older tab.' } });
    fireEvent.click(within(second.container).getByRole('button', { name: 'Post reply' }));
    expect(mocks.request).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toBe(saved);
    expect(within(second.container).getByRole('button', { name: 'Post reply' })).toBeDisabled();
  });

  it('keeps an unsent draft when another tab saves and clears a reply', async () => {
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'My unsent draft.' } });
    const key = 'sportarr-support-pending-reply:412';
    localStorage.setItem(key, JSON.stringify({ id: null, body: 'Other tab reply.',
      key: 'abcdefabcdefabcdefabcdefabcdefab', mode: 'github' }));
    fireEvent(window, new StorageEvent('storage', { key }));
    expect(screen.getByLabelText('Your reply')).toHaveValue('My unsent draft.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    localStorage.removeItem(key);
    fireEvent(window, new StorageEvent('storage', { key }));
    expect(screen.getByLabelText('Your reply')).toHaveValue('My unsent draft.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled();
  });

  it('shows the saved reply when this tab has no draft', async () => {
    showIssue();
    await screen.findByText('Account connected');
    const key = 'sportarr-support-pending-reply:412';
    localStorage.setItem(key, JSON.stringify({ id: null, body: 'Other tab reply.',
      key: 'abcdefabcdefabcdefabcdefabcdefab', mode: 'github' }));
    fireEvent(window, new StorageEvent('storage', { key }));
    expect(screen.getByLabelText('Your reply')).toHaveValue('Other tab reply.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
  });

  it('keeps a draft after checking another tab’s reply', async () => {
    mocks.request.mockResolvedValueOnce({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: 123, status: 'published' });
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'My unsent draft.' } });
    const key = 'sportarr-support-pending-reply:412';
    localStorage.setItem(key, JSON.stringify({ id: null, body: 'Other tab reply.',
      key: 'abcdefabcdefabcdefabcdefabcdefab', mode: 'github' }));
    fireEvent(window, new StorageEvent('storage', { key }));
    fireEvent.click(screen.getByRole('button', { name: 'Check saved reply' }));
    await waitFor(() => expect(localStorage.getItem(key)).toBeNull());
    expect(screen.getByLabelText('Your reply')).toHaveValue('My unsent draft.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled();
  });

  it('captures an edited draft after a polled reply clears in this tab', async () => {
    const key = 'sportarr-support-pending-reply:412';
    mocks.request.mockResolvedValue({ github_comment_id: 123, status: 'published' });
    const interval = vi.spyOn(window, 'setInterval');
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'My first draft.' } });
    localStorage.setItem(key, JSON.stringify({ id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', body: 'Other tab reply.' }));
    fireEvent(window, new StorageEvent('storage', { key }));
    await waitFor(() => expect(interval.mock.calls.some(([, delay]) => delay === 10000)).toBe(true));
    const poll = interval.mock.calls.find(([, delay]) => delay === 10000)?.[0];
    await act(async () => { if (typeof poll === 'function') poll(); });
    await waitFor(() => expect(localStorage.getItem(key)).toBeNull());
    expect(screen.getByLabelText('Your reply')).toHaveValue('My first draft.');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'My edited draft.' } });
    localStorage.setItem(key, JSON.stringify({ id: null, body: 'A second tab reply.',
      key: 'abcdefabcdefabcdefabcdefabcdefab', mode: 'github' }));
    fireEvent(window, new StorageEvent('storage', { key }));
    expect(screen.getByLabelText('Your reply')).toHaveValue('My edited draft.');
    localStorage.removeItem(key);
    fireEvent(window, new StorageEvent('storage', { key }));
    expect(screen.getByLabelText('Your reply')).toHaveValue('My edited draft.');
    interval.mockRestore();
  });

  it('does not clear another tab’s saved reply when an older request finishes', async () => {
    let finish!: (value: object) => void;
    mocks.request.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }));
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'Original reply.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    const newer = { id: null, body: 'Another tab reply.', key: 'abcdefabcdefabcdefabcdefabcdefab', mode: 'github' };
    localStorage.setItem('sportarr-support-pending-reply:412', JSON.stringify(newer));
    await act(async () => finish({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: 123, status: 'published' }));
    expect(JSON.parse(localStorage.getItem('sportarr-support-pending-reply:412') || '{}')).toEqual(newer);
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
  });

  it('keeps a failed reply blocked when GitHub might have accepted it', async () => {
    mocks.request.mockResolvedValueOnce({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: null, status: 'failed', error: 'GitHub accepted this reply, but Sportarr could not verify it.' });
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'The import still fails.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await screen.findByText('GitHub accepted this reply, but Sportarr could not verify it.');
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toContain('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Start another reply' }));
    expect(screen.getByRole('dialog', { name: 'Check before starting another reply' })).toBeInTheDocument();
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).not.toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'I checked GitHub, start again' }));
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toBeNull();
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled();
  });

  it('starts a new reply request when navigating to another issue', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    mocks.request.mockRejectedValueOnce(new Error('Connection failed'))
      .mockResolvedValueOnce({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        github_comment_id: 123, status: 'published' });
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'Reply for issue 412.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await screen.findByText(/Sportarr could not confirm this reply/);
    const firstKey = JSON.parse(mocks.request.mock.calls[0][1].body).idempotency_key;
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'Reply for issue 413.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(2));
    expect(mocks.request.mock.calls[1][0]).toBe('/issues/413/comments');
    expect(JSON.parse(mocks.request.mock.calls[1][1].body).idempotency_key).not.toBe(firstKey);
  });

  it('tracks a queued reply that is accepted after leaving its issue', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    let finish!: (value: object) => void;
    mocks.request.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'Reply for issue 412.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');

    await act(async () => finish({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: null, status: 'pending' }));

    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toContain('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
    expect(localStorage.getItem('sportarr-support-pending-reply:413')).toBeNull();
    expect(screen.getByLabelText('Your reply')).toHaveValue('');
  });

  it('does not let a late status check clear another issue pending reply', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    localStorage.setItem('sportarr-support-pending-reply:412', JSON.stringify({
      id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', body: 'Queued on issue 412.',
    }));
    localStorage.setItem('sportarr-support-pending-reply:413', JSON.stringify({
      id: 'bbbbbbbb-cccc-dddd-eeee-ffffffffffff', body: 'Queued on issue 413.',
    }));
    let finishStatus!: (value: object) => void;
    mocks.request.mockImplementation(() => new Promise((resolve) => { finishStatus = resolve; }));
    const interval = vi.spyOn(window, 'setInterval');
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    await waitFor(() => expect(interval.mock.calls.some(([, delay]) => delay === 10000)).toBe(true));
    const poll = interval.mock.calls.find(([, delay]) => delay === 10000)?.[0];
    await act(async () => { if (typeof poll === 'function') poll(); });
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');

    await act(async () => finishStatus({ github_comment_id: 123, status: 'published' }));

    expect(screen.getByLabelText('Your reply')).toHaveValue('Queued on issue 413.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    expect(localStorage.getItem('sportarr-support-pending-reply:413')).toContain('bbbbbbbb-cccc-dddd-eeee-ffffffffffff');
    interval.mockRestore();
  });

  it('lets the next issue accept a reply while the previous one is still posting', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    let finish!: (value: object) => void;
    mocks.request.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'Reply for issue 412.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');

    expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled();
    await act(async () => finish({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: null, status: 'pending' }));
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled();
  });

  it('keeps the saved request when returning before its result arrives', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    let finish!: (value: object) => void;
    mocks.request.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }))
      .mockResolvedValueOnce({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        github_comment_id: 123, status: 'published' });
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'First reply for 412.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    const firstKey = JSON.parse(mocks.request.mock.calls[0][1].body).idempotency_key;
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');
    fireEvent.click(screen.getByRole('link', { name: 'Original issue' }));
    await screen.findByText('Report #412');
    expect(screen.getByLabelText('Your reply')).toHaveValue('First reply for 412.');
    expect(screen.getByLabelText('Your reply')).toBeDisabled();

    await act(async () => finish({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: 123, status: 'published' }));
    fireEvent.click(screen.getByRole('button', { name: 'Check saved reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(2));
    expect(JSON.parse(mocks.request.mock.calls[1][1].body).idempotency_key).toBe(firstKey);
    await waitFor(() => expect(screen.getByLabelText('Your reply')).toHaveValue(''));
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeEnabled();
  });

  it('blocks a second reply when an earlier post queues after returning to its issue', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    let finish!: (value: object) => void;
    mocks.request.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }))
      .mockResolvedValueOnce({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        github_comment_id: null, status: 'pending' });
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    fireEvent.change(screen.getByLabelText('Your reply'), { target: { value: 'First reply for 412.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Post reply' }));
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');
    fireEvent.click(screen.getByRole('link', { name: 'Original issue' }));
    await screen.findByText('Report #412');
    expect(screen.getByLabelText('Your reply')).toHaveValue('First reply for 412.');
    expect(screen.getByLabelText('Your reply')).toBeDisabled();

    await act(async () => finish({ comment_id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      github_comment_id: null, status: 'pending' }));

    fireEvent.click(screen.getByRole('button', { name: 'Check saved reply' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Check saved reply' })).not.toBeInTheDocument());
    expect(mocks.request).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText('Your reply')).toHaveValue('First reply for 412.');
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeDisabled();
    expect(localStorage.getItem('sportarr-support-pending-reply:412')).toContain('First reply for 412.');
  });

});

describe('Issue reactions', () => {
  beforeEach(() => {
    localStorage.clear();
    mocks.githubPosting = true;
    mocks.request.mockReset();
    mocks.readIssue.mockReset();
    vi.mocked(clearSupportToken).mockClear();
    mocks.readIssue.mockResolvedValue({ issue: {
      number: 412, title: 'Synthetic report', body: '### What happened\n\nThe import stopped.',
      url: 'https://github.com/Sportarr/Sportarr/issues/412', state: 'open', state_reason: null,
      author: 'synthetic-user', labels: ['bug'], comment_count: 0,
      reactions: { total: 0, thumbs_up: 0, thumbs_down: 0 },
      created_at: '2026-10-01T00:00:00Z', updated_at: '2026-10-01T00:00:00Z',
    }, comments: [], comment_page: 1 });
  });

  it('posts a thumbs-up and stops duplicate clicks in this session', async () => {
    mocks.request.mockResolvedValue({});
    showIssue();
    await screen.findByText('Account connected');

    const button = await screen.findByRole('button', { name: 'Same here' });
    expect(button).toHaveAttribute('title', 'Add a GitHub thumbs-up to show this affects you too');
    expect(button).toHaveAccessibleDescription(/Add a GitHub thumbs-up to show this affects you too/);
    fireEvent.click(button);

    expect(await screen.findByText('Your thumbs-up was added.')).toBeInTheDocument();
    expect(mocks.request).toHaveBeenCalledWith('/issues/412/reactions', {
      method: 'POST', body: JSON.stringify({ content: '+1' }),
    });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(mocks.request).toHaveBeenCalledTimes(1);
  });

  it('explains why an unconnected reader cannot react', async () => {
    mocks.githubPosting = false;
    showIssue();
    await screen.findByText('Account connected');
    const button = await screen.findByRole('button', { name: 'Same here' });
    expect(button).toBeDisabled();
    expect(screen.getByText('Connect GitHub below to react.')).toBeInTheDocument();
    expect(button).toHaveAccessibleDescription(/Connect GitHub below to react/);
  });

  it('announces a failed reaction as an error', async () => {
    mocks.request.mockRejectedValue(new SupportApiError('GitHub unavailable', 503));
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.click(await screen.findByRole('button', { name: 'Same here' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('GitHub unavailable');
  });

  it('clears an expired connection after a reaction returns 401', async () => {
    mocks.request.mockRejectedValue(new SupportApiError('Connection expired', 401));
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.click(await screen.findByRole('button', { name: 'Same here' }));
    await waitFor(() => expect(clearSupportToken).toHaveBeenCalled());
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection expired');
  });

  it('refreshes the GitHub thumbs-up count after reacting', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockResolvedValueOnce(first).mockResolvedValue({
      ...first, issue: { ...first.issue, reactions: { total: 1, thumbs_up: 1, thumbs_down: 0 } },
    });
    mocks.request.mockResolvedValue({});
    showIssue();
    await screen.findByText('Account connected');
    fireEvent.click(await screen.findByRole('button', { name: 'Same here' }));
    expect(await screen.findByText('1 GitHub thumbs-up reaction')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Same here' })).toBeDisabled();
  });

  it('uses neutral confirmation text on a feature request', async () => {
    mocks.readIssue.mockResolvedValue({ issue: {
      number: 412, title: 'Synthetic request', body: '### Feature\n\nA sample feature.',
      url: 'https://github.com/Sportarr/Sportarr/issues/412', state: 'open', state_reason: null,
      author: 'synthetic-user', labels: ['enhancement'], comment_count: 0,
      reactions: { total: 0, thumbs_up: 0, thumbs_down: 0 },
      created_at: '2026-10-01T00:00:00Z', updated_at: '2026-10-01T00:00:00Z',
    }, comments: [], comment_page: 1 });
    mocks.request.mockResolvedValue({});
    showIssue();
    await screen.findByText('Account connected');
    expect(await screen.findByRole('button', { name: 'Same here' })).toHaveAttribute('title', 'Add a GitHub thumbs-up to support this request');
    fireEvent.click(screen.getByRole('button', { name: 'Same here' }));
    expect(await screen.findByText('Your thumbs-up was added.')).toBeInTheDocument();
  });

  it('does not apply a late reaction result to another issue', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    let finish: (result: object) => void = () => undefined;
    mocks.request.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    fireEvent.click(screen.getByRole('button', { name: 'Same here' }));
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');
    expect(screen.getByRole('button', { name: 'Same here' })).toBeEnabled();
    await act(async () => finish({}));
    expect(screen.getByRole('button', { name: 'Same here' })).toBeEnabled();
    expect(screen.queryByText('Your thumbs-up was added.')).not.toBeInTheDocument();
  });

  it('expires a connection when a late reaction returns 401 after navigation', async () => {
    const first = await mocks.readIssue();
    mocks.readIssue.mockImplementation((issueNumber: number) => Promise.resolve({
      ...first, issue: { ...first.issue, number: issueNumber, title: `Report #${issueNumber}` },
    }));
    let rejectReaction: (error: Error) => void = () => undefined;
    mocks.request.mockImplementation(() => new Promise((_resolve, reject) => { rejectReaction = reject; }));
    showIssue();
    await screen.findByText('Report #412');
    await screen.findByText('Account connected');
    fireEvent.click(screen.getByRole('button', { name: 'Same here' }));
    fireEvent.click(screen.getByRole('link', { name: 'Next issue' }));
    await screen.findByText('Report #413');
    await act(async () => rejectReaction(new SupportApiError('Connection expired', 401)));
    expect(clearSupportToken).toHaveBeenCalled();
    expect(screen.queryByText('Connection expired')).not.toBeInTheDocument();
  });
});
