import { useState, useEffect } from 'react';
import {
  XMarkIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  MagnifyingGlassIcon,
  FolderIcon,
  CalendarIcon,
  FilmIcon,
  TagIcon,
  ArrowPathIcon,
  InformationCircleIcon
} from '@heroicons/react/24/outline';
import apiClient from '../api/client';

interface League {
  id: number;
  name: string;
  sport: string;
  logoUrl?: string;
}

interface EventOption {
  id: number;
  externalId?: string;
  title: string;
  sport: string;
  eventDate: string;
  season?: string;
  seasonNumber?: number;
  episodeNumber?: number;
  venue?: string;
  leagueName?: string;
  homeTeam?: string;
  awayTeam?: string;
  hasFile: boolean;
  usesMultiPart: boolean;
  files: Array<{
    id: number;
    partName?: string;
    partNumber?: number;
    quality?: string;
  }>;
}

interface PartDefinition {
  name: string;
  partNumber: number;
}

interface Event {
  id: number;
  title: string;
  organization?: string;
  eventDate: string;
  league?: {
    id: number;
    name: string;
    sport: string;
  };
  season?: string;
}

interface ImportSuggestion {
  eventId?: number;
  eventTitle?: string;
  league?: string;
  leagueId?: number;
  season?: string;
  eventDate?: string;
  quality?: string;
  qualityScore?: number;
  part?: string;
  confidence: number;
}

interface PendingImport {
  id: number;
  title: string;
  filePath: string;
  size: number;
  quality?: string;
  qualityScore: number;
  suggestedEventId?: number;
  suggestedEvent?: Event;
  suggestedPart?: string;
  suggestionConfidence: number;
  detected: string;
  protocol?: string;
}

interface Props {
  pendingImport: PendingImport;
  onClose: () => void;
  onSuccess: () => void;
}

export default function ManualImportModal({ pendingImport, onClose, onSuccess }: Props) {
  const [isLoading, setIsLoading] = useState(false);

  // Selection state - hierarchical selection
  const [selectedLeagueId, setSelectedLeagueId] = useState<number | null>(
    pendingImport.suggestedEvent?.league?.id || null
  );
  const [selectedSeason, setSelectedSeason] = useState<string | null>(
    pendingImport.suggestedEvent?.season || null
  );
  const [selectedEventId, setSelectedEventId] = useState<number | null>(
    pendingImport.suggestedEventId || null
  );
  const [selectedPart, setSelectedPart] = useState<string | null>(
    pendingImport.suggestedPart || null
  );
  const [selectedPartNumber, setSelectedPartNumber] = useState<number | null>(null);

  // Data state
  const [leagues, setLeagues] = useState<League[]>([]);
  const [seasons, setSeasons] = useState<string[]>([]);
  const [events, setEvents] = useState<EventOption[]>([]);
  const [parts, setParts] = useState<PartDefinition[]>([]);
  const [allMatches, setAllMatches] = useState<ImportSuggestion[]>([]);

  // Loading state
  const [loadingLeagues, setLoadingLeagues] = useState(true);
  const [loadingSeasons, setLoadingSeasons] = useState(false);
  const [loadingEvents, setLoadingEvents] = useState(false);
  const [loadingParts, setLoadingParts] = useState(false);

  // Search state for events
  const [eventSearch, setEventSearch] = useState('');

  // View mode - show AI suggestions or manual selection
  const [showAISuggestions, setShowAISuggestions] = useState(true);

  // Load leagues and matches on mount
  useEffect(() => {
    loadLeagues();
    loadAllMatches();
  }, []);

  // Load seasons when league changes
  useEffect(() => {
    if (selectedLeagueId) {
      loadSeasons(selectedLeagueId);
      // Only reset if user manually changed league (not initial load)
      if (!pendingImport.suggestedEvent?.league?.id || selectedLeagueId !== pendingImport.suggestedEvent.league.id) {
        setSelectedSeason(null);
        setSelectedEventId(null);
        setEvents([]);
      }
    } else {
      setSeasons([]);
      setEvents([]);
    }
  }, [selectedLeagueId]);

  // Load events when league or season changes
  useEffect(() => {
    if (selectedLeagueId) {
      loadEvents(selectedLeagueId, selectedSeason);
      // Only reset if user manually changed (not initial load)
      if (!pendingImport.suggestedEventId) {
        setSelectedEventId(null);
      }
    }
  }, [selectedLeagueId, selectedSeason]);

  // Load parts when event changes
  useEffect(() => {
    if (selectedEventId) {
      const event = events.find(e => e.id === selectedEventId);
      if (event?.usesMultiPart) {
        loadParts(event.sport);
      } else {
        setParts([]);
        setSelectedPart(null);
        setSelectedPartNumber(null);
      }
    } else {
      setParts([]);
    }
  }, [selectedEventId, events]);

  const loadLeagues = async () => {
    setLoadingLeagues(true);
    try {
      const response = await fetch('/api/leagues');
      if (response.ok) {
        const data = await response.json();
        setLeagues(data);
      }
    } catch (err) {
      console.error('Failed to load leagues:', err);
    } finally {
      setLoadingLeagues(false);
    }
  };

  const loadSeasons = async (leagueId: number) => {
    setLoadingSeasons(true);
    try {
      const response = await fetch(`/api/library/leagues/${leagueId}/seasons`);
      if (response.ok) {
        const data = await response.json();
        setSeasons(data.seasons || []);
      }
    } catch (err) {
      console.error('Failed to load seasons:', err);
    } finally {
      setLoadingSeasons(false);
    }
  };

  const loadEvents = async (leagueId: number, season: string | null) => {
    setLoadingEvents(true);
    try {
      const url = season
        ? `/api/library/leagues/${leagueId}/events?season=${encodeURIComponent(season)}`
        : `/api/library/leagues/${leagueId}/events`;
      const response = await fetch(url);
      if (response.ok) {
        const data = await response.json();
        setEvents(data.events || []);
      }
    } catch (err) {
      console.error('Failed to load events:', err);
    } finally {
      setLoadingEvents(false);
    }
  };

  const loadParts = async (sport: string) => {
    setLoadingParts(true);
    try {
      const response = await fetch(`/api/library/parts/${encodeURIComponent(sport)}`);
      if (response.ok) {
        const data = await response.json();
        setParts(data.parts || []);
      }
    } catch (err) {
      console.error('Failed to load parts:', err);
    } finally {
      setLoadingParts(false);
    }
  };

  const loadAllMatches = async () => {
    try {
      const response = await apiClient.get(`/pending-imports/${pendingImport.id}/matches`);
      setAllMatches(response.data);
    } catch (error) {
      console.error('Failed to load matches:', error);
    }
  };

  const handlePartChange = (partName: string) => {
    const part = parts.find(p => p.name === partName);
    setSelectedPart(partName || null);
    setSelectedPartNumber(part?.partNumber ?? null);
  };

  const handleAccept = async () => {
    if (!selectedEventId) {
      alert('Please select an event to import to');
      return;
    }

    setIsLoading(true);
    try {
      // Update suggestion if user changed it
      if (selectedEventId !== pendingImport.suggestedEventId || selectedPart !== pendingImport.suggestedPart) {
        await apiClient.put(`/pending-imports/${pendingImport.id}/suggestion`, {
          eventId: selectedEventId,
          part: selectedPart
        });
      }

      // Accept the import
      await apiClient.post(`/pending-imports/${pendingImport.id}/accept`);
      onSuccess();
    } catch (error: any) {
      console.error('Failed to import:', error);
      alert(`Import failed: ${error.response?.data?.error || error.message}`);
    } finally {
      setIsLoading(false);
    }
  };

  const handleReject = async () => {
    setIsLoading(true);
    try {
      await apiClient.post(`/pending-imports/${pendingImport.id}/reject`);
      onSuccess();
    } catch (error) {
      console.error('Failed to reject:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const selectFromSuggestion = (match: ImportSuggestion) => {
    if (match.eventId) {
      setSelectedEventId(match.eventId);
      setSelectedPart(match.part || null);
      // Try to set the league if available
      if (match.leagueId) {
        setSelectedLeagueId(match.leagueId);
      }
      if (match.season) {
        setSelectedSeason(match.season);
      }
      setShowAISuggestions(false);
    }
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString();
  };

  const getConfidenceColor = (confidence: number) => {
    if (confidence >= 80) return 'text-green-400';
    if (confidence >= 60) return 'text-yellow-400';
    return 'text-orange-400';
  };

  const getConfidenceBg = (confidence: number) => {
    if (confidence >= 80) return 'bg-green-900/30 border-green-700';
    if (confidence >= 60) return 'bg-yellow-900/30 border-yellow-700';
    return 'bg-orange-900/30 border-orange-700';
  };

  const filteredEvents = events.filter(e => {
    if (!eventSearch) return true;
    const search = eventSearch.toLowerCase();
    return (
      e.title.toLowerCase().includes(search) ||
      e.homeTeam?.toLowerCase().includes(search) ||
      e.awayTeam?.toLowerCase().includes(search)
    );
  });

  const selectedEvent = events.find(e => e.id === selectedEventId);
  const selectedLeague = leagues.find(l => l.id === selectedLeagueId);

  return (
    <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-4xl w-full max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="sticky top-0 bg-gradient-to-br from-gray-900 to-black border-b border-gray-700 px-6 py-4 flex items-center justify-between z-10">
          <div>
            <h3 className="text-xl font-bold text-white">Manual Import</h3>
            <p className="text-sm text-gray-400 mt-1">Map download to an event in your library</p>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-white transition-colors"
          >
            <XMarkIcon className="w-6 h-6" />
          </button>
        </div>

        <div className="p-6 space-y-6">
          {/* Download Info */}
          <div className="bg-gray-800/50 rounded-lg p-4 border border-gray-700">
            <h4 className="text-white font-medium mb-3 flex items-center gap-2">
              <InformationCircleIcon className="w-5 h-5 text-blue-400" />
              Download Information
            </h4>
            <div className="grid grid-cols-2 gap-x-8 gap-y-2 text-sm">
              <div className="flex justify-between col-span-2">
                <span className="text-gray-400">Title:</span>
                <span className="text-white max-w-lg truncate text-right" title={pendingImport.title}>{pendingImport.title}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Size:</span>
                <span className="text-white">{formatBytes(pendingImport.size)}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Detected:</span>
                <span className="text-white">{formatDate(pendingImport.detected)}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Quality:</span>
                <span className="px-2 py-0.5 bg-purple-900/30 text-purple-400 text-xs rounded">
                  {pendingImport.quality || 'Unknown'}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Protocol:</span>
                <span className="px-2 py-0.5 bg-blue-900/30 text-blue-400 text-xs rounded uppercase">
                  {pendingImport.protocol || 'Unknown'}
                </span>
              </div>
            </div>
          </div>

          {/* AI Suggestions Section */}
          {(pendingImport.suggestedEvent || allMatches.length > 0) && showAISuggestions && (
            <div className={`rounded-lg p-4 border ${pendingImport.suggestedEvent ? getConfidenceBg(pendingImport.suggestionConfidence) : 'bg-gray-800/50 border-gray-700'}`}>
              <div className="flex items-start justify-between mb-3">
                <div className="flex items-center gap-2">
                  <CheckCircleIcon className="w-5 h-5 text-green-400" />
                  <h4 className="text-white font-medium">AI Suggested Matches</h4>
                </div>
                <button
                  onClick={() => setShowAISuggestions(false)}
                  className="text-xs text-gray-400 hover:text-white"
                >
                  Hide suggestions
                </button>
              </div>

              {/* Top suggestion */}
              {pendingImport.suggestedEvent && (
                <div
                  onClick={() => selectFromSuggestion({
                    eventId: pendingImport.suggestedEventId,
                    eventTitle: pendingImport.suggestedEvent?.title,
                    league: pendingImport.suggestedEvent?.league?.name,
                    leagueId: pendingImport.suggestedEvent?.league?.id,
                    season: pendingImport.suggestedEvent?.season,
                    part: pendingImport.suggestedPart,
                    confidence: pendingImport.suggestionConfidence
                  })}
                  className={`p-3 rounded-lg border cursor-pointer transition-colors mb-2 ${
                    selectedEventId === pendingImport.suggestedEventId
                      ? 'bg-red-900/30 border-red-500'
                      : 'bg-gray-800/50 border-gray-600 hover:border-gray-500'
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <div>
                      <div className="text-white font-medium">{pendingImport.suggestedEvent.title}</div>
                      <div className="text-sm text-gray-400">
                        {pendingImport.suggestedEvent.league?.name || pendingImport.suggestedEvent.organization}
                        {pendingImport.suggestedEvent.season && ` • ${pendingImport.suggestedEvent.season}`}
                      </div>
                      {pendingImport.suggestedPart && (
                        <div className="text-sm text-blue-400 mt-1">
                          Part: {pendingImport.suggestedPart}
                        </div>
                      )}
                    </div>
                    <div className="text-right">
                      <span className={`text-sm font-bold ${getConfidenceColor(pendingImport.suggestionConfidence)}`}>
                        {pendingImport.suggestionConfidence}%
                      </span>
                      <div className="text-xs text-gray-500">confidence</div>
                    </div>
                  </div>
                </div>
              )}

              {/* Other matches */}
              {allMatches.length > 0 && (
                <div className="mt-3">
                  <p className="text-xs text-gray-400 mb-2">Other potential matches:</p>
                  <div className="max-h-32 overflow-y-auto space-y-1">
                    {allMatches
                      .filter(m => m.eventId !== pendingImport.suggestedEventId)
                      .slice(0, 5)
                      .map((match, index) => (
                        <div
                          key={index}
                          onClick={() => selectFromSuggestion(match)}
                          className={`p-2 rounded border cursor-pointer transition-colors text-sm ${
                            selectedEventId === match.eventId
                              ? 'bg-red-900/30 border-red-500'
                              : 'bg-gray-800/30 border-gray-700 hover:border-gray-600'
                          }`}
                        >
                          <div className="flex items-center justify-between">
                            <div className="truncate">
                              <span className="text-white">{match.eventTitle}</span>
                              <span className="text-gray-500 ml-2">{match.league}</span>
                            </div>
                            <span className={`text-xs font-medium ${getConfidenceColor(match.confidence)}`}>
                              {match.confidence}%
                            </span>
                          </div>
                        </div>
                      ))}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Manual Selection */}
          <div className="bg-gray-800/50 rounded-lg p-4 border border-gray-700">
            <div className="flex items-center justify-between mb-4">
              <h4 className="text-white font-medium flex items-center gap-2">
                <MagnifyingGlassIcon className="w-5 h-5 text-gray-400" />
                {showAISuggestions ? 'Or Select Manually' : 'Select Event'}
              </h4>
              {!showAISuggestions && (pendingImport.suggestedEvent || allMatches.length > 0) && (
                <button
                  onClick={() => setShowAISuggestions(true)}
                  className="text-xs text-blue-400 hover:text-blue-300"
                >
                  Show AI suggestions
                </button>
              )}
            </div>

            <div className="space-y-4">
              {/* League Selection */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                  <FolderIcon className="w-4 h-4" />
                  League
                </label>
                <select
                  value={selectedLeagueId ?? ''}
                  onChange={(e) => setSelectedLeagueId(e.target.value ? Number(e.target.value) : null)}
                  className="w-full px-4 py-3 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                  disabled={loadingLeagues}
                >
                  <option value="">Select a league...</option>
                  {leagues.map(league => (
                    <option key={league.id} value={league.id}>
                      {league.name} ({league.sport})
                    </option>
                  ))}
                </select>
                {loadingLeagues && (
                  <p className="text-sm text-gray-500 mt-1 flex items-center gap-1">
                    <ArrowPathIcon className="w-3 h-3 animate-spin" /> Loading leagues...
                  </p>
                )}
              </div>

              {/* Season Selection */}
              {selectedLeagueId && (
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                    <CalendarIcon className="w-4 h-4" />
                    Season
                  </label>
                  <select
                    value={selectedSeason ?? ''}
                    onChange={(e) => setSelectedSeason(e.target.value || null)}
                    className="w-full px-4 py-3 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                    disabled={loadingSeasons}
                  >
                    <option value="">All seasons</option>
                    {seasons.map(season => (
                      <option key={season} value={season}>
                        {season}
                      </option>
                    ))}
                  </select>
                  {loadingSeasons && (
                    <p className="text-sm text-gray-500 mt-1 flex items-center gap-1">
                      <ArrowPathIcon className="w-3 h-3 animate-spin" /> Loading seasons...
                    </p>
                  )}
                </div>
              )}

              {/* Event Selection */}
              {selectedLeagueId && (
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                    <FilmIcon className="w-4 h-4" />
                    Event
                  </label>

                  {/* Event search */}
                  <div className="relative mb-2">
                    <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                    <input
                      type="text"
                      value={eventSearch}
                      onChange={(e) => setEventSearch(e.target.value)}
                      placeholder="Search events by title or team..."
                      className="w-full pl-10 pr-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-red-500"
                    />
                  </div>

                  {loadingEvents ? (
                    <div className="p-4 text-center text-gray-400">
                      <ArrowPathIcon className="w-5 h-5 animate-spin mx-auto mb-2" />
                      Loading events...
                    </div>
                  ) : filteredEvents.length === 0 ? (
                    <div className="p-4 text-center text-gray-400 bg-gray-800 rounded-lg border border-gray-700">
                      {events.length === 0 ? 'No events found in this league' : 'No events match your search'}
                    </div>
                  ) : (
                    <div className="max-h-48 overflow-y-auto border border-gray-700 rounded-lg">
                      {filteredEvents.map(event => {
                        const isSelected = selectedEventId === event.id;
                        return (
                          <div
                            key={event.id}
                            onClick={() => setSelectedEventId(event.id)}
                            className={`p-3 cursor-pointer transition-colors ${
                              isSelected
                                ? 'bg-red-900/30 border-l-4 border-l-red-500'
                                : 'bg-gray-800 hover:bg-gray-700 border-l-4 border-l-transparent'
                            } ${event.hasFile ? 'opacity-60' : ''}`}
                          >
                            <div className="flex items-center justify-between">
                              <div className="flex-1 min-w-0">
                                <p className={`font-medium truncate ${isSelected ? 'text-white' : 'text-gray-200'}`}>
                                  {event.title}
                                </p>
                                <p className="text-xs text-gray-400 mt-0.5">
                                  {new Date(event.eventDate).toLocaleDateString()}
                                  {event.season && ` • Season ${event.season}`}
                                  {event.episodeNumber && ` • Episode ${event.episodeNumber}`}
                                </p>
                              </div>
                              {event.hasFile && (
                                <span className="text-xs bg-yellow-900/50 text-yellow-400 px-2 py-0.5 rounded ml-2">
                                  Has File
                                </span>
                              )}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}

              {/* Part Selection (for multi-part events) */}
              {selectedEvent?.usesMultiPart && parts.length > 0 && (
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                    <TagIcon className="w-4 h-4" />
                    Part / Session
                  </label>
                  <select
                    value={selectedPart ?? ''}
                    onChange={(e) => handlePartChange(e.target.value)}
                    className="w-full px-4 py-3 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                    disabled={loadingParts}
                  >
                    <option value="">Auto-detect from filename</option>
                    {parts.map(part => {
                      // Check if this part already has a file
                      const existingFile = selectedEvent.files.find(f => f.partNumber === part.partNumber);
                      return (
                        <option key={part.name} value={part.name}>
                          {part.name} (pt{part.partNumber})
                          {existingFile && ' - Has File'}
                        </option>
                      );
                    })}
                  </select>
                  {loadingParts && (
                    <p className="text-sm text-gray-500 mt-1 flex items-center gap-1">
                      <ArrowPathIcon className="w-3 h-3 animate-spin" /> Loading parts...
                    </p>
                  )}
                  <p className="text-xs text-gray-500 mt-2">
                    Select the specific part/session this file represents, or leave as auto-detect
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Import Summary */}
          {selectedEventId && selectedEvent && (
            <div className="p-4 bg-green-900/20 border border-green-700 rounded-lg">
              <p className="text-sm text-green-400 font-medium mb-1">Ready to Import:</p>
              <p className="text-white">
                <strong>{selectedEvent.title}</strong>
              </p>
              <p className="text-sm text-gray-400 mt-1">
                {selectedLeague?.name}
                {selectedSeason && ` • ${selectedSeason}`}
                {selectedPart && ` • ${selectedPart}`}
              </p>
            </div>
          )}

          {/* No Selection Warning */}
          {!selectedEventId && !pendingImport.suggestedEvent && allMatches.length === 0 && (
            <div className="bg-yellow-900/20 border border-yellow-700/50 rounded-lg p-4">
              <div className="flex items-center gap-2">
                <ExclamationTriangleIcon className="w-5 h-5 text-yellow-400" />
                <p className="text-yellow-400 text-sm">
                  No matching events found automatically. Please select a league and event manually,
                  or ensure the event exists in your library.
                </p>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="sticky bottom-0 bg-gradient-to-br from-gray-900 to-black border-t border-gray-700 px-6 py-4 flex justify-end gap-3">
          <button
            onClick={handleReject}
            disabled={isLoading}
            className="px-6 py-2 bg-gray-700 hover:bg-gray-600 disabled:opacity-50 text-white rounded-lg transition-colors"
          >
            Reject
          </button>
          <button
            onClick={onClose}
            className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleAccept}
            disabled={isLoading || !selectedEventId}
            className="px-6 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors font-medium"
          >
            {isLoading ? (
              <span className="flex items-center gap-2">
                <ArrowPathIcon className="w-4 h-4 animate-spin" />
                Importing...
              </span>
            ) : (
              'Import'
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
