export const NAMING_CONTEXT_TEXT = 'TV libraries recognize events as episodes by season and episode number. Sports have no shared event ID in these libraries, so Sportarr provides one. Like a TVDB or IMDb ID, it identifies the exact catalog event once indexed.';

interface NamingExampleOptions {
  seasonYear?: number;
  qualityFull?: string;
  includePart?: boolean;
}

export function renderNamingExample(format: string, options: NamingExampleOptions = {}): string {
  const year = options.seasonYear ?? 2026;
  const date = `${year}-11-16`;
  const title = 'Event 100 Main Event';
  const qualityFull = options.qualityFull ?? 'WEBDL-1080p';
  const values: Record<string, string> = {
    '{Series}': 'MMA League',
    '{League}': 'MMA League',
    '{Season}': `s${year}`,
    '{Episode}': 'e12',
    '{Part}': options.includePart === false ? '' : ' - pt3',
    '{Part Name}': options.includePart === false ? '' : ' - Main Card',
    '{Event Title}': title,
    '{Event Title The}': title,
    '{Event CleanTitle}': 'event100mainevent',
    '{Event Date}': date,
    '{Air Date}': date,
    '{Event Date Year}': String(year),
    '{Air Date Year}': String(year),
    '{Event Date Month}': '11',
    '{Air Date Month}': '11',
    '{Event Date Day}': '16',
    '{Air Date Day}': '16',
    '{Quality}': '1080p',
    '{Quality Full}': qualityFull,
    '{Sportarr Id}': 'sportarr-ev-2338110',
    '{Release Group}': 'GROUP',
    '{Original Title}': 'MMA League Event 100 Main Event',
    '{Original Filename}': `MMA.League.${year}.Event.100.S${year}E12.1080p.WEB-GROUP`,
    '{Custom Formats}': 'HDR',
  };

  return `${format.replace(/\{[^}]+\}/g, token => values[token] ?? token)}.mkv`;
}

export function getNamingWarning(format: string, renameEvents: boolean): string | null {
  if (!renameEvents) {
    return 'Renaming is off. Matching depends on the source filename. Enable renaming with {Season}{Episode} and {Sportarr Id} for reliable TV-library matching.';
  }

  if (format.includes('{Original Filename}')) {
    return 'Original release names vary. Some lack episode numbers or a Sportarr ID. Use a standard preset for reliable matching.';
  }

  const hasEpisodeNumbers = format.includes('{Season}{Episode}');
  const hasEventId = format.includes('{Sportarr Id}');

  if (!hasEpisodeNumbers && !hasEventId) {
    return 'This format omits TV-style episode numbers and the Sportarr ID. Media servers may miss or misidentify events. Add {Season}{Episode} and {Sportarr Id}.';
  }

  if (!hasEpisodeNumbers) {
    return 'The ID identifies the event, but an ID alone may not let a TV library index the file as an episode. Add {Season}{Episode}.';
  }

  if (!hasEventId) {
    return 'Episode numbers can change as events are added. Add {Sportarr Id} for an exact event lookup.';
  }

  return null;
}
