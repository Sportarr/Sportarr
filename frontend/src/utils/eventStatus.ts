/**
 * One vocabulary for what an event's status means.
 *
 * The metadata API sends "scheduled" before an event and flips it to
 * "completed" on its next pass, which can be hours later. Sportarr syncs a
 * league once a day, so a finished event keeps "scheduled" until then. The
 * league page read that as "Not Started" days after the event, so the meaning
 * of a status now lives in one place and the clock fills the gap.
 */
export type EventLifecycle = 'cancelled' | 'postponed' | 'live' | 'completed' | 'upcoming';

/** An event is treated as live for this long after its start time. */
export const LIVE_WINDOW_HOURS = 4;

const CANCELLED = new Set(['CANCELLED', 'CANCELED', 'CANC', 'ABANDONED']);
const POSTPONED = new Set(['POSTPONED', 'PST']);
const FINISHED = new Set(['COMPLETED', 'FINISHED', 'FT', 'AET', 'AOT', 'AP', 'MATCH FINISHED']);
const IN_PLAY = new Set(['LIVE', 'IN_PROGRESS', 'IN PROGRESS', 'PLAYING', '1H', '2H', 'HT', 'ET', 'SUSPENDED', 'SUSP']);

const normalize = (status?: string | null) => (status ?? '').trim().toUpperCase();

/** Whether a status says the event will not be played again. */
export function isTerminalStatus(status?: string | null): boolean {
  const s = normalize(status);
  return FINISHED.has(s) || CANCELLED.has(s) || POSTPONED.has(s);
}

export function getEventLifecycle(params: {
  status?: string | null;
  eventDate: string | Date;
  hasFile?: boolean;
  now?: Date;
}): EventLifecycle {
  const status = normalize(params.status);
  if (CANCELLED.has(status)) return 'cancelled';
  if (POSTPONED.has(status)) return 'postponed';
  if (FINISHED.has(status) || params.hasFile) return 'completed';
  if (IN_PLAY.has(status)) return 'live';

  const start = params.eventDate instanceof Date ? params.eventDate : new Date(params.eventDate);
  if (Number.isNaN(start.getTime())) return 'upcoming';
  const now = params.now ?? new Date();
  if (now < start) return 'upcoming';
  return now.getTime() - start.getTime() <= LIVE_WINDOW_HOURS * 60 * 60 * 1000 ? 'live' : 'completed';
}
