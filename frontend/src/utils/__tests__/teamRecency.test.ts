import { describe, expect, it } from 'vitest';
import { isOlderGroupForcedOpen, partitionTeamsByRecency } from '../teamRecency';

const teams = [
  { idTeam: 'new', strTeam: 'Current Team' },
  { idTeam: 'old', strTeam: 'Historic Team' },
  { idTeam: 'selected-old', strTeam: 'Selected Historic Team' },
];

describe('team recency groups', () => {
  it('keeps older teams available without changing the full team list', () => {
    const groups = partitionTeamsByRecency(teams, ['new']);

    expect(groups.recent.map(team => team.idTeam)).toEqual(['new']);
    expect(groups.earlier.map(team => team.idTeam)).toEqual(['old', 'selected-old']);
    expect([...groups.recent, ...groups.earlier]).toHaveLength(teams.length);
  });

  it('shows one flat group when the server has no recency data', () => {
    const groups = partitionTeamsByRecency(teams, []);

    expect(groups.recent).toEqual(teams);
    expect(groups.earlier).toEqual([]);
  });

  it('does not create an older group when every team is recent', () => {
    const groups = partitionTeamsByRecency(teams, teams.map(team => team.idTeam));

    expect(groups.recent).toEqual(teams);
    expect(groups.earlier).toEqual([]);
  });

  it('forces older teams into view while searching or editing a selected older team', () => {
    expect(isOlderGroupForcedOpen('leicester', false, false)).toBe(true);
    expect(isOlderGroupForcedOpen('', true, false)).toBe(true);
    expect(isOlderGroupForcedOpen('', true, true)).toBe(false);
    expect(isOlderGroupForcedOpen('', false, false)).toBe(false);
  });
});
