import type { QueryClient } from '@tanstack/react-query';
import type { Event } from '../types';

/**
 * Fold a monitor change into every cached calendar range.
 *
 * The calendar caches one list per range and per switch position, keyed
 * ['calendar-events', start, end, includeUnmonitored]. Each cache has to be
 * judged by its OWN key, not by the switch the page happens to be showing.
 * Reading the page's value instead dropped the event from the "all events"
 * ranges too, so unmonitoring from a monitored-only view made it vanish from
 * both.
 *
 * An event that becomes monitored cannot be patched into a monitored-only
 * range that never held it, so those ranges are refetched instead.
 */
export function applyCalendarMonitorChange(
  queryClient: QueryClient,
  eventId: number,
  monitored: boolean,
) {
  const cached = queryClient.getQueriesData<Event[]>({ queryKey: ['calendar-events'] });

  for (const [key] of cached) {
    const includeUnmonitored = key[3] === true;
    queryClient.setQueryData<Event[]>(key, (prev) => {
      if (!prev) return prev;
      if (!monitored && !includeUnmonitored) return prev.filter((e) => e.id !== eventId);
      return prev.map((e) => (e.id === eventId ? { ...e, monitored } : e));
    });
  }

  if (monitored) {
    queryClient.invalidateQueries({
      queryKey: ['calendar-events'],
      predicate: (query) => query.queryKey[3] !== true,
    });
  }
}
