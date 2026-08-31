import { formatEventDate } from '../utils/timezone';
import { useState, useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
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
import { apiGet, apiPost, apiPut } from '../utils/api';
import FileMetadataEditor, { type FileMetadataEditorValues } from './FileMetadataEditor';

interface League {
  id: number;
  name: string;
  sport: string;
  logoUrl?: string;
  monitored?: boolean;
}

interface EventOption {
  id: number;
  externalId?: string;
  title: string;
  sport: string;
  eventDate: string;
  broadcastDate?: string | null;
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
  broadcastDate?: string | null;
  league?: {
    id: number;
    name: string;
    sport: string;
  };
  // The events API returns these flat, the search API nests them under league.
  leagueId?: number;
  leagueName?: string;
  season?: string;
}

// Accepts either shape so a suggestion reads the same from both endpoints.
const leagueNameOf = (event?: Event | null): string | undefined =>
  event?.league?.name ?? event?.leagueName ?? event?.organization ?? undefined;

const leagueIdOf = (event?: Event | null): number | undefined =>
  event?.league?.id ?? event?.leagueId ?? undefined;

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
  // Null when the release name carried no date, which is why several dates of
  // the same fixture can appear together.
  parsedEventDate?: string | null;
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
  // Set when the user has just come back from adding the league this file
  // needs, so the dropdown opens on it instead of on nothing.
  initialLeagueId?: number | null;
}

// A file can belong to a league the user has not added yet, and until they do
// there is nothing in the dropdown to import it against. This sentinel sends
// them to the real add-league search and brings them back here.
const ADD_LEAGUE_OPTION = '__add_league__';

// Adding a league only queues its sync, so a league picked seconds ago still
// reads back with no events. Keep asking for about a minute before giving up.
const EVENT_SYNC_POLL_MS = 3000;
const EVENT_SYNC_MAX_ATTEMPTS = 20;

// Drop fields the user didn't fill in so the import endpoint doesn't apply
// blank string overrides over good parser values. Languages is treated
// specially: an empty array is a valid "no languages" state and gets sent.
function stripEmptyOverrides(v: FileMetadataEditorValues) {
  const out: Record<string, unknown> = {};
  const setIfNonEmpty = (key: keyof FileMetadataEditorValues) => {
    const val = v[key];
    if (val == null) return;
    if (typeof val === 'string' && val.trim() === '') return;
    out[key] = val;
  };
  setIfNonEmpty('quality');
  setIfNonEmpty('source');
  setIfNonEmpty('codec');
  setIfNonEmpty('releaseGroup');
  setIfNonEmpty('originalTitle');
  setIfNonEmpty('indexerFlags');
  setIfNonEmpty('partName');
  if (typeof v.partNumber === 'number') out.partNumber = v.partNumber;
  if (Array.isArray(v.languages) && v.languages.length > 0) out.languages = v.languages;
  return out;
}

export default function ManualImportModal({ pendingImport, onClose, onSuccess, initialLeagueId }: Props) {
  const navigate = useNavigate();
  const location = useLocation();
  const [isLoading, setIsLoading] = useState(false);

  // Selection state - hierarchical selection
  const [selectedLeagueId, setSelectedLeagueId] = useState<number | null>(
    initialLeagueId ?? pendingImport.suggestedEvent?.league?.id ?? null
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

  // Pre-import metadata editor state. Pre-filled from the parser-derived values
  // on the pending import; user can override any field before clicking Import.
  // Sent to the accept endpoint as `metadataOverrides`.
  const [editorValues, setEditorValues] = useState<FileMetadataEditorValues>({
    quality: pendingImport.quality,
    source: undefined,
    codec: undefined,
    releaseGroup: undefined,
    originalTitle: pendingImport.title,
    languages: [],
    indexerFlags: undefined,
    partName: pendingImport.suggestedPart,
    partNumber: undefined,
  });
  const [showEditor, setShowEditor] = useState(false);
  // Same choice the library importer offers. Auto reads the media management
  // settings, so hardlinks stay on for people who turned them on.
  const [importMode, setImportMode] = useState<'auto' | 'copy' | 'hardlink' | 'move'>('auto');
  // Shown beside Auto so the choice is not a guess.
  const [autoModeLabel, setAutoModeLabel] = useState('follow media management settings');

  // Data state
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await apiGet('/api/settings');
        if (!res.ok) return;
        const data = await res.json();
        const mm = data.mediaManagementSettings ? JSON.parse(data.mediaManagementSettings) : null;
        if (!cancelled && mm) {
          setAutoModeLabel(mm.useHardlinks ? 'hardlink' : mm.copyFiles ? 'copy' : 'move');
        }
      } catch {
        // Keep the generic label if settings cannot load.
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const [leagues, setLeagues] = useState<League[]>([]);
  const [seasons, setSeasons] = useState<string[]>([]);
  const [events, setEvents] = useState<EventOption[]>([]);
  const [parts, setParts] = useState<PartDefinition[]>([]);
  const [allMatches, setAllMatches] = useState<ImportSuggestion[]>([]);

  // Loading state
  const [loadingLeagues, setLoadingLeagues] = useState(true);
  const [loadingSeasons, setLoadingSeasons] = useState(false);
  const [loadingEvents, setLoadingEvents] = useState(false);
  const [eventSyncAttempts, setEventSyncAttempts] = useState(0);
  const [loadingParts, setLoadingParts] = useState(false);

  // Search state for events
  const [eventSearch, setEventSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');

  // View mode - show AI suggestions or manual selection
  const [showAISuggestions, setShowAISuggestions] = useState(true);

  // Resolved suggested event (fetched by ID when not provided inline)
  const [resolvedSuggestedEvent, setResolvedSuggestedEvent] = useState<Event | null>(null);

  // Use inline suggestedEvent if available, otherwise fall back to fetched event
  const suggestedEvent = pendingImport.suggestedEvent || resolvedSuggestedEvent;

  // Debounce search input for server-side search
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(eventSearch);
    }, 300);
    return () => clearTimeout(timer);
  }, [eventSearch]);

  // Load leagues and matches on mount
  useEffect(() => {
    loadLeagues();
    loadAllMatches();
  }, []);

  // Fetch suggested event by ID if not provided inline
  useEffect(() => {
    if (pendingImport.suggestedEventId && !pendingImport.suggestedEvent) {
      (async () => {
        try {
          const response = await apiGet(`/api/events/${pendingImport.suggestedEventId}`);
          if (response.ok) {
            const event = await response.json();
            setResolvedSuggestedEvent(event);
            const leagueId = leagueIdOf(event);
            if (leagueId && !selectedLeagueId) {
              setSelectedLeagueId(leagueId);
            }
            if (event.season && !selectedSeason) {
              setSelectedSeason(event.season);
            }
          }
        } catch {
          // Suggestion unavailable, user can still select manually
        }
      })();
    }
  }, [pendingImport.suggestedEventId]);

  useEffect(() => { setEventSyncAttempts(0); }, [selectedLeagueId]);

  // Load seasons when league changes
  useEffect(() => {
    if (selectedLeagueId) {
      loadSeasons(selectedLeagueId);
      // Only reset if user manually changed league (not initial load)
      if (!suggestedEvent?.league?.id || selectedLeagueId !== suggestedEvent.league.id) {
        setSelectedSeason(null);
        setSelectedEventId(null);
        setEvents([]);
      }
    } else {
      setSeasons([]);
      setEvents([]);
    }
  }, [selectedLeagueId]);

  // Load events when league, season, or search changes (server-side search)
  useEffect(() => {
    if (selectedLeagueId) {
      loadEvents(selectedLeagueId, selectedSeason, debouncedSearch);
    }
  }, [selectedLeagueId, selectedSeason, debouncedSearch]);

  // Clear event selection when league or season changes (but not on search)
  useEffect(() => {
    // Only reset if user manually changed (not initial load)
    if (!pendingImport.suggestedEventId) {
      setSelectedEventId(null);
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
      const response = await apiGet('/api/leagues');
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
      const response = await apiGet(`/api/library/leagues/${leagueId}/seasons`);
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

  const loadEvents = async (leagueId: number, season: string | null, search: string = '') => {
    setLoadingEvents(true);
    try {
      const params = new URLSearchParams();
      if (season) params.append('season', season);
      if (search) params.append('search', search);
      // Increase limit when searching to show more results
      if (search) params.append('limit', '200');

      const queryString = params.toString();
      const url = `/api/library/leagues/${leagueId}/events${queryString ? `?${queryString}` : ''}`;
      const response = await apiGet(url);
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
      const response = await apiGet(`/api/library/parts/${encodeURIComponent(sport)}`);
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
      const response = await apiGet(`/api/pending-imports/${pendingImport.id}/matches`);
      if (response.ok) {
        setAllMatches(await response.json());
      }
    } catch (error) {
      console.error('Failed to load matches:', error);
    }
  };

  const handlePartChange = (partName: string) => {
    const part = parts.find(p => p.name === partName);
    setSelectedPart(partName || null);
    setSelectedPartNumber(part?.partNumber ?? null);
  };

  // Pull the server's message out of a failed response, falling back to a
  // plain one when the body is not the shape we expect.
  const readError = async (response: Response, fallback: string): Promise<string> => {
    try {
      const body = await response.json();
      return body?.error ?? body?.message ?? fallback;
    } catch {
      return fallback;
    }
  };

  const handleAccept = async () => {
    if (!selectedEventId) {
      alert('Please select an event to import to');
      return;
    }

    setIsLoading(true);
    try {
      // These helpers hand back the response rather than throwing, so a failed
      // request has to be read. Without that the accept below went ahead with
      // the event the server still had recorded, importing the file under the
      // previous suggestion instead of the one just chosen, and a failed
      // accept still closed the modal as though it had worked.
      if (selectedEventId !== pendingImport.suggestedEventId || selectedPart !== pendingImport.suggestedPart) {
        const suggestionResponse = await apiPut(`/api/pending-imports/${pendingImport.id}/suggestion`, {
          eventId: selectedEventId,
          part: selectedPart
        });

        if (!suggestionResponse.ok) {
          throw new Error(await readError(suggestionResponse, "Could not change the event for this file"));
        }
      }

      // Accept the import — pass user-edited metadata overrides so the new
      // EventFile lands with the corrected Quality / ReleaseGroup / Languages
      // / etc. instead of whatever the parser guessed.
      const metadataOverrides = stripEmptyOverrides(editorValues);
      const acceptBody: Record<string, unknown> = {};
      if (Object.keys(metadataOverrides).length > 0) acceptBody.metadataOverrides = metadataOverrides;
      if (importMode !== 'auto') acceptBody.importMode = importMode;
      const acceptResponse = await apiPost(`/api/pending-imports/${pendingImport.id}/accept`, acceptBody);

      if (!acceptResponse.ok) {
        throw new Error(await readError(acceptResponse, "Import failed"));
      }

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
      const response = await apiPost(`/api/pending-imports/${pendingImport.id}/reject`, {});

      if (!response.ok) {
        throw new Error(await readError(response, "Could not reject this file"));
      }

      onSuccess();
    } catch (error: any) {
      console.error('Failed to reject:', error);
      alert(error?.message ?? 'Could not reject this file');
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

  // Events are now filtered server-side, so we use them directly
  const filteredEvents = events;

  const selectedEvent = events.find(e => e.id === selectedEventId);
  const selectedLeague = leagues.find(l => l.id === selectedLeagueId);

  const leagueHasNoEvents = !!selectedLeagueId && !loadingEvents && events.length === 0 && !eventSearch;
  // A team league added with no teams monitored syncs nothing at all, so
  // waiting for events would never end.
  const leagueSyncsNothing = leagueHasNoEvents && selectedLeague?.monitored === false;
  const waitingForEventSync =
    leagueHasNoEvents && !leagueSyncsNothing && eventSyncAttempts < EVENT_SYNC_MAX_ATTEMPTS;

  useEffect(() => {
    if (!waitingForEventSync || !selectedLeagueId) return;

    const timer = setTimeout(() => {
      setEventSyncAttempts(attempts => attempts + 1);
      loadEvents(selectedLeagueId, selectedSeason, eventSearch);
    }, EVENT_SYNC_POLL_MS);

    return () => clearTimeout(timer);
  }, [waitingForEventSync, eventSyncAttempts, selectedLeagueId, selectedSeason, eventSearch]);

  return (
    <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-4xl w-full max-h-[85dvh] overflow-y-auto">
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
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-2 text-sm">
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
          {(suggestedEvent || allMatches.length > 0) && showAISuggestions && (
            <div className={`rounded-lg p-4 border ${suggestedEvent ? getConfidenceBg(pendingImport.suggestionConfidence) : 'bg-gray-800/50 border-gray-700'}`}>
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
              {suggestedEvent && (
                <div
                  onClick={() => selectFromSuggestion({
                    eventId: pendingImport.suggestedEventId,
                    eventTitle: suggestedEvent?.title,
                    league: leagueNameOf(suggestedEvent),
                    leagueId: leagueIdOf(suggestedEvent),
                    season: suggestedEvent?.season,
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
                      <div className="text-white font-medium">
                        {suggestedEvent.title || `Event #${pendingImport.suggestedEventId}`}
                      </div>
                      <div className="text-sm text-gray-400">
                        {leagueNameOf(suggestedEvent)}
                        {suggestedEvent.season && ` • ${suggestedEvent.season}`}
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
                  {allMatches.length > 1 && !allMatches.some(m => m.parsedEventDate) && (
                    <p className="text-xs text-amber-400/90 mb-2">
                      No date was found in the file name, so the same fixture is
                      listed for every date it was played. Pick the one you want.
                    </p>
                  )}
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
                              {match.eventDate && (
                                <span className="text-gray-400 ml-2">
                                  {new Date(match.eventDate).toLocaleDateString(undefined, {
                                    year: 'numeric', month: 'short', day: 'numeric',
                                  })}
                                </span>
                              )}
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
              {!showAISuggestions && (suggestedEvent || allMatches.length > 0) && (
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
                  onChange={(e) => {
                    if (e.target.value === ADD_LEAGUE_OPTION) {
                      // Leave the resume marker on the entry behind us so the
                      // browser Back button reopens this file too, not only
                      // the page's own return button.
                      navigate(location.pathname + location.search, {
                        replace: true,
                        state: { resumeImport: { pendingImportId: pendingImport.id } },
                      });
                      navigate('/add-league/search', {
                        state: {
                          importReturn: {
                            pendingImportId: pendingImport.id,
                            fileName: pendingImport.title,
                          },
                        },
                      });
                      return;
                    }
                    setSelectedLeagueId(e.target.value ? Number(e.target.value) : null);
                  }}
                  className="w-full px-4 py-3 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                  disabled={loadingLeagues}
                >
                  <option value="">Select a league...</option>
                  {leagues.map(league => (
                    <option key={league.id} value={league.id}>
                      {league.name} ({league.sport})
                    </option>
                  ))}
                  <option value={ADD_LEAGUE_OPTION}>+ Add a new league...</option>
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
                      placeholder="Search by title, team, date, or event number..."
                      className="w-full pl-10 pr-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-red-500"
                    />
                    {loadingEvents && eventSearch && (
                      <ArrowPathIcon className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 animate-spin" />
                    )}
                  </div>
                  {eventSearch && events.length > 0 && (
                    <p className="text-xs text-gray-500 mb-2">
                      Found {events.length} events matching "{eventSearch}"
                    </p>
                  )}

                  {loadingEvents ? (
                    <div className="p-4 text-center text-gray-400">
                      <ArrowPathIcon className="w-5 h-5 animate-spin mx-auto mb-2" />
                      {eventSearch ? `Searching for "${eventSearch}"...` : 'Loading events...'}
                    </div>
                  ) : filteredEvents.length === 0 ? (
                    <div className="p-4 text-center text-gray-400 bg-gray-800 rounded-lg border border-gray-700">
                      {eventSearch ? (
                        <>
                          <p>No events found matching "{eventSearch}"</p>
                          <p className="text-xs mt-1">Try a different search term or check if the event exists in this league</p>
                        </>
                      ) : leagueSyncsNothing ? (
                        <>
                          <p>This league is not monitored, so none of its events were synced</p>
                          <p className="text-xs mt-1">
                            Monitor a team on the league page and its events appear here
                          </p>
                        </>
                      ) : waitingForEventSync ? (
                        <>
                          <p className="flex items-center justify-center gap-2">
                            <ArrowPathIcon className="w-4 h-4 animate-spin" />
                            Syncing this league's events
                          </p>
                          <p className="text-xs mt-1">They appear here as they arrive</p>
                        </>
                      ) : (
                        <>
                          <p>No events found in this league</p>
                          <p className="text-xs mt-1">Events may need to be synced first</p>
                        </>
                      )}
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
                                  {formatEventDate(event, null)}
                                  {event.season && ` • Season ${event.season}`}
                                  {event.episodeNumber && ` • Episode ${event.episodeNumber}`}
                                </p>
                              </div>
                              {event.hasFile && (
                                <span className="text-xs bg-yellow-900/50 text-yellow-400 px-2 py-0.5 rounded ml-2 whitespace-nowrap">
                                  Has File
                                  {(() => {
                                    const qualities = [...new Set(event.files.map(f => f.quality).filter(Boolean))];
                                    return qualities.length > 0 ? ` (${qualities.join(' / ')})` : '';
                                  })()}
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
          {!selectedEventId && !suggestedEvent && allMatches.length === 0 && (
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

          {/* Pre-import metadata editor.
              Lets the user override Quality, ReleaseGroup, Languages, etc. before
              committing — same fields available post-import via the Files panel
              pencil. Collapsible so it doesn't dominate the screen for users
              who trust the parser. */}
          {selectedEventId && (
            <div className="bg-gray-800/40 border border-gray-700 rounded-lg overflow-hidden">
              <button
                type="button"
                onClick={() => setShowEditor((s) => !s)}
                className="w-full px-4 py-3 flex items-center justify-between text-left hover:bg-gray-800/60"
              >
                <div className="flex items-center gap-2">
                  <FilmIcon className="w-5 h-5 text-emerald-400" />
                  <span className="text-white font-medium">File Metadata</span>
                  <span className="text-xs text-gray-400">
                    {showEditor ? 'click to collapse' : 'click to override parser values'}
                  </span>
                </div>
                <span className="text-xs text-gray-500">
                  Quality: {editorValues.quality || 'auto-detect'}
                  {editorValues.releaseGroup ? ` • Group: ${editorValues.releaseGroup}` : ''}
                </span>
              </button>
              {showEditor && (
                <div className="px-4 pb-4 pt-1">
                  <FileMetadataEditor
                    value={editorValues}
                    onChange={setEditorValues}
                    hideFields={pendingImport.suggestedPart ? [] : ['partName', 'partNumber']}
                  />
                  <p className="mt-3 text-xs text-gray-500">
                    Empty fields here keep the parser's value. Anything you fill in will be applied
                    after the import.
                  </p>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="sticky bottom-0 bg-gradient-to-br from-gray-900 to-black border-t border-gray-700 px-6 py-4 flex flex-wrap items-end justify-end gap-3">
          {/* Same choice the library importer offers, so a file that is still
              seeding can be linked instead of moved. */}
          <div className="w-full sm:mr-auto sm:w-auto">
            <label htmlFor="manual-import-mode" className="block text-xs font-medium text-gray-400 mb-1">
              Import Mode
            </label>
            <select
              id="manual-import-mode"
              value={importMode}
              onChange={(e) => setImportMode(e.target.value as 'auto' | 'copy' | 'hardlink' | 'move')}
              disabled={isLoading}
              className="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm text-white focus:border-red-600 focus:outline-none focus:ring-1 focus:ring-red-500 disabled:opacity-50"
              title="Hardlink/Copy and Copy leave the original file where it is, so a seeding torrent keeps working"
            >
              <option value="auto">Auto ({autoModeLabel})</option>
              <option value="move">Move</option>
              <option value="hardlink">Hardlink/Copy</option>
              <option value="copy">Copy</option>
            </select>
          </div>
          <div className="flex w-full flex-1 justify-end gap-3 sm:w-auto sm:flex-none">
          <button
            onClick={handleReject}
            disabled={isLoading}
            className="px-4 sm:px-6 py-2 bg-gray-700 hover:bg-gray-600 disabled:opacity-50 text-white rounded-lg transition-colors"
            title="Permanently dismiss this file — it won't appear in future scans"
          >
            Dismiss
          </button>
          <button
            onClick={onClose}
            className="px-4 sm:px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleAccept}
            disabled={isLoading || !selectedEventId}
            className="px-4 sm:px-6 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors font-medium"
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
    </div>
  );
}
