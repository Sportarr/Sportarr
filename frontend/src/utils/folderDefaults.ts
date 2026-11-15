// The fields of a scanned file that choose the match dialog's preselect.
export interface FolderDefaultsSource {
  matchedEventId?: number;
  existingEventId?: number;
  folderLeagueId?: number | null;
  folderSeason?: string | null;
}

// The league and season to preselect in the match dialog. A file that has a
// matched or linked event keeps that event. Its folder can belong to another
// league, so the folder must not choose for it.
export function folderDefaultsFor(file: FolderDefaultsSource): { leagueId?: number; season?: string } | undefined {
  if (file.matchedEventId || file.existingEventId || !file.folderLeagueId) return undefined;
  return { leagueId: file.folderLeagueId, season: file.folderSeason ?? undefined };
}
