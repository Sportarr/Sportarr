namespace Sportarr.Api.Models.Requests;

public record UpdateSuggestionRequest(int? EventId, string? Part);
public record SetPreferredChannelRequest(int? ChannelId);
public record BulkRenameRequest(List<int> LeagueIds);
public record PackImportScanRequest(string Path, int? LeagueId);
public record PackImportRequest(string Path, int? LeagueId, bool? DeleteUnmatched, bool? DryRun);
