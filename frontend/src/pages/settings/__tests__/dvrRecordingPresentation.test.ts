import { describe, expect, it } from 'vitest';
import {
  getRecordingAttentionCount,
  getRecordingCompletedCount,
  getVisibleSelectedIds,
  recordingMatchesView,
} from '../dvrRecordingPresentation';

describe('recordingMatchesView', () => {
  it('groups scheduled and active recordings for day-to-day browsing', () => {
    expect(recordingMatchesView('Scheduled', 'upcoming')).toBe(true);
    expect(recordingMatchesView('Recording', 'recording')).toBe(true);
    expect(recordingMatchesView('Completed', 'completed')).toBe(true);
    expect(recordingMatchesView('Imported', 'completed')).toBe(true);
  });

  it('puts failed and interrupted work in Needs attention', () => {
    expect(recordingMatchesView('Failed', 'attention')).toBe(true);
    expect(recordingMatchesView('Cancelled', 'attention')).toBe(true);
    expect(recordingMatchesView('Scheduled', 'attention')).toBe(false);
  });

  it('keeps every status in the all view', () => {
    expect(recordingMatchesView('Importing', 'all')).toBe(true);
  });

  it('limits bulk work to selected rows that remain visible', () => {
    expect(getVisibleSelectedIds(new Set([1, 2, 3]), [{ id: 2 }, { id: 4 }])).toEqual([2]);
  });

  it('counts failed and cancelled recordings that need attention', () => {
    expect(getRecordingAttentionCount({ failedCount: 2, cancelledCount: 3 })).toBe(5);
  });

  it('counts every recording shown in the completed view', () => {
    expect(getRecordingCompletedCount({
      completedCount: 2,
      importedCount: 3,
      importingCount: 1,
      ignoredCount: 1,
    })).toBe(7);
    expect(recordingMatchesView('Ignored', 'completed')).toBe(true);
  });
});
