import { describe, it, expect } from 'vitest';
import { fallbackSearchTemplateTokens } from './searchTemplateTokens';

/**
 * Guards the frontend fallback token list against drifting from the backend
 * catalog (SearchTemplateTokens.All in src/Helpers/SearchTemplateTokens.cs).
 * Before this existed, the UI's token picker only showed 16 of the 19 tokens
 * the query builder actually supports - {Round:00}, {Stage:0}, and {vs}
 * could never be inserted from the UI even though typing them by hand
 * worked. This fallback must be a complete, hardcoded list so the token
 * picker still works when the "GET /api/search/available-tokens" request
 * fails.
 */
describe('fallbackSearchTemplateTokens', () => {
  const expectedTokens = [
    '{League}', '{Year}', '{Month}', '{Day}', '{Round}', '{Round:00}', '{Round:0}',
    '{Week}', '{EventTitle}', '{EventName}', '{Stage}', '{Stage:00}', '{Stage:0}',
    '{HomeTeam}', '{AwayTeam}', '{vs}', '{Season}', '{Part}', '{EventType}',
  ];

  it('exports exactly the 19 expected token strings', () => {
    const tokens = fallbackSearchTemplateTokens.map((t) => t.token);
    expect(tokens.sort()).toEqual([...expectedTokens].sort());
    expect(fallbackSearchTemplateTokens).toHaveLength(19);
  });

  it('includes the three tokens the UI previously could not insert', () => {
    const tokens = fallbackSearchTemplateTokens.map((t) => t.token);
    expect(tokens).toContain('{Round:00}');
    expect(tokens).toContain('{Stage:0}');
    expect(tokens).toContain('{vs}');
  });

  it('has no duplicate tokens', () => {
    const tokens = fallbackSearchTemplateTokens.map((t) => t.token);
    expect(new Set(tokens).size).toBe(tokens.length);
  });

  it('gives every token a non-empty description', () => {
    fallbackSearchTemplateTokens.forEach((t) => {
      expect(t.description.length).toBeGreaterThan(0);
    });
  });
});
