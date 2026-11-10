// League sport classification helpers.
//
// These rules centralize the logic for "does this sport behave like a
// team-based league?" so LeagueSearchPage and LeagueDetailPage stay aligned.

/**
 * Returns true for motorsports (F1, NASCAR, WRC, ...).
 * Motorsports have no meaningful home/away team - all participants race
 * in every event, so the league is always considered monitored.
 */
export function isMotorsport(sport: string): boolean {
  const motorsports = [
    'Motorsport', 'Racing', 'Formula 1', 'F1', 'NASCAR', 'IndyCar',
    'MotoGP', 'WEC', 'Formula E', 'Rally', 'WRC', 'DTM', 'Super GT',
    'IMSA', 'V8 Supercars', 'Supercars', 'Le Mans',
  ];
  return motorsports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
}

/**
 * Returns true for golf.
 * Golf tournaments have all players competing together - no home/away teams.
 */
export function isGolf(sport: string): boolean {
  return sport.toLowerCase() === 'golf';
}

/**
 * Returns true for individual-format tennis leagues (ATP/WTA tours).
 * Individual tours have no meaningful team data - all events should sync.
 * Team-based competitions (Fed Cup, Davis Cup, Olympics) return false.
 */
export function isIndividualTennis(sport: string, leagueName: string): boolean {
  if (sport.toLowerCase() !== 'tennis') return false;
  const nameLower = leagueName.toLowerCase();
  const individualTours = ['atp', 'wta'];
  const teamBased = ['fed cup', 'davis cup', 'olympic', 'billie jean king'];
  if (teamBased.some(t => nameLower.includes(t))) return false;
  return individualTours.some(t => nameLower.includes(t));
}

/**
 * Returns true for fighting sports (UFC, Boxing, MMA, Wrestling, etc.).
 * Fighting events use multi-part structure (Early Prelims, Prelims, Main Card).
 */
export function isFightingSport(sport: string): boolean {
  // 'Combat' is the sportarr-hub canonical sport name; TheSportsDB labels
  // the same sport 'Fighting'. Both must classify so MMA leagues like
  // MVP MMA (hub sport=Combat) skip the team-based event filter that
  // would otherwise drop fight events (TSDB doesn't populate home/away).
  // Keep aligned with the backend EventPartDetector.IsFightingSport list.
  const fightingSports = ['Fighting', 'Combat', 'MMA', 'UFC', 'Boxing', 'Kickboxing', 'Muay Thai', 'Wrestling'];
  return fightingSports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
}

/**
 * Part options for a sport. Only fighting sports have multi-part episodes
 * (Early Prelims / Prelims / Main Card / Post Show); motorsports and other
 * sports have none. Order matches PartNumber on the backend
 * (EventPartDetector.CardSegment). Post Show only exists on PPV-style
 * events but is listed so it can be opted out at the league level (Fight
 * Night events ignore it because their per-event partStatuses omit it).
 */
export function getPartOptions(sport: string): string[] {
  if (isFightingSport(sport)) {
    return ['Early Prelims', 'Prelims', 'Main Card', 'Post Show'];
  }
  return [];
}

/**
 * The monitored-parts value that keeps an event as one whole file. It is
 * exclusive: it is never combined with parts. Matches
 * EventPartDetector.FullEventSegmentName on the backend.
 */
export const FULL_EVENT_PART = 'Full Event';

/**
 * The part ticks to show when Full Event is turned off. The backend keeps the
 * earlier value: "*" stands for all parts. Without a kept value, every part is
 * ticked.
 */
export function partsAfterLeavingFullEvent(
  partsBeforeFullEvent: string | null | undefined,
  availableParts: string[],
): string[] {
  if (partsBeforeFullEvent === null || partsBeforeFullEvent === undefined || partsBeforeFullEvent === '*') {
    return availableParts;
  }
  return partsBeforeFullEvent.split(',').map(p => p.trim()).filter(Boolean);
}

/**
 * The league picker's part ticks after one click. Full Event is exclusive:
 * ticking it clears the parts, and unticking it brings back the earlier part
 * choices the backend kept. Ticking a part clears Full Event.
 */
export function togglePartChoice(
  current: ReadonlySet<string>,
  part: string,
  availableParts: string[],
  partsBeforeFullEvent?: string | null,
): Set<string> {
  if (part === FULL_EVENT_PART) {
    return current.has(FULL_EVENT_PART)
      ? new Set(partsAfterLeavingFullEvent(partsBeforeFullEvent, availableParts))
      : new Set([FULL_EVENT_PART]);
  }
  const next = new Set(current);
  next.delete(FULL_EVENT_PART);
  if (next.has(part)) {
    next.delete(part);
  } else {
    next.add(part);
  }
  return next;
}

/**
 * The monitored-parts value the league picker saves. Null means all parts,
 * "" means none, otherwise the ticked parts or Full Event.
 */
export function monitoredPartsValue(selected: ReadonlySet<string>, availableParts: string[]): string | null {
  if (selected.has(FULL_EVENT_PART)) return FULL_EVENT_PART;
  if (selected.size === availableParts.length) return null;
  if (selected.size === 0) return '';
  return Array.from(selected).join(',');
}

/**
 * The file badge for an event. Hidden files are parked from the media
 * server, so the badge counts the visible files and names the hidden ones.
 */
export function fileCountLabel(files: { isParked?: boolean }[] | undefined): string {
  const all = files ?? [];
  const hidden = all.filter(f => f.isParked).length;
  const visible = all.length - hidden;
  if (hidden > 0) return `${visible} ${visible === 1 ? 'File' : 'Files'} · ${hidden} hidden`;
  return visible > 1 ? `${visible} Files` : 'Downloaded';
}

/** True when a monitored-parts value is only Full Event. */
export function isFullEventOnly(monitoredParts: string | null | undefined): boolean {
  return (monitoredParts ?? '').trim().toLowerCase() === FULL_EVENT_PART.toLowerCase();
}

/**
 * The event value that turns Full Event off for one event. Null inherits the
 * league. A league set to Full Event would keep the event whole, so the event
 * then lists the parts its own type defines. Without those names it falls back
 * to the sport's part options.
 */
export function partsValueLeavingFullEvent(
  leagueMonitoredParts: string | null | undefined,
  sport: string,
  eventPartNames?: string[] | null,
): string | null {
  if (!isFullEventOnly(leagueMonitoredParts)) return null;
  const names = eventPartNames && eventPartNames.length > 0 ? eventPartNames : getPartOptions(sport);
  return names.join(',');
}

/**
 * Returns true for individual-player racket/cue sports (Badminton, Table Tennis, Snooker).
 * Tournaments feature individual players, not teams - all events should sync without team filtering.
 */
export function isIndividualRacketOrCueSport(sport: string): boolean {
  const s = sport.toLowerCase();
  return s === 'badminton' || s === 'table tennis' || s === 'snooker';
}

/**
 * Darts matches are between individual players, not teams.
 */
export function isDarts(sport: string): boolean {
  return sport.toLowerCase() === 'darts';
}

/**
 * Climbing competitions are individual climbers, not teams.
 */
export function isClimbing(sport: string): boolean {
  return sport.toLowerCase() === 'climbing';
}

/**
 * Gambling (Poker, WSOP) events are individual players in tournaments, not teams.
 */
export function isGambling(sport: string): boolean {
  return sport.toLowerCase() === 'gambling';
}

/**
 * Returns true for sports/leagues that have no meaningful home/away team structure.
 * These leagues should auto-monitor on add (no team selection required) and must
 * stay in sync with the backend's sportsWithoutTeamFiltering list in
 * LeagueEventSyncService.cs.
 */
export function isTeamlessSport(sport: string, leagueName: string, sportFormat?: string | null): boolean {
  const format = sportFormat?.trim().toLowerCase();
  if (format === 'eventsport') return true;
  if (format === 'teamvsteam') return false;
  return (
    sport.toLowerCase() === 'athletics' ||
    isMotorsport(sport) ||
    isGolf(sport) ||
    isDarts(sport) ||
    isClimbing(sport) ||
    isGambling(sport) ||
    isIndividualRacketOrCueSport(sport) ||
    isIndividualTennis(sport, leagueName)
  );
}

/**
 * Returns true for fighting leagues that monitor by event type (PPV, Fight Night,
 * Premium Live Event, ...) instead of by team. The edit modal hides the team
 * picker for these leagues, so they must be treated as teamless when computing
 * `monitored` on save — otherwise an empty `monitoredTeamIds` would force the
 * league off and override the user's event-type selection.
 */
export function usesFightingEventTypes(sport: string, leagueName: string): boolean {
  if (!isFightingSport(sport)) return false;
  const name = leagueName.toLowerCase();
  return (
    name.includes('ufc') ||
    name.includes('ultimate fighting') ||
    name.includes('wwe') ||
    name.includes('aew') ||
    name.includes('wrestling') ||
    name === 'one' ||
    name.includes('one championship') ||
    name.includes('one fc')
  );
}
