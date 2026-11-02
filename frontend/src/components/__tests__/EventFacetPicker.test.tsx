import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import EventFacetPicker from '../EventFacetPicker';
import type { FacetRule, FacetSchema } from '../../utils/eventFacets';

const SCHEMA: FacetSchema = {
  id: 'fis-alpine',
  dimensions: [
    { key: 'gender', label: 'Gender', values: ['Men', 'Women', 'Mixed'] },
    { key: 'discipline', label: 'Discipline', values: ['Downhill', 'Super G', 'Slalom'] },
  ],
};

// Holds the value the way AddLeagueModal does, so each click sees the last save.
function Harness({ initial, onSave }: { initial: FacetRule[] | null; onSave: (rules: FacetRule[] | null) => void }) {
  const [value, setValue] = useState<FacetRule[] | null>(initial);
  return <EventFacetPicker schema={SCHEMA} value={value} onChange={rules => { setValue(rules); onSave(rules); }} />;
}

describe('EventFacetPicker', () => {
  it('keeps the other row when one row is cleared and refilled', async () => {
    const onSave = vi.fn();
    render(<Harness initial={[{ gender: '*', discipline: ['Slalom'] }]} onSave={onSave} />);

    await userEvent.click(screen.getByRole('button', { name: 'No genders' }));
    expect(onSave).toHaveBeenLastCalledWith([]);
    expect(screen.getByRole('button', { name: 'Slalom' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'Women' }));
    expect(onSave).toHaveBeenLastCalledWith([{ gender: ['Women'], discipline: ['Slalom'] }]);

    await userEvent.click(screen.getByRole('button', { name: 'All genders' }));
    expect(onSave).toHaveBeenLastCalledWith([{ gender: '*', discipline: ['Slalom'] }]);
  });

  it('keeps the other row when the last chip of a row is unticked', async () => {
    const onSave = vi.fn();
    render(<Harness initial={[{ gender: ['Men'] }]} onSave={onSave} />);

    await userEvent.click(screen.getByRole('button', { name: 'Men' }));
    await userEvent.click(screen.getByRole('button', { name: 'Women' }));

    expect(onSave).toHaveBeenLastCalledWith([{ gender: ['Women'], discipline: '*' }]);
  });

  it('warns before the simple choice widens exact picks', async () => {
    const onSave = vi.fn();
    render(<Harness initial={[{ gender: ['Men'], discipline: ['Downhill'] }, { gender: ['Women'], discipline: ['Slalom'] }]} onSave={onSave} />);

    await userEvent.click(screen.getByRole('button', { name: 'Back to simple choice' }));
    expect(screen.getByText(/cannot hold these exact picks/)).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Keep exact picks' }));
    expect(screen.getByRole('button', { name: 'Women Slalom' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Back to simple choice' }));
    await userEvent.click(screen.getByRole('button', { name: 'Switch anyway' }));
    expect(onSave).toHaveBeenLastCalledWith([{ gender: ['Men', 'Women'], discipline: ['Downhill', 'Slalom'] }]);
    expect(screen.getByRole('button', { name: 'Men' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('draws the shorter dimension across the grid', async () => {
    const alpine: FacetSchema = {
      id: 'fis-alpine',
      dimensions: [
        SCHEMA.dimensions[0],
        { key: 'discipline', label: 'Discipline', values: ['Downhill', 'Super G', 'Giant Slalom', 'Slalom', 'Combined', 'Parallel'] },
      ],
    };
    render(<EventFacetPicker schema={alpine} value={null} onChange={() => {}} />);
    await userEvent.click(screen.getByRole('button', { name: 'Pick exact combinations' }));

    const headers = screen.getAllByRole('columnheader').map(h => h.textContent);
    expect(headers).toEqual(['', 'Men', 'Women', 'Mixed']);
    expect(screen.getAllByRole('rowheader').map(h => h.textContent)).toEqual(alpine.dimensions[1].values);
    expect(screen.queryByTestId('facet-grid-scroll-cue')).toBeNull();
  });

  it('shows a scroll cue when the grid can still be too wide', async () => {
    const wide: FacetSchema = {
      id: 'wide',
      dimensions: [
        { key: 'a', label: 'Event', values: ['A1', 'A2', 'A3', 'A4', 'A5', 'A6'] },
        { key: 'b', label: 'Stage', values: ['B1', 'B2', 'B3', 'B4', 'B5'] },
      ],
    };
    render(<EventFacetPicker schema={wide} value={null} onChange={() => {}} />);
    await userEvent.click(screen.getByRole('button', { name: 'Pick exact combinations' }));

    expect(screen.getByTestId('facet-grid-scroll-cue')).toBeInTheDocument();
  });

  it('saves one rule from the chips', async () => {
    const onChange = vi.fn();
    render(<EventFacetPicker schema={SCHEMA} value={null} onChange={onChange} />);

    expect(screen.getByRole('button', { name: 'Men' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Men' }));

    expect(onChange).toHaveBeenCalledWith([{ gender: ['Women', 'Mixed'], discipline: '*' }]);
  });

  it('opens the grid for exact combinations and saves a cell', async () => {
    const onChange = vi.fn();
    render(<EventFacetPicker schema={SCHEMA} value={[{ gender: ['Men'] }]} onChange={onChange} />);

    await userEvent.click(screen.getByRole('button', { name: 'Pick exact combinations' }));
    expect(screen.getByRole('button', { name: 'Men Downhill' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Women Slalom' }));

    expect(onChange).toHaveBeenCalledWith([
      { gender: ['Men'], discipline: '*' },
      { gender: ['Women'], discipline: ['Slalom'] },
    ]);
  });

  it('shows rules that chips cannot hold in the grid', () => {
    render(
      <EventFacetPicker
        schema={SCHEMA}
        value={[{ gender: ['Men'], discipline: ['Downhill'] }, { gender: ['Women'], discipline: ['Slalom'] }]}
        onChange={() => {}}
      />,
    );

    expect(screen.getByRole('button', { name: 'Women Slalom' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Women Downhill' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('warns when nothing is selected', () => {
    render(<EventFacetPicker schema={SCHEMA} value={[]} onChange={() => {}} editMode />);

    expect(screen.getByText(/No events will be monitored/)).toBeInTheDocument();
  });
});
