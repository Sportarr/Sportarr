/**
 * Fallback catalog of every token a custom search query template understands.
 *
 * This mirrors SearchTemplateTokens.All in
 * src/Helpers/SearchTemplateTokens.cs, the backend's authoritative source of
 * truth. It exists as a *hardcoded* list - not derived from a successful
 * "GET /api/search/available-tokens" response - so the token picker still
 * shows every supported token when that request fails (offline, server
 * restarting, etc). Keep this list in sync with the backend catalog by hand;
 * SearchTemplateTokensTests on the backend and the tests alongside this file
 * both assert there are exactly 19 tokens.
 */
export interface SearchTemplateToken {
  token: string;
  description: string;
  example: string;
}

export const fallbackSearchTemplateTokens: SearchTemplateToken[] = [
  { token: '{League}', description: 'League name (normalized abbreviation)', example: 'NFL, UFC, Formula1' },
  { token: '{Year}', description: 'Event year (4 digits)', example: '2025' },
  { token: '{Month}', description: 'Event month (2 digits)', example: '01, 12' },
  { token: '{Day}', description: 'Event day (2 digits)', example: '01, 31' },
  { token: '{Round}', description: 'Round/race number, zero-padded (default, e.g. 01, 15)', example: '01, 15' },
  { token: '{Round:00}', description: 'Round/race number, zero-padded (same as {Round})', example: '01, 15' },
  { token: '{Round:0}', description: 'Round/race number, no padding', example: '1, 15' },
  { token: '{Week}', description: 'Week number (for team sports)', example: '1, 15' },
  { token: '{EventTitle}', description: 'Full event title (raw)', example: 'UFC 299, Super Bowl LVIII' },
  {
    token: '{EventName}',
    description:
      "Event title with trailing 'fighter1 vs fighter2' stripped (use for fighting cards where releases name the card, not the fighters)",
    example: "ONE Friday Fights 150 (from 'ONE Friday Fights 150 Kompetch vs Attachai')",
  },
  { token: '{Stage}', description: 'Stage number of a stage race, no padding; empty when the title names no stage', example: '16' },
  { token: '{Stage:00}', description: 'Stage number of a stage race, zero-padded', example: '16, 01' },
  { token: '{Stage:0}', description: 'Stage number of a stage race, no padding (same as {Stage})', example: '16' },
  { token: '{HomeTeam}', description: 'Home team name', example: 'Chiefs, Lakers' },
  { token: '{AwayTeam}', description: 'Away team name', example: 'Raiders, Celtics' },
  { token: '{vs}', description: 'Versus separator', example: 'vs' },
  { token: '{Season}', description: 'Season identifier', example: '2024-25, 2025' },
  { token: '{Part}', description: 'Part being searched (Prelims, Main Card, ...); empty for whole-event searches', example: 'Prelims, Main Card' },
  {
    token: '{EventType}',
    description: "Detected fighting event type, spaced for release-name matching; empty when the title doesn't classify",
    example: 'PPV, Fight Night, Contender Series, Weekly',
  },
];
