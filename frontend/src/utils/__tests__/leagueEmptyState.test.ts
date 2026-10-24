import { describe, expect, it } from 'vitest';
import {
  describeEmptyLeague,
  findLastLeagueSync,
  isLeagueSyncActive,
  type EmptyLeagueInput,
  type LeagueSyncTask,
} from '../leagueEmptyState';

const base: EmptyLeagueInput = {
  seasonsFailed: false,
  syncActive: false,
  stillWaitingForFirstSync: false,
  waitingForTasks: false,
  storedEventCount: 0,
  followsTeams: false,
  lastSync: null,
};

const task = (id: number, leagueId: number, status: string, message: string | null): LeagueSyncTask => ({
  id,
  commandName: 'RefreshLeague',
  status,
  body: JSON.stringify({ leagueId, scope: 'full' }),
  message,
});

describe('league empty state', () => {
  it('keeps the spinner only while a sync is running', () => {
    expect(describeEmptyLeague({ ...base, syncActive: true }).kind).toBe('syncing');
  });

  it('stops the spinner once a sync finished with no events', () => {
    const view = describeEmptyLeague({
      ...base,
      lastSync: { status: 'Completed', message: 'The hub has no events for this league yet.' },
    });
    expect(view).toEqual({
      kind: 'noEvents',
      followsTeams: false,
      detail: 'The hub has no events for this league yet.',
    });
  });

  it('reports a failed sync instead of spinning', () => {
    const view = describeEmptyLeague({ ...base, lastSync: { status: 'Failed', message: 'Rate limited' } });
    expect(view).toEqual({ kind: 'syncFailed', detail: 'Rate limited' });
  });

  it('reports a season list that could not load', () => {
    expect(describeEmptyLeague({ ...base, seasonsFailed: true }).kind).toBe('loadFailed');
  });

  it('says so when stored events are all hidden by what the user follows', () => {
    const view = describeEmptyLeague({ ...base, storedEventCount: 12, followsTeams: true });
    expect(view).toEqual({ kind: 'hiddenByFilters', storedCount: 12 });
  });

  it('gives a new league a short moment to start its first sync', () => {
    expect(describeEmptyLeague({ ...base, stillWaitingForFirstSync: true }).kind).toBe('syncing');
    expect(describeEmptyLeague({ ...base, waitingForTasks: true }).kind).toBe('syncing');
  });

  it('ends the wait when no sync task shows up', () => {
    expect(describeEmptyLeague(base)).toEqual({ kind: 'noEvents', followsTeams: false, detail: null });
  });

  it('picks the newest sync task for the right league', () => {
    const tasks = [
      task(5, 2, 'Failed', 'other league'),
      task(4, 1, 'Completed', 'older'),
      task(7, 1, 'Failed', 'newest'),
      { id: 9, commandName: 'RefreshLeague', status: 'Completed', body: 'not json', message: 'bad' },
      { id: 10, commandName: 'Other', status: 'Completed', body: '{"leagueId":1}', message: 'wrong task' },
    ];
    expect(findLastLeagueSync(tasks, 1)).toEqual({ status: 'Failed', message: 'newest' });
    expect(findLastLeagueSync(tasks, 3)).toBeNull();
    expect(findLastLeagueSync(undefined, 1)).toBeNull();
  });

  it('counts only a running sync of the same league as active', () => {
    const tasks = [
      task(1, 2, 'Running', null),
      task(2, 1, 'Completed', 'done'),
      { id: 3, commandName: 'Other', status: 'Running', body: '{"leagueId":1}', message: null },
    ];
    expect(isLeagueSyncActive(tasks, 1)).toBe(false);
    expect(isLeagueSyncActive(tasks, 2)).toBe(true);
    expect(isLeagueSyncActive([task(4, 1, 'Queued', null)], 1)).toBe(true);
    expect(isLeagueSyncActive(undefined, 1)).toBe(false);
  });
});
