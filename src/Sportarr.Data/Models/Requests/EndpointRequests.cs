namespace Sportarr.Api.Models.Requests;

public record UpdateSuggestionRequest(int? EventId, string? Part);
public record SetPreferredChannelRequest(int? ChannelId);
public record BulkRenameRequest(List<int> LeagueIds);
public record PackImportScanRequest(string Path, int? LeagueId);
public record PackImportRequest(string Path, int? LeagueId, bool? DeleteUnmatched, bool? DryRun);

// PUT /api/leagues/{id}/move
public record MoveLeagueRequest(int RootFolderId, bool MoveFiles);

// POST /api/leagues/move/bulk
public record BulkMoveLeaguesRequest(List<int> LeagueIds, int RootFolderId, bool MoveFiles);

// POST /api/leagues/{id}/reorganize
public record ReorganizeLeagueRequest(int RootFolderId);
