import { describe, expect, it } from 'vitest';
import { moveItem } from '../moveItem';
import {
  choosePart,
  defaultParts,
  filesPerEvent,
  pairFilesToEvents,
  realParts,
  sortEventsOldestFirst,
  type BatchEvent,
  type BatchFile,
  type BatchPart,
} from '../seasonBatchMatch';

const files = (count: number): BatchFile[] =>
  Array.from({ length: count }, (_, i) => ({
    filePath: `/downloads/f1/${String(i + 1).padStart(2, '0')} - Kinai nagydij.mkv`,
    fileName: `${String(i + 1).padStart(2, '0')} - Kinai nagydij.mkv`,
  }));

const race = (id: number, day: number, keepsParts = true): BatchEvent => ({
  id,
  title: `Race ${id}`,
  eventDate: `2026-03-${String(day).padStart(2, '0')}T07:00:00Z`,
  keepsParts,
});

const ppvParts: BatchPart[] = [
  { name: 'Full Event', partNumber: 0 },
  { name: 'Early Prelims', partNumber: 1 },
  { name: 'Prelims', partNumber: 2 },
  { name: 'Main Card', partNumber: 3 },
];
const fightNightParts: BatchPart[] = [
  { name: 'Full Event', partNumber: 0 },
  { name: 'Prelims', partNumber: 1 },
  { name: 'Main Card', partNumber: 2 },
];

describe('season batch pairing', () => {
  it('pairs one file to one event in order', () => {
    const result = pairFilesToEvents(files(3), [race(1, 1), race(2, 8), race(3, 15)]);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.filesPerEvent).toBe(1);
    expect(result.pairs.map(p => [p.file.fileName, p.eventId, p.partName])).toEqual([
      ['01 - Kinai nagydij.mkv', 1, undefined],
      ['02 - Kinai nagydij.mkv', 2, undefined],
      ['03 - Kinai nagydij.mkv', 3, undefined],
    ]);
  });

  it('gives each fighting event its last parts by default, so the main card is always there', () => {
    const events = [race(10, 1), race(11, 8)];
    const parts = new Map([[10, ppvParts], [11, fightNightParts]]);

    const result = pairFilesToEvents(files(4), events, parts);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.filesPerEvent).toBe(2);
    expect(result.pairs.map(p => [p.eventId, p.partName, p.partNumber])).toEqual([
      [10, 'Prelims', 2],
      [10, 'Main Card', 3],
      [11, 'Prelims', 1],
      [11, 'Main Card', 2],
    ]);
    expect(result.duplicateEvents).toEqual([]);
    expect(result.pairs[0].partOptions.map(p => p.name)).toEqual(['Early Prelims', 'Prelims', 'Main Card']);
  });

  it('uses the part the user picks and keeps the other slot', () => {
    const events = [race(10, 1)];
    const parts = new Map([[10, ppvParts]]);
    const first = pairFilesToEvents(files(2), events, parts);
    if (!first.ok) throw new Error(first.message);

    const choices = choosePart(new Map(), first.pairs, 10, 0, 1);
    const result = pairFilesToEvents(files(2), events, parts, choices);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.pairs.map(p => p.partName)).toEqual(['Early Prelims', 'Main Card']);
    expect(result.duplicateEvents).toEqual([]);
  });

  it('flags an event that gets the same part twice', () => {
    const events = [race(10, 1)];
    const parts = new Map([[10, ppvParts]]);
    const first = pairFilesToEvents(files(2), events, parts);
    if (!first.ok) throw new Error(first.message);

    const result = pairFilesToEvents(files(2), events, parts, choosePart(new Map(), first.pairs, 10, 0, 3));

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.duplicateEvents).toEqual([10]);
  });

  it('flags a part the event already holds and a file the library already holds', () => {
    const held: BatchEvent = { ...race(10, 1), files: [{ partName: 'Main Card', partNumber: 3 }] };
    const batch = files(2);
    batch[0] = { ...batch[0], inLibrary: true };

    const result = pairFilesToEvents(batch, [held], new Map([[10, ppvParts]]));

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.pairs.map(p => [p.partName, p.replacesFile, p.alreadyImported])).toEqual([
      ['Prelims', false, true],
      ['Main Card', true, false],
    ]);
  });

  it('flags a whole file held by an event that keeps one file', () => {
    const events = [{ ...race(1, 1), hasFile: true }, race(2, 8)];
    const result = pairFilesToEvents(files(2), events, new Map([[1, []], [2, []]]));

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.pairs.map(p => p.replacesFile)).toEqual([true, false]);
  });

  it('claims no replace while the parts of the event are unknown', () => {
    const result = pairFilesToEvents(files(1), [{ ...race(1, 1), hasFile: true }]);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.pairs[0].replacesFile).toBe(false);
  });

  it('flags a whole file without parts when the event keeps one whole file', () => {
    const result = pairFilesToEvents(files(1), [{ ...race(1, 1, false), hasFile: true }]);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.pairs[0].replacesFile).toBe(true);
  });

  it('blocks several files for an event with no named parts', () => {
    const result = pairFilesToEvents(files(4), [race(1, 1), race(2, 8)], new Map([[1, ppvParts], [2, []]]));

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.reason).toBe('noNamedParts');
    expect(result.message).toBe('Race 2 keeps one file, because the event has no named parts. Pick one file per event.');
  });

  it('reads the part from the file name by default with one file per event', () => {
    const held: BatchEvent = { ...race(10, 1), hasFile: true, files: [{ partName: null, partNumber: null }] };
    const result = pairFilesToEvents(files(1), [held], new Map([[10, ppvParts]]));

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const [pair] = result.pairs;
    expect(pair.partName).toBeUndefined();
    expect(pair.partOptions.map(p => p.name)).toEqual(['Early Prelims', 'Prelims', 'Main Card']);
    // The part comes from the file name later, so a replace is not known here.
    expect(pair.replacesFile).toBe(false);
  });

  it('flags a picked part the event holds with one file per event', () => {
    const held: BatchEvent = { ...race(10, 1), files: [{ partName: 'Main Card', partNumber: 3 }] };
    const first = pairFilesToEvents(files(1), [held], new Map([[10, ppvParts]]));
    if (!first.ok) throw new Error(first.message);

    const result = pairFilesToEvents(files(1), [held], new Map([[10, ppvParts]]), choosePart(new Map(), first.pairs, 10, 0, 3));

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.pairs[0]).toMatchObject({ partName: 'Main Card', partNumber: 3, replacesFile: true });

    const cleared = pairFilesToEvents(files(1), [held], new Map([[10, ppvParts]]), choosePart(new Map(), result.pairs, 10, 0, 0));
    if (!cleared.ok) throw new Error(cleared.message);
    expect(cleared.pairs[0].partName).toBeUndefined();
  });

  it('blocks counts that do not pair up and names both counts', () => {
    const result = pairFilesToEvents(files(7), [race(1, 1), race(2, 8), race(3, 15)]);

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.reason).toBe('count');
    expect(result.message).toContain('7 files');
    expect(result.message).toContain('3 events');
  });

  it('blocks parts for an event that keeps one whole file', () => {
    const result = pairFilesToEvents(files(4), [race(1, 1), race(2, 8, false)], new Map());

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.reason).toBe('wholeFileOnly');
    expect(result.message).toContain('one file only');
    expect(result.message).toContain('Race 2');
  });

  it('allows one file per event when the event keeps one whole file', () => {
    expect(pairFilesToEvents(files(2), [race(1, 1, false), race(2, 8, false)]).ok).toBe(true);
  });

  it('blocks an event with fewer parts than files', () => {
    const result = pairFilesToEvents(files(3), [race(10, 1)], new Map([[10, fightNightParts]]));

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.reason).toBe('tooFewParts');
    expect(result.message).toContain('Race 10 has 2 parts');
  });

  it('waits for the parts before pairing several files per event', () => {
    const result = pairFilesToEvents(files(4), [race(1, 1), race(2, 8)]);

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.reason).toBe('partsUnknown');
  });

  it('blocks when nothing is picked', () => {
    expect(pairFilesToEvents([], [race(1, 1)])).toMatchObject({ ok: false, reason: 'noFiles' });
    expect(pairFilesToEvents(files(1), [])).toMatchObject({ ok: false, reason: 'noEvents' });
  });

  it('changes the pairing when a file moves', () => {
    const events = [race(1, 1), race(2, 8), race(3, 15)];
    const moved = moveItem(files(3), 2, 0);

    const result = pairFilesToEvents(moved, events);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.pairs.map(p => [p.file.fileName, p.eventId])).toEqual([
      ['03 - Kinai nagydij.mkv', 1],
      ['01 - Kinai nagydij.mkv', 2],
      ['02 - Kinai nagydij.mkv', 3],
    ]);
  });

  it('sorts events oldest first', () => {
    const sorted = sortEventsOldestFirst([race(3, 15), race(1, 1), race(2, 8)]);
    expect(sorted.map(e => e.id)).toEqual([1, 2, 3]);
  });

  it('works out the files per event', () => {
    expect(filesPerEvent(20, 20)).toBe(1);
    expect(filesPerEvent(20, 10)).toBe(2);
    expect(filesPerEvent(20, 3)).toBeNull();
    expect(filesPerEvent(0, 3)).toBeNull();
  });

  it('takes the last parts as the default', () => {
    expect(defaultParts(realParts(ppvParts), 2).map(p => p.name)).toEqual(['Prelims', 'Main Card']);
    expect(defaultParts(realParts(fightNightParts), 2).map(p => p.name)).toEqual(['Prelims', 'Main Card']);
  });

  it('ends the default parts at the main part when a post show follows', () => {
    const withPostShow = [...ppvParts, { name: 'Post Show', partNumber: 4 }];
    expect(defaultParts(realParts(withPostShow), 2).map(p => p.name)).toEqual(['Prelims', 'Main Card']);
    expect(defaultParts(realParts(withPostShow), 3).map(p => p.name)).toEqual(['Early Prelims', 'Prelims', 'Main Card']);
    expect(defaultParts(realParts(withPostShow), 4).map(p => p.name)).toEqual(['Early Prelims', 'Prelims', 'Main Card', 'Post Show']);
    expect(defaultParts([{ name: 'Pre-Show', partNumber: 1 }, { name: 'Main Show', partNumber: 2 }, { name: 'Post Show', partNumber: 3 }], 1)
      .map(p => p.name)).toEqual(['Main Show']);
  });

  it('drops Full Event from the part list', () => {
    expect(realParts(ppvParts).map(p => p.name)).toEqual(['Early Prelims', 'Prelims', 'Main Card']);
  });
});
