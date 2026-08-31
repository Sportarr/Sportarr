import { describe, it, expect, beforeEach } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import type { Event } from '../../types';
import { applyCalendarMonitorChange } from '../calendarCache';

/**
 * The bug this guards: unmonitoring from the monitored-only view used to drop
 * the event from the "all events" ranges as well, so it disappeared from both
 * and the eye toggle looked like it had deleted something.
 */
const evt = (id: number, monitored: boolean) => ({ id, monitored }) as Event;

const MONITORED_ONLY = ['calendar-events', '2026-08-01', '2026-08-31', false];
const ALL_EVENTS = ['calendar-events', '2026-08-01', '2026-08-31', true];

describe('applyCalendarMonitorChange', () => {
  let qc: QueryClient;

  beforeEach(() => {
    qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    qc.setQueryData<Event[]>(MONITORED_ONLY, [evt(1, true), evt(2, true)]);
    qc.setQueryData<Event[]>(ALL_EVENTS, [evt(1, true), evt(2, true), evt(3, false)]);
  });

  it('drops an unmonitored event from monitored-only, but keeps it under all events', () => {
    applyCalendarMonitorChange(qc, 1, false);

    expect(qc.getQueryData<Event[]>(MONITORED_ONLY)?.map((e) => e.id)).toEqual([2]);

    const all = qc.getQueryData<Event[]>(ALL_EVENTS);
    expect(all?.map((e) => e.id)).toEqual([1, 2, 3]);
    expect(all?.find((e) => e.id === 1)?.monitored).toBe(false);
  });

  it('flips the flag in place when an event becomes monitored', () => {
    applyCalendarMonitorChange(qc, 3, true);

    const all = qc.getQueryData<Event[]>(ALL_EVENTS);
    expect(all?.find((e) => e.id === 3)?.monitored).toBe(true);
    expect(all?.map((e) => e.id)).toEqual([1, 2, 3]);
  });

  it('refetches monitored-only ranges when an event becomes monitored', () => {
    applyCalendarMonitorChange(qc, 3, true);

    // Patching cannot add an event to a range that never held it, so the
    // monitored-only range must be marked stale. The all-events range holds
    // the event already and is left alone.
    expect(qc.getQueryState(MONITORED_ONLY)?.isInvalidated).toBe(true);
    expect(qc.getQueryState(ALL_EVENTS)?.isInvalidated).toBe(false);
  });

  it('leaves other ranges untouched', () => {
    const OTHER = ['calendar-events', '2026-09-01', '2026-09-30', false];
    qc.setQueryData<Event[]>(OTHER, [evt(9, true)]);

    applyCalendarMonitorChange(qc, 1, false);

    expect(qc.getQueryData<Event[]>(OTHER)?.map((e) => e.id)).toEqual([9]);
  });
});
