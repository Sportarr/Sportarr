import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SegmentedTabs from '../SegmentedTabs';

describe('SegmentedTabs', () => {
  it('renders items and active tab style', () => {
    render(
      <SegmentedTabs
        items={[
          { key: 'queue', label: 'Queue' },
          { key: 'history', label: 'History' },
        ]}
        value="queue"
        onChange={() => {}}
      />
    );

    expect(screen.getByRole('button', { name: 'Queue' })).toHaveClass('bg-red-600');
    expect(screen.getByRole('button', { name: 'History' })).not.toHaveClass('bg-red-600');
  });

  it('calls onChange with clicked tab key', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <SegmentedTabs
        items={[
          { key: 'missing', label: 'Missing' },
          { key: 'cutoff', label: 'Cutoff' },
        ]}
        value="missing"
        onChange={onChange}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Cutoff' }));
    expect(onChange).toHaveBeenCalledWith('cutoff');
  });

  it('renders badge values', () => {
    render(
      <SegmentedTabs
        items={[
          { key: 'queue', label: 'Queue', badge: 2 },
          { key: 'history', label: 'History', badge: null },
        ]}
        value="queue"
        onChange={() => {}}
      />
    );

    expect(screen.getByText('2')).toBeInTheDocument();
  });
});
