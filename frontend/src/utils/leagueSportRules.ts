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
export function isTeamlessSport(sport: string, leagueName: string): boolean {
  return (
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
