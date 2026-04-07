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
  const fightingSports = ['Fighting', 'MMA', 'UFC', 'Boxing', 'Kickboxing', 'Wrestling'];
  return fightingSports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
}
