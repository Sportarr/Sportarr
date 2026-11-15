import { describe, expect, it } from 'vitest';
import { folderDefaultsFor } from '../folderDefaults';

describe('folderDefaultsFor', () => {
  it('gives the folder league and season for an unmatched file', () => {
    expect(folderDefaultsFor({ folderLeagueId: 2, folderSeason: '2024-2025' }))
      .toEqual({ leagueId: 2, season: '2024-2025' });
  });

  it('gives the folder league with no season when the folder names none', () => {
    expect(folderDefaultsFor({ folderLeagueId: 2, folderSeason: null })).toEqual({ leagueId: 2, season: undefined });
  });

  it('gives nothing for a file with a matched event', () => {
    expect(folderDefaultsFor({ matchedEventId: 41, folderLeagueId: 2, folderSeason: '2024-2025' })).toBeUndefined();
  });

  it('gives nothing for a file that is linked to an event', () => {
    expect(folderDefaultsFor({ existingEventId: 41, folderLeagueId: 2, folderSeason: '2024-2025' })).toBeUndefined();
  });

  it('gives nothing when the folder names no league', () => {
    expect(folderDefaultsFor({ folderLeagueId: null, folderSeason: '2024-2025' })).toBeUndefined();
  });
});
