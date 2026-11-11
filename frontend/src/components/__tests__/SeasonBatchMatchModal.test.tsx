import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import SeasonBatchMatchModal from '../SeasonBatchMatchModal';

const transport = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../../utils/api', () => ({ apiGet: transport.get }));

const league = { id: 4, name: 'UFC', sport: 'Fighting' };
// The server lists events newest first.
const events = [
  { id: 22, title: 'UFC Fight Night 2', sport: 'Fighting', eventDate: '2024-12-14T23:00:00Z', hasFile: false, usesMultiPart: true, keepsParts: true, files: [] },
  { id: 21, title: 'UFC 310', sport: 'Fighting', eventDate: '2024-12-07T23:00:00Z', hasFile: false, usesMultiPart: true, keepsParts: true, files: [] },
];

function ok(data: unknown) {
  return { ok: true, json: async () => data };
}

beforeEach(() => {
  transport.get.mockReset().mockImplementation(async (path: string) => {
    if (path === '/api/leagues') return ok([league]);
    if (path === '/api/library/leagues/4/seasons') return ok({ seasons: ['2024'] });
    if (path.startsWith('/api/library/leagues/4/events')) return ok({ events });
    if (path === '/api/library/parts/Fighting') return ok({ parts: [{ name: 'Full Event', partNumber: 0 }, { name: 'Prelims', partNumber: 2 }] });
    if (path === '/api/event/21/parts') return ok({ parts: [
      { name: 'Full Event', partNumber: 0 }, { name: 'Early Prelims', partNumber: 1 },
      { name: 'Prelims', partNumber: 2 }, { name: 'Main Card', partNumber: 3 },
    ] });
    if (path === '/api/event/22/parts') return ok({ parts: [
      { name: 'Full Event', partNumber: 0 }, { name: 'Prelims', partNumber: 1 }, { name: 'Main Card', partNumber: 2 },
    ] });
    throw new Error(`Unexpected request: ${path}`);
  });
});

const files = ['a.mkv', 'b.mkv', 'c.mkv', 'd.mkv'].map(name => ({ filePath: `/in/${name}`, fileName: name }));

describe('SeasonBatchMatchModal', () => {
  it('loads the season events oldest first, pairs files as parts and lets the user reorder', async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    renderWithProviders(<SeasonBatchMatchModal isOpen onClose={() => {}} files={files} onConfirm={onConfirm} />);

    await user.selectOptions(await screen.findByLabelText('League'), '4');
    await waitFor(() => expect(screen.getByRole('option', { name: '2024' })).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText('Season'), '2024');

    const list = await screen.findByRole('list', { name: 'Season events' });
    const titles = within(list).getAllByRole('checkbox').map(box => box.closest('label')!.textContent);
    expect(titles[0]).toContain('UFC 310');
    expect(titles[1]).toContain('UFC Fight Night 2');
    expect(transport.get).toHaveBeenCalledWith('/api/library/leagues/4/events?season=2024&limit=500');

    await user.click(screen.getByRole('button', { name: 'Select all' }));
    expect(screen.getByText(/Each event gets 2 files as parts/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Preview pairs/ }));

    const pairs = await screen.findByRole('list', { name: 'File and event pairs' });
    const rows = within(pairs).getAllByRole('listitem').map(row => row.textContent);
    expect(rows[0]).toContain('a.mkv');
    expect(rows[0]).toContain('UFC 310');
    expect(screen.getByLabelText('Part for a.mkv')).toHaveDisplayValue('Prelims');
    expect(screen.getByLabelText('Part for b.mkv')).toHaveDisplayValue('Main Card');
    expect(screen.getByLabelText('Part for c.mkv')).toHaveDisplayValue('Prelims');

    // The same part twice blocks the match until the user fixes it.
    await user.selectOptions(screen.getByLabelText('Part for a.mkv'), 'Main Card');
    expect(screen.getByRole('alert')).toHaveTextContent('Change the parts of UFC 310');
    expect(screen.getByRole('button', { name: 'Match 4 files' })).toBeDisabled();
    await user.selectOptions(screen.getByLabelText('Part for a.mkv'), 'Early Prelims');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Move d.mkv up' }));
    await user.click(screen.getByRole('button', { name: 'Match 4 files' }));

    await waitFor(() => expect(onConfirm).toHaveBeenCalled());
    const result = onConfirm.mock.calls[0][0];
    expect(result).toMatchObject({ leagueId: 4, leagueName: 'UFC', season: '2024' });
    expect(result.pairs.map((p: { file: { fileName: string }; eventId: number; partName: string }) =>
      [p.file.fileName, p.eventId, p.partName])).toEqual([
      ['a.mkv', 21, 'Early Prelims'],
      ['b.mkv', 21, 'Main Card'],
      ['d.mkv', 22, 'Prelims'],
      ['c.mkv', 22, 'Main Card'],
    ]);
  });

  it('blocks a count that does not pair up', async () => {
    const user = userEvent.setup();
    renderWithProviders(<SeasonBatchMatchModal isOpen onClose={() => {}} files={files.slice(0, 3)} onConfirm={vi.fn()} />);

    await user.selectOptions(await screen.findByLabelText('League'), '4');
    await waitFor(() => expect(screen.getByRole('option', { name: '2024' })).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText('Season'), '2024');
    await screen.findByRole('list', { name: 'Season events' });
    await user.click(screen.getByRole('button', { name: 'Select all' }));

    expect(screen.getByRole('alert')).toHaveTextContent('You selected 3 files and ticked 2 events');
    expect(screen.getByRole('button', { name: /Preview pairs/ })).toBeDisabled();
  });

  it('shows a notice when the event list reaches the cap', async () => {
    const many = Array.from({ length: 500 }, (_, i) => ({
      ...events[1], id: 1000 + i, title: `Event ${i}`, eventDate: new Date(Date.UTC(2024, 0, 1 + (i % 300))).toISOString(),
    }));
    transport.get.mockImplementation(async (path: string) => {
      if (path === '/api/leagues') return ok([league]);
      if (path === '/api/library/leagues/4/seasons') return ok({ seasons: ['2024'] });
      if (path.startsWith('/api/library/leagues/4/events')) return ok({ events: many });
      throw new Error(`Unexpected request: ${path}`);
    });
    const user = userEvent.setup();
    renderWithProviders(<SeasonBatchMatchModal isOpen onClose={() => {}} files={files} onConfirm={vi.fn()} />);

    await user.selectOptions(await screen.findByLabelText('League'), '4');
    await waitFor(() => expect(screen.getByRole('option', { name: '2024' })).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText('Season'), '2024');

    expect(await screen.findByText(/The list stops at 500 events/)).toBeInTheDocument();
  });

  it('opens the preview with one file per event when the parts cannot load', async () => {
    const base = transport.get.getMockImplementation()!;
    transport.get.mockImplementation(async (path: string) =>
      path.startsWith('/api/event/') ? { ok: false, json: async () => ({}) } : base(path));
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    renderWithProviders(<SeasonBatchMatchModal isOpen onClose={() => {}} files={files.slice(0, 2)} onConfirm={onConfirm} />);

    await user.selectOptions(await screen.findByLabelText('League'), '4');
    await waitFor(() => expect(screen.getByRole('option', { name: '2024' })).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText('Season'), '2024');
    await screen.findByRole('list', { name: 'Season events' });
    await user.click(screen.getByRole('button', { name: 'Select all' }));
    await user.click(screen.getByRole('button', { name: /Preview pairs/ }));

    await screen.findByRole('list', { name: 'File and event pairs' });
    expect(screen.getByText(/Each file takes its part from the file name/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Part for a.mkv')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Match 2 files' }));
    await waitFor(() => expect(onConfirm).toHaveBeenCalled());
    expect(onConfirm.mock.calls[0][0].pairs.map((p: { partName?: string }) => p.partName)).toEqual([undefined, undefined]);
  });
});
