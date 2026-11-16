import { describe, expect, it } from 'vitest';
import { buildLibraryImportRequest, chooseImportPart } from '../libraryImportRequest';

const file = { filePath: '/in/03 - Kinai nagydij.mkv', parsedTitle: 'Kinai nagydij', quality: 'WEBDL-1080p' };
const mapping = { eventId: 7, eventTitle: 'UFC 310', leagueId: 4, season: '2024', partName: 'Main Card', partNumber: 3, createNew: false };

describe('library import request', () => {
  it('keeps the mapped part when the overrides have no part', () => {
    const request = buildLibraryImportRequest(file, mapping, { codec: 'x265' });

    expect(request).toMatchObject({ eventId: 7, partName: 'Main Card', partNumber: 3, codec: 'x265' });
  });

  it('keeps the mapped part when the override part name is empty', () => {
    const request = buildLibraryImportRequest(file, mapping, { partName: '  ', partNumber: 9 });

    expect(request.partName).toBe('Main Card');
    expect(request.partNumber).toBe(3);
  });

  it('takes name and number together from the override', () => {
    expect(chooseImportPart({ partName: 'Prelims', partNumber: 2 }, mapping)).toEqual({ partName: 'Prelims', partNumber: 2 });
  });

  it('leaves the number to the server when the override names a part without one', () => {
    const request = buildLibraryImportRequest(file, mapping, { partName: 'Prelims', partNumber: null });

    expect(request.partName).toBe('Prelims');
    expect(request.partNumber).toBeUndefined();
  });

  it('keeps the scanned quality for a new event when the override has none', () => {
    const request = buildLibraryImportRequest(file, undefined, { codec: 'x265' });

    expect(request).toMatchObject({ createNew: true, quality: 'WEBDL-1080p', codec: 'x265', eventTitle: 'Kinai nagydij' });
  });

  it('uses the override quality for a new event', () => {
    expect(buildLibraryImportRequest(file, undefined, { quality: 'Bluray-1080p' }).quality).toBe('Bluray-1080p');
  });

  it('sends the scan match when there is no manual match', () => {
    const request = buildLibraryImportRequest({ ...file, matchedEventId: 12 }, undefined, undefined);

    expect(request).toEqual({ filePath: file.filePath, eventId: 12, suggestedEventId: 12, createNew: false });
  });

  // The server compares the two ids to tell a kept match from a user's pick.
  it('sends the scan suggestion beside a manual match', () => {
    const request = buildLibraryImportRequest({ ...file, matchedEventId: 12 }, mapping, undefined);

    expect(request).toMatchObject({ eventId: 7, suggestedEventId: 12 });
  });

  // The server does not report a kept memory answer back to the hub.
  it('marks a suggestion that came from match memory', () => {
    const memory = { ...file, matchedEventId: 12, matchedByMemory: true };

    expect(buildLibraryImportRequest(memory, undefined, undefined).suggestedByMemory).toBe(true);
    expect(buildLibraryImportRequest(memory, mapping, undefined).suggestedByMemory).toBe(true);
    expect(buildLibraryImportRequest({ ...file, matchedEventId: 12 }, undefined, undefined).suggestedByMemory).toBeUndefined();
  });

  // Match season pairs files with events by order. The user did not pick them.
  it('marks a pair that Match season made', () => {
    const paired = { ...mapping, pairedBySeasonMatch: true };

    expect(buildLibraryImportRequest({ ...file, matchedEventId: 12 }, paired, undefined))
      .toMatchObject({ eventId: 7, suggestedEventId: 12, pairedBySeasonMatch: true });
    expect(buildLibraryImportRequest(file, mapping, undefined).pairedBySeasonMatch).toBeUndefined();
  });

  it('sends no suggestion when the scan matched nothing', () => {
    expect(buildLibraryImportRequest(file, mapping, undefined).suggestedEventId).toBeUndefined();
  });

  // The server keeps a number alone only when multi-part episodes are off.
  it('sends an override part number alone when there is no mapped part', () => {
    const request = buildLibraryImportRequest({ ...file, matchedEventId: 12 }, undefined, { partNumber: 2 });

    expect(request.partName).toBeUndefined();
    expect(request.partNumber).toBe(2);
  });

  it('keeps the mapped part over an override part number alone', () => {
    const request = buildLibraryImportRequest(file, mapping, { partNumber: 2 });

    expect(request.partName).toBe('Main Card');
    expect(request.partNumber).toBe(3);
  });
});
