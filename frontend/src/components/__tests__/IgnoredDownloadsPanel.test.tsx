import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import apiClient from '../../api/client';
import IgnoredDownloadsPanel from '../IgnoredDownloadsPanel';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

const item = { id: 12, title: 'MotoGP sprint', clientName: 'Downloads', ignoredAt: null };

describe('IgnoredDownloadsPanel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(apiClient.get).mockResolvedValue({ data: [item] });
    vi.mocked(apiClient.post).mockResolvedValue({});
  });

  it('shows ignored downloads and resumes only the selected entry', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: [item, { ...item, id: 13, title: 'Another race' }] });
    render(<IgnoredDownloadsPanel refreshToken={0} />);
    const row = (await screen.findByText(item.title)).closest('li')!;
    expect(within(row).getByText('Downloads')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'Unignore' })).toHaveTextContent('Unignore');
    expect(within(row).getByRole('button', { name: 'Unignore' })).toHaveClass('bg-gray-700');
    await userEvent.click(within(row).getByRole('button', { name: 'Unignore' }));
    expect(apiClient.post).toHaveBeenCalledWith('/queue/ignored/12/resume');
    await waitFor(() => expect(screen.queryByText(item.title)).not.toBeInTheDocument());
    expect(screen.getByText('Another race')).toBeInTheDocument();
    expect(apiClient.get).toHaveBeenCalledTimes(1);
  });

  it('uses the compact table layout and keeps unignore available', async () => {
    render(<IgnoredDownloadsPanel refreshToken={0} compactView />);
    expect(await screen.findByRole('cell', { name: item.title })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unignore' })).toHaveTextContent('Unignore');
    await userEvent.click(screen.getByRole('button', { name: 'Unignore' }));
    expect(apiClient.post).toHaveBeenCalledWith('/queue/ignored/12/resume');
  });

  it('keeps the entry on conflict and allows retry', async () => {
    vi.mocked(apiClient.post).mockRejectedValueOnce({
      isAxiosError: true, response: { data: { error: 'Processing is in progress. Try again.' } },
    });
    render(<IgnoredDownloadsPanel refreshToken={0} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Unignore' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Processing is in progress');
    expect(screen.getByText(item.title)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Unignore' }));
    expect(await screen.findByText('No ignored downloads')).toBeInTheDocument();
  });

  it('recovers a failed load when refreshed', async () => {
    vi.mocked(apiClient.get).mockRejectedValueOnce(new Error('Unavailable'));
    const view = render(<IgnoredDownloadsPanel refreshToken={0} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load');
    view.rerender(<IgnoredDownloadsPanel refreshToken={1} />);
    expect(await screen.findByText(item.title)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('keeps an orphaned ignore visible and resumable', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: [{ ...item, clientName: null }] });
    render(<IgnoredDownloadsPanel refreshToken={0} />);
    expect(await screen.findByText('Download client no longer available')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unignore' })).toBeEnabled();
  });

  it.each([false, true])('discards an obsolete refresh after unignore (failure: %s)', async failure => {
    let finishResume!: (value: object) => void;
    let finishRefresh!: () => void;
    vi.mocked(apiClient.post).mockReturnValue(new Promise(resolve => { finishResume = resolve; }));
    const view = render(<IgnoredDownloadsPanel refreshToken={0} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Unignore' }));
    vi.mocked(apiClient.get).mockReturnValueOnce(new Promise((resolve, reject) => {
      finishRefresh = () => failure ? reject(new Error('Obsolete failure')) : resolve({ data: [item] });
    }));
    view.rerender(<IgnoredDownloadsPanel refreshToken={1} />);
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledTimes(2));
    finishResume({});
    await screen.findByText('No ignored downloads');
    await act(async () => { finishRefresh(); });
    expect(screen.getByText('No ignored downloads')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('confirms a single bulk clear, retaining every row on failure and allowing retry', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: [item, { ...item, id: 13, title: 'Second race' }] });
    vi.mocked(apiClient.post).mockRejectedValueOnce({ isAxiosError: true,
      response: { data: { error: 'Processing is in progress. Try again.' } } });
    render(<IgnoredDownloadsPanel refreshToken={0} />);
    await screen.findByText(item.title);
    await userEvent.click(screen.getByRole('button', { name: 'Clear All' }));
    expect(apiClient.post).not.toHaveBeenCalled();
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Files and release blocks will be kept');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Clear All' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Processing is in progress');
    expect(screen.getByText(item.title)).toBeInTheDocument();
    expect(screen.getByText('Second race')).toBeInTheDocument();
    expect(apiClient.post).toHaveBeenCalledExactlyOnceWith('/queue/ignored/clear');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Clear All' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Clear All' }));
    expect(await screen.findByText('No ignored downloads')).toBeInTheDocument();
    expect(apiClient.post).toHaveBeenCalledTimes(2);
  });
});
