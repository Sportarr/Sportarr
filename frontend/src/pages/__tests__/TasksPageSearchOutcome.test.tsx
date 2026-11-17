import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import TasksPage from '../TasksPage';

const state = vi.hoisted(() => ({ tasks: [] as object[], compact: true }));

vi.mock('../../api/hooks', () => ({
  useTasks: () => ({ data: state.tasks, isLoading: false, error: null }),
  useQueueTask: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCancelTask: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock('../../hooks/useCompactView', () => ({ useCompactView: () => state.compact }));
vi.mock('../../utils/api', () => ({
  apiGet: vi.fn(async () => ({ ok: true, json: async () => [] })),
  apiPost: vi.fn(),
}));

function searchTask(id: number, result: string | null, message: string) {
  return {
    id, name: `Search: Event ${id}`, commandName: 'EventSearch', status: 'Completed',
    queued: '2026-11-16T10:00:00Z', started: '2026-11-16T10:00:01Z', ended: '2026-11-16T10:00:04Z',
    duration: '00:00:03', message, progress: 100, priority: 10, body: String(id), exception: null, result,
  };
}

beforeEach(() => {
  state.tasks = [
    searchTask(1, '{"grabbed":false}', 'Found 1 release, but none clearly match this event. Use Manual Search to pick one.'),
    searchTask(2, '{"grabbed":true}', 'Found 1 releases - Downloaded: Event.2'),
  ];
});

describe('tasks page search outcome', () => {
  for (const compact of [true, false]) {
    it(`marks a search that grabbed nothing in yellow (${compact ? 'table' : 'cards'})`, async () => {
      state.compact = compact;
      render(<MemoryRouter><TasksPage /></MemoryRouter>);

      const held = (await screen.findByText('Search: Event 1')).closest(compact ? 'tr' : 'div.rounded-lg') as HTMLElement;
      const label = within(held).getByText('Nothing grabbed');
      expect(label.closest('.text-yellow-400')).not.toBeNull();
      expect(within(held).queryByText('Completed')).not.toBeInTheDocument();

      const grabbed = screen.getByText('Search: Event 2').closest(compact ? 'tr' : 'div.rounded-lg') as HTMLElement;
      expect(within(grabbed).getByText('Completed').closest('.text-green-400')).not.toBeNull();
    });
  }
});
