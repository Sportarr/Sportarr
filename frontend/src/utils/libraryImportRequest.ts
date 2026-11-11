/**
 * Builds the request that Library Import sends for one file. A manual match
 * wins over the scan match. Metadata overrides from the editor win over the
 * values the scan found.
 */

export interface ImportRequestFile {
  filePath: string;
  parsedTitle?: string;
  parsedOrganization?: string;
  parsedDate?: string;
  quality?: string;
  matchedEventId?: number;
  existingEventId?: number;
}

export interface ImportRequestMapping {
  eventId?: number;
  eventTitle?: string;
  leagueId?: number;
  season?: string;
  partName?: string;
  partNumber?: number;
  createNew?: boolean;
}

export interface ImportRequestOverrides {
  quality?: string;
  source?: string;
  codec?: string;
  releaseGroup?: string;
  originalTitle?: string;
  languages?: string[];
  indexerFlags?: string;
  partName?: string | null;
  partNumber?: number | null;
}

export interface LibraryImportRequest {
  filePath: string;
  eventId?: number;
  createNew: boolean;
  eventTitle?: string;
  organization?: string;
  eventDate?: string;
  quality?: string;
  partName?: string;
  partNumber?: number;
  leagueId?: number;
  season?: string;
  source?: string;
  codec?: string;
  releaseGroup?: string;
  originalTitle?: string;
  languages?: string[];
  indexerFlags?: string;
  importMode?: string;
  overrideSourcePrecedence?: boolean;
}

function nonEmpty(s: string | null | undefined): string | undefined {
  if (s === undefined || s === null) return undefined;
  const t = s.trim();
  return t.length === 0 ? undefined : t;
}

/** Only the override values that are set. An empty value never hides another value. */
function setOverrides(overrides?: ImportRequestOverrides): Partial<LibraryImportRequest> {
  if (!overrides) return {};
  const values: Partial<LibraryImportRequest> = {
    quality: nonEmpty(overrides.quality),
    source: nonEmpty(overrides.source),
    codec: nonEmpty(overrides.codec),
    releaseGroup: nonEmpty(overrides.releaseGroup),
    originalTitle: nonEmpty(overrides.originalTitle),
    languages: overrides.languages && overrides.languages.length > 0 ? overrides.languages : undefined,
    indexerFlags: nonEmpty(overrides.indexerFlags),
  };
  return Object.fromEntries(Object.entries(values).filter(([, value]) => value !== undefined));
}

/**
 * The part name and number come from one source. A part name in the
 * overrides takes its own number, and the server fills a missing number.
 * Without an override name, the mapped part stays. With no mapped part, an
 * override number alone is sent. The server keeps that number only when
 * multi-part episodes are off. Otherwise it reads the part from the file name.
 */
export function chooseImportPart(
  overrides: ImportRequestOverrides | undefined,
  mapping: ImportRequestMapping | undefined,
): { partName?: string; partNumber?: number } {
  const overrideName = nonEmpty(overrides?.partName);
  if (overrideName) {
    return {
      partName: overrideName,
      partNumber: typeof overrides?.partNumber === 'number' ? overrides.partNumber : undefined,
    };
  }
  if (mapping?.partName || typeof mapping?.partNumber === 'number') {
    return { partName: mapping.partName, partNumber: mapping.partNumber };
  }
  return { partNumber: typeof overrides?.partNumber === 'number' ? overrides.partNumber : undefined };
}

export function buildLibraryImportRequest(
  file: ImportRequestFile,
  mapping: ImportRequestMapping | undefined,
  overrides: ImportRequestOverrides | undefined,
): LibraryImportRequest {
  const set = setOverrides(overrides);
  const part = chooseImportPart(overrides, mapping);
  const withPart = (request: LibraryImportRequest): LibraryImportRequest => {
    if (part.partName !== undefined) request.partName = part.partName;
    if (part.partNumber !== undefined) request.partNumber = part.partNumber;
    return request;
  };

  if (mapping) {
    return withPart({
      ...set,
      filePath: file.filePath,
      eventId: mapping.eventId,
      createNew: mapping.createNew ?? false,
      eventTitle: mapping.eventTitle,
      leagueId: mapping.leagueId,
      season: mapping.season,
    });
  }

  // A new match from the scan, or a re-import of a file the library holds.
  if (file.matchedEventId || file.existingEventId) {
    return withPart({
      ...set,
      filePath: file.filePath,
      eventId: file.matchedEventId || file.existingEventId,
      createNew: false,
    });
  }

  return withPart({
    ...set,
    filePath: file.filePath,
    createNew: true,
    eventTitle: file.parsedTitle,
    organization: file.parsedOrganization,
    eventDate: file.parsedDate,
    quality: set.quality ?? file.quality,
  });
}
