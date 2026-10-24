// Decides what the league page says while its season list is empty.
// An empty list used to show a spinner for good, even when the sync had
// finished and the hub had nothing to give.

export interface LeagueSyncTask {
  commandName: string;
  status: string;
  body: string | null;
  message: string | null;
  id: number;
}

export interface LeagueSyncSummary {
  status: string;
  message: string | null;
}

export type EmptyLeagueView =
  | { kind: 'syncing' }
  | { kind: 'loadFailed' }
  | { kind: 'hiddenByFilters'; storedCount: number }
  | { kind: 'syncFailed'; detail: string | null }
  | { kind: 'noEvents'; followsTeams: boolean; detail: string | null };

// Finds the newest sync task for one league. The task list holds every
// league, so the task body names the league it ran for.
export function findLastLeagueSync(
  tasks: LeagueSyncTask[] | undefined,
  leagueId: number,
): LeagueSyncSummary | null {
  let newest: LeagueSyncTask | null = null;
  for (const task of tasks ?? []) {
    if (task.commandName !== 'RefreshLeague' || !task.body) continue;
    let bodyLeagueId: unknown;
    try {
      bodyLeagueId = (JSON.parse(task.body) as { leagueId?: unknown }).leagueId;
    } catch {
      continue;
    }
    if (bodyLeagueId !== leagueId) continue;
    if (!newest || task.id > newest.id) newest = task;
  }
  return newest ? { status: newest.status, message: newest.message } : null;
}

// True when a sync task for this league is running or queued. Other leagues
// can sync at the same time, so the task body names the league it runs for.
export function isLeagueSyncActive(tasks: LeagueSyncTask[] | undefined, leagueId: number): boolean {
  for (const task of tasks ?? []) {
    if (task.commandName !== 'RefreshLeague' || !task.body) continue;
    if (task.status !== 'Running' && task.status !== 'Queued') continue;
    try {
      if ((JSON.parse(task.body) as { leagueId?: unknown }).leagueId === leagueId) return true;
    } catch {
      continue;
    }
  }
  return false;
}

export interface EmptyLeagueInput {
  seasonsFailed: boolean;
  syncActive: boolean;
  // True while a league that was just added can still start its first sync.
  stillWaitingForFirstSync: boolean;
  // True while the task list has not arrived, for a short moment only.
  waitingForTasks: boolean;
  storedEventCount: number;
  followsTeams: boolean;
  lastSync: LeagueSyncSummary | null;
}

export function describeEmptyLeague(input: EmptyLeagueInput): EmptyLeagueView {
  if (input.syncActive) return { kind: 'syncing' };
  if (input.seasonsFailed) return { kind: 'loadFailed' };

  // The sync stored events, but the visibility rules hide every one of them.
  if (input.storedEventCount > 0) {
    return { kind: 'hiddenByFilters', storedCount: input.storedEventCount };
  }

  if (!input.lastSync) {
    // No sync task is known yet. Give a new league a short moment to start one.
    if (input.waitingForTasks || input.stillWaitingForFirstSync) return { kind: 'syncing' };
    return { kind: 'noEvents', followsTeams: input.followsTeams, detail: null };
  }

  if (input.lastSync.status === 'Failed') {
    return { kind: 'syncFailed', detail: input.lastSync.message };
  }

  return { kind: 'noEvents', followsTeams: input.followsTeams, detail: input.lastSync.message };
}
