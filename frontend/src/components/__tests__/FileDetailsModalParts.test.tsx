import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import FileDetailsModal from '../FileDetailsModal';

const transport = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../../utils/api', () => ({ apiGet: transport.get }));

const league = { id: 4, name: 'UFC', sport: 'Fighting' };
const ppv = {
  id: 21, title: 'UFC 310', sport: 'Fighting', eventDate: '2024-12-07T23:00:00Z',
  hasFile: false, usesMultiPart: true, keepsParts: true, files: [],
};
const fightNight = {
  id: 22, title: 'UFC Fight Night 248', sport: 'Fighting', eventDate: '2024-11-23T23:00:00Z',
  hasFile: false, usesMultiPart: true, keepsParts: true, files: [],
};

function ok(data: unknown) {
  return { ok: true, json: async () => data };
}

beforeEach(() => {
  transport.get.mockReset().mockImplementation(async (path: string) => {
    if (path === '/api/leagues') return ok([league]);
    if (path === '/api/library/leagues/4/seasons') return ok({ seasons: ['2024'] });
    if (path.startsWith('/api/library/leagues/4/events')) return ok({ events: [fightNight, ppv] });
    // The sport list numbers Main Card 3. A Fight Night numbers it 2.
    if (path === '/api/library/parts/Fighting') return ok({ parts: [
      { name: 'Full Event', partNumber: 0 }, { name: 'Early Prelims', partNumber: 1 },
      { name: 'Prelims', partNumber: 2 }, { name: 'Main Card', partNumber: 3 },
    ] });
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

describe('FileDetailsModal parts', () => {
  it('offers the parts of the picked event, with its own part numbers', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn();
    renderWithProviders(<FileDetailsModal isOpen onClose={() => {}} onSave={onSave} fileName="02 - FN248 Fokartya.mkv" />);

    await waitFor(() => expect(screen.getByRole('option', { name: 'UFC (Fighting)' })).toBeInTheDocument());
    const [leagueSelect] = screen.getAllByRole('combobox');
    await user.selectOptions(leagueSelect, '4');
    await waitFor(() => expect(screen.getByRole('option', { name: '2024' })).toBeInTheDocument());
    await user.selectOptions(screen.getAllByRole('combobox')[1], '2024');
    await user.click(await screen.findByText('UFC Fight Night 248'));

    expect(await screen.findByRole('option', { name: 'Main Card (pt2)' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /Early Prelims/ })).not.toBeInTheDocument();
    expect(transport.get).toHaveBeenCalledWith('/api/event/22/parts');
    expect(transport.get).not.toHaveBeenCalledWith('/api/library/parts/Fighting');

    await user.selectOptions(screen.getAllByRole('combobox')[2], 'Main Card');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ eventId: 22, partName: 'Main Card', partNumber: 2 }));
  });

  it('falls back to the sport list when the event parts cannot load', async () => {
    const base = transport.get.getMockImplementation()!;
    transport.get.mockImplementation(async (path: string) =>
      path === '/api/event/22/parts' ? { ok: false, json: async () => ({}) } : base(path));
    const user = userEvent.setup();
    renderWithProviders(<FileDetailsModal isOpen onClose={() => {}} onSave={vi.fn()} fileName="x.mkv" />);

    await waitFor(() => expect(screen.getByRole('option', { name: 'UFC (Fighting)' })).toBeInTheDocument());
    await user.selectOptions(screen.getAllByRole('combobox')[0], '4');
    await waitFor(() => expect(screen.getByRole('option', { name: '2024' })).toBeInTheDocument());
    await user.selectOptions(screen.getAllByRole('combobox')[1], '2024');
    await user.click(await screen.findByText('UFC Fight Night 248'));

    expect(await screen.findByRole('option', { name: 'Main Card (pt3)' })).toBeInTheDocument();
  });

  async function openOnSeason(user: ReturnType<typeof userEvent.setup>, onSave = vi.fn()) {
    renderWithProviders(<FileDetailsModal isOpen onClose={() => {}} onSave={onSave} fileName="x.mkv" />);
    await waitFor(() => expect(screen.getByRole('option', { name: 'UFC (Fighting)' })).toBeInTheDocument());
    await user.selectOptions(screen.getAllByRole('combobox')[0], '4');
    await waitFor(() => expect(screen.getByRole('option', { name: '2024' })).toBeInTheDocument());
    await user.selectOptions(screen.getAllByRole('combobox')[1], '2024');
    return onSave;
  }

  it('takes the part number of the new event when the event changes', async () => {
    const user = userEvent.setup();
    const onSave = await openOnSeason(user);
    await user.click(await screen.findByText('UFC 310'));
    await screen.findByRole('option', { name: 'Main Card (pt3)' });
    await user.selectOptions(screen.getAllByRole('combobox')[2], 'Main Card');

    await user.click(screen.getByText('UFC Fight Night 248'));
    await screen.findByRole('option', { name: 'Main Card (pt2)' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());
    expect(screen.getAllByRole('combobox')[2]).toHaveValue('Main Card');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ eventId: 22, partName: 'Main Card', partNumber: 2 }));
  });

  it('clears a part the new event does not have', async () => {
    const user = userEvent.setup();
    const onSave = await openOnSeason(user);
    await user.click(await screen.findByText('UFC 310'));
    await screen.findByRole('option', { name: 'Early Prelims (pt1)' });
    await user.selectOptions(screen.getAllByRole('combobox')[2], 'Early Prelims');

    await user.click(screen.getByText('UFC Fight Night 248'));
    await screen.findByRole('option', { name: 'Main Card (pt2)' });
    await waitFor(() => expect(screen.getAllByRole('combobox')[2]).toHaveValue(''));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ eventId: 22, partName: undefined, partNumber: undefined }));
  });
});
