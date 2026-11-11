/**
 * Pairing for Match season in Library Import. Files and ticked events pair
 * by order, not by name. Equal counts pair one file to one event. A whole
 * multiple gives each event that many files in a row, as its parts.
 */

export interface BatchFile {
  filePath: string;
  fileName: string;
  /** True when the library already holds this file. */
  inLibrary?: boolean;
}

export interface BatchEvent {
  id: number;
  title: string;
  eventDate: string;
  episodeNumber?: number;
  /**
   * False when the event keeps one whole file. Multi-part episodes are off,
   * or the event is set to Full Event.
   */
  keepsParts?: boolean;
  hasFile?: boolean;
  /** The files the event holds now. */
  files?: Array<{ partName?: string | null; partNumber?: number | null }>;
}

export interface BatchPart {
  name: string;
  partNumber: number;
}

export interface BatchPair {
  file: BatchFile;
  eventId: number;
  eventTitle: string;
  partName?: string;
  partNumber?: number;
  /** Position of the file inside its event, from 0. */
  slot: number;
  /** The parts the user can pick for this file. Empty when the event has no named parts. */
  partOptions: BatchPart[];
  /** True when the event already holds a file for this part. */
  replacesFile: boolean;
  /** True when the library already holds this file. */
  alreadyImported: boolean;
}

export type BatchBlock = 'noFiles' | 'noEvents' | 'count' | 'wholeFileOnly' | 'partsUnknown' | 'noNamedParts' | 'tooFewParts';

export type BatchPairing =
  | {
    ok: true;
    filesPerEvent: number;
    pairs: BatchPair[];
    /** Events that got the same part twice. The user must fix them first. */
    duplicateEvents: number[];
  }
  | { ok: false; reason: BatchBlock; message: string };

/** Oldest event first. Events at the same time keep their round order. */
export function sortEventsOldestFirst<T extends BatchEvent>(events: T[]): T[] {
  return [...events].sort((a, b) => {
    const byDate = new Date(a.eventDate).getTime() - new Date(b.eventDate).getTime();
    if (byDate !== 0) return byDate;
    const byEpisode = (a.episodeNumber ?? 0) - (b.episodeNumber ?? 0);
    return byEpisode !== 0 ? byEpisode : a.id - b.id;
  });
}

/** Files each event gets, or null when the counts do not pair up. */
export function filesPerEvent(fileCount: number, eventCount: number): number | null {
  if (fileCount <= 0 || eventCount <= 0 || fileCount % eventCount !== 0) return null;
  return fileCount / eventCount;
}

/** The parts a file can take. Full Event is the whole file, not a part. */
export function realParts(parts: BatchPart[]): BatchPart[] {
  return parts
    .filter(part => part.partNumber > 0 && part.name.toLowerCase() !== 'full event')
    .sort((a, b) => a.partNumber - b.partNumber);
}

/**
 * The default parts for k files. A release always has the main part, so
 * the k parts that end at the main part are used. A part after it, such as
 * Post Show, is used only when the parts before it are too few. Without a
 * main part, the last k parts are used.
 */
export function defaultParts(options: BatchPart[], count: number): BatchPart[] {
  const main = options.findIndex(part => /^main\b/i.test(part.name));
  const end = main >= 0 ? main + 1 : options.length;
  const start = Math.max(0, Math.min(end - count, options.length - count));
  return options.slice(start, start + count);
}

function nameList(titles: string[]): string {
  if (titles.length <= 3) return titles.join(', ');
  return `${titles.slice(0, 3).join(', ')} and ${titles.length - 3} more`;
}

function holdsPart(event: BatchEvent, partNumber: number): boolean {
  return (event.files ?? []).some(f => f.partNumber === partNumber);
}

function holdsWholeFile(event: BatchEvent): boolean {
  if (!event.files) return event.hasFile === true;
  return event.files.some(f => !f.partNumber);
}

/**
 * Pair files to events in order.
 *
 * @param files Files in the order the user set.
 * @param events Ticked events, oldest first.
 * @param eventParts Parts of each event from the server. Needed when an
 *   event gets more than one file. With one file per event, the parts only
 *   fill the part picker.
 * @param partChoices Part numbers the user picked, by event and slot. A
 *   missing or unknown choice uses the default part. With one file per
 *   event, the default is no part, so the server reads it from the file name.
 */
export function pairFilesToEvents(
  files: BatchFile[],
  events: BatchEvent[],
  eventParts?: Map<number, BatchPart[]>,
  partChoices?: Map<number, number[]>,
): BatchPairing {
  if (files.length === 0) {
    return { ok: false, reason: 'noFiles', message: 'Select the files to match on the import page first.' };
  }
  if (events.length === 0) {
    return { ok: false, reason: 'noEvents', message: 'Tick the events these files belong to.' };
  }

  const perEvent = filesPerEvent(files.length, events.length);
  if (perEvent === null) {
    return {
      ok: false,
      reason: 'count',
      message:
        `You selected ${files.length} file${files.length === 1 ? '' : 's'} and ticked ` +
        `${events.length} event${events.length === 1 ? '' : 's'}. The file count must equal the event count, ` +
        `or be a whole multiple of it (for example ${events.length * 2} files for ${events.length} events gives each event 2 parts).`,
    };
  }

  if (perEvent === 1) {
    return {
      ok: true,
      filesPerEvent: 1,
      duplicateEvents: [],
      pairs: files.map((file, index) => {
        const event = events[index];
        const options = event.keepsParts === false ? [] : realParts(eventParts?.get(event.id) ?? []);
        const picked = options.find(option => option.partNumber === partChoices?.get(event.id)?.[0]);
        // Without a picked part, the import reads the part from the file name.
        // A replace is known only for an event that keeps one file. An event
        // keeps one file when parts are off for it, or when its parts are
        // known and it has none. Unknown parts tell nothing.
        const partsKnown = eventParts?.has(event.id) === true;
        const keepsOneFile = event.keepsParts === false || (partsKnown && options.length === 0);
        return {
          file,
          eventId: event.id,
          eventTitle: event.title,
          partName: picked?.name,
          partNumber: picked?.partNumber,
          slot: 0,
          partOptions: options,
          replacesFile: picked ? holdsPart(event, picked.partNumber) : keepsOneFile && holdsWholeFile(event),
          alreadyImported: file.inLibrary === true,
        };
      }),
    };
  }

  // An event that keeps one whole file takes one file. Several files for it
  // would compete for the same place in the library.
  const wholeOnly = events.filter(event => event.keepsParts === false);
  if (wholeOnly.length > 0) {
    return {
      ok: false,
      reason: 'wholeFileOnly',
      message:
        `Each event would get ${perEvent} files as parts, but ${nameList(wholeOnly.map(e => e.title))} ` +
        `${wholeOnly.length === 1 ? 'keeps' : 'keep'} one whole file, so the batch can give ${wholeOnly.length === 1 ? 'it' : 'them'} one file only. ` +
        'Turn on Enable Multi-Part Episodes in Settings > Media Management, or set the monitored parts of these events to more than Full Event.',
    };
  }

  if (!eventParts) {
    return { ok: false, reason: 'partsUnknown', message: 'Loading the parts of the ticked events...' };
  }

  // An event type without named parts keeps one file everywhere else in the
  // app. Numbered parts for it would be lost on the next rescan.
  const unnamed = events.filter(event => realParts(eventParts.get(event.id) ?? []).length === 0);
  if (unnamed.length > 0) {
    return {
      ok: false,
      reason: 'noNamedParts',
      message:
        `${nameList(unnamed.map(e => e.title))} ${unnamed.length === 1 ? 'keeps' : 'keep'} one file, ` +
        'because the event has no named parts. Pick one file per event.',
    };
  }

  const tooFew: string[] = [];
  const optionsFor = new Map<number, BatchPart[]>();
  for (const event of events) {
    const own = realParts(eventParts.get(event.id) ?? []);
    if (own.length < perEvent) {
      tooFew.push(`${event.title} has ${own.length} part${own.length === 1 ? '' : 's'}`);
    } else {
      optionsFor.set(event.id, own);
    }
  }
  if (tooFew.length > 0) {
    return {
      ok: false,
      reason: 'tooFewParts',
      message: `Each event would get ${perEvent} files, but not every event has that many parts. ${nameList(tooFew)}.`,
    };
  }

  const duplicateEvents: number[] = [];
  const pairs: BatchPair[] = [];
  events.forEach((event, eventIndex) => {
    const options = optionsFor.get(event.id)!;
    const defaults = defaultParts(options, perEvent);
    const chosen = defaults.map((fallback, slot) => {
      const picked = partChoices?.get(event.id)?.[slot];
      return options.find(option => option.partNumber === picked) ?? fallback;
    });
    if (new Set(chosen.map(part => part.partNumber)).size !== chosen.length) duplicateEvents.push(event.id);
    chosen.forEach((part, slot) => {
      const file = files[eventIndex * perEvent + slot];
      pairs.push({
        file,
        eventId: event.id,
        eventTitle: event.title,
        partName: part.name,
        partNumber: part.partNumber,
        slot,
        partOptions: options,
        replacesFile: holdsPart(event, part.partNumber),
        alreadyImported: file.inLibrary === true,
      });
    });
  });
  return { ok: true, filesPerEvent: perEvent, pairs, duplicateEvents };
}

/**
 * The part choices after one slot changes. The other slots keep their
 * current part. Part number 0 means no part.
 */
export function choosePart(
  choices: Map<number, number[]>,
  pairs: BatchPair[],
  eventId: number,
  slot: number,
  partNumber: number,
): Map<number, number[]> {
  const current = pairs
    .filter(pair => pair.eventId === eventId)
    .sort((a, b) => a.slot - b.slot)
    .map(pair => pair.partNumber ?? 0);
  current[slot] = partNumber;
  const next = new Map(choices);
  next.set(eventId, current);
  return next;
}
