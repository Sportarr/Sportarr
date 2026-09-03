import { describe, it, expect } from 'vitest';
import { getEventLifecycle, isTerminalStatus } from './eventStatus';

const NOW = new Date('2026-09-02T22:00:00Z');
const hoursAgo = (h: number) => new Date(NOW.getTime() - h * 60 * 60 * 1000).toISOString();

describe('getEventLifecycle', () => {
  it('treats a finished event that still says scheduled as completed', () => {
    // The metadata API had not flipped it yet, and the daily sync had not run.
    expect(getEventLifecycle({ status: 'scheduled', eventDate: hoursAgo(30), now: NOW })).toBe('completed');
  });

  it('keeps an event live for four hours after it starts', () => {
    expect(getEventLifecycle({ status: 'scheduled', eventDate: hoursAgo(1), now: NOW })).toBe('live');
    expect(getEventLifecycle({ status: 'scheduled', eventDate: hoursAgo(5), now: NOW })).toBe('completed');
  });

  it('reads the statuses the metadata API sends', () => {
    expect(getEventLifecycle({ status: 'in_progress', eventDate: hoursAgo(20), now: NOW })).toBe('live');
    expect(getEventLifecycle({ status: 'completed', eventDate: hoursAgo(20), now: NOW })).toBe('completed');
    expect(getEventLifecycle({ status: 'cancelled', eventDate: hoursAgo(20), now: NOW })).toBe('cancelled');
    expect(getEventLifecycle({ status: 'postponed', eventDate: hoursAgo(20), now: NOW })).toBe('postponed');
  });

  it('still reads the older feed spellings', () => {
    expect(getEventLifecycle({ status: 'FT', eventDate: hoursAgo(20), now: NOW })).toBe('completed');
    expect(getEventLifecycle({ status: 'NS', eventDate: hoursAgo(20), now: NOW })).toBe('completed');
    expect(getEventLifecycle({ status: 'Match Finished', eventDate: hoursAgo(20), now: NOW })).toBe('completed');
  });

  it('leaves an event that has not started alone', () => {
    expect(getEventLifecycle({ status: 'scheduled', eventDate: hoursAgo(-3), now: NOW })).toBe('upcoming');
    expect(getEventLifecycle({ status: null, eventDate: 'not a date', now: NOW })).toBe('upcoming');
  });

  it('counts a file as proof the event happened', () => {
    expect(getEventLifecycle({ status: 'scheduled', eventDate: hoursAgo(1), hasFile: true, now: NOW })).toBe('completed');
  });

  it('never calls a cancelled event completed just because a file exists', () => {
    expect(getEventLifecycle({ status: 'cancelled', eventDate: hoursAgo(1), hasFile: true, now: NOW })).toBe('cancelled');
  });
});

describe('isTerminalStatus', () => {
  it('knows which statuses mean the event will not be played again', () => {
    expect(isTerminalStatus('FT')).toBe(true);
    expect(isTerminalStatus('cancelled')).toBe(true);
    expect(isTerminalStatus('postponed')).toBe(true);
    expect(isTerminalStatus('scheduled')).toBe(false);
    expect(isTerminalStatus(null)).toBe(false);
  });
});
