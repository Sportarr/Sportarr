export function partitionTeamsByRecency<T extends { idTeam: string }>(teams: T[], recentTeamIds: string[]) {
  const recentIds = new Set(recentTeamIds);
  if (recentIds.size === 0) return { recent: teams, earlier: [] as T[] };

  return {
    recent: teams.filter(team => recentIds.has(team.idTeam)),
    earlier: teams.filter(team => !recentIds.has(team.idTeam)),
  };
}

export function isOlderGroupForcedOpen(searchQuery: string, earlierSelected: boolean, selectAll: boolean) {
  return !!searchQuery.trim() || (earlierSelected && !selectAll);
}
