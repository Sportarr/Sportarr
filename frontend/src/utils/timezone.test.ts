import { describe, it, expect } from 'vitest';
import { localInputToUtcIso } from './timezone';

describe('localInputToUtcIso (issue #114: manual DVR recording time shift)', () => {
  it('converts a wall-clock value in a negative-offset timezone to UTC', () => {
    // 1:00 PM Eastern Standard Time (UTC-5, no DST in January) is 6:00 PM UTC.
    const result = localInputToUtcIso('2026-01-15T13:00', 'America/New_York');

    expect(new Date(result).toISOString()).toBe('2026-01-15T18:00:00.000Z');
  });

  it('rolls the UTC calendar date forward for a late local evening time', () => {
    // 8:00 PM PST (UTC-8, January - no DST) on Jan 15 is 4:00 AM UTC on Jan 16.
    // The reported bug (raw local digits stored and later re-rendered in the
    // user's timezone) produced exactly this kind of date rollover, but on
    // the wrong side - this pins the correct forward conversion.
    const result = localInputToUtcIso('2026-01-15T20:00', 'America/Los_Angeles');

    expect(new Date(result).toISOString()).toBe('2026-01-16T04:00:00.000Z');
  });

  it('converts a wall-clock value in a positive-offset timezone to UTC', () => {
    // 9:00 AM JST (UTC+9) is 12:00 AM UTC the same day.
    const result = localInputToUtcIso('2026-06-10T09:00', 'Asia/Tokyo');

    expect(new Date(result).toISOString()).toBe('2026-06-10T00:00:00.000Z');
  });

  it('accounts for daylight saving time', () => {
    // July is EDT (UTC-4), not EST (UTC-5).
    const result = localInputToUtcIso('2026-07-15T13:00', 'America/New_York');

    expect(new Date(result).toISOString()).toBe('2026-07-15T17:00:00.000Z');
  });

  it('returns an empty string for an empty input', () => {
    expect(localInputToUtcIso('', 'America/New_York')).toBe('');
  });

  it('falls back to native browser-local parsing when no timezone is configured', () => {
    const result = localInputToUtcIso('2026-01-15T13:00', undefined);

    expect(result).toBe(new Date('2026-01-15T13:00').toISOString());
  });
});
