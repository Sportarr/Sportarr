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

    expect(request).toEqual({ filePath: file.filePath, eventId: 12, createNew: false });
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
