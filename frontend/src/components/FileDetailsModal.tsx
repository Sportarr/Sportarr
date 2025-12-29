import React, { useState, useEffect } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  MagnifyingGlassIcon,
  FolderIcon,
  CalendarIcon,
  FilmIcon,
  TagIcon,
  ArrowPathIcon
} from '@heroicons/react/24/outline';

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

interface FileMapping {
  eventId?: number;
  eventTitle?: string;
  leagueId?: number;
  leagueName?: string;
  season?: string;
  partName?: string;
  partNumber?: number;
  createNew?: boolean;
}

interface FileDetailsModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave: (mapping: FileMapping) => void;
  fileName: string;
  parsedTitle?: string;
  parsedDate?: string;
  currentMapping?: FileMapping;
}

export default function FileDetailsModal({
  isOpen,
  onClose,
  onSave,
  fileName,
  parsedTitle,
  parsedDate,
  currentMapping
}: FileDetailsModalProps) {
  // Selection state
  const [selectedLeagueId, setSelectedLeagueId] = useState<number | null>(currentMapping?.leagueId ?? null);
  const [selectedSeason, setSelectedSeason] = useState<string | null>(currentMapping?.season ?? null);
  const [selectedEventId, setSelectedEventId] = useState<number | null>(currentMapping?.eventId ?? null);
  const [selectedPart, setSelectedPart] = useState<string | null>(currentMapping?.partName ?? null);
  const [selectedPartNumber, setSelectedPartNumber] = useState<number | null>(currentMapping?.partNumber ?? null);

  // Data state
  const [leagues, setLeagues] = useState<League[]>([]);
  const [seasons, setSeasons] = useState<string[]>([]);
  const [events, setEvents] = useState<EventOption[]>([]);
  const [parts, setParts] = useState<PartDefinition[]>([]);

  // Loading state
  const [loadingLeagues, setLoadingLeagues] = useState(true);
  const [loadingSeasons, setLoadingSeasons] = useState(false);
  const [loadingEvents, setLoadingEvents] = useState(false);
  const [loadingParts, setLoadingParts] = useState(false);

  // Search state for events
  const [eventSearch, setEventSearch] = useState('');

  // Load leagues on mount
  useEffect(() => {
    if (isOpen) {
      loadLeagues();
    }
  }, [isOpen]);

  // Load seasons when league changes
  useEffect(() => {
    if (selectedLeagueId) {
      loadSeasons(selectedLeagueId);
      setSelectedSeason(null);
      setSelectedEventId(null);
      setEvents([]);
    } else {
      setSeasons([]);
      setEvents([]);
    }
  }, [selectedLeagueId]);

  // Load events when league or season changes
  useEffect(() => {
    if (selectedLeagueId) {
      loadEvents(selectedLeagueId, selectedSeason);
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
      const response = await fetch('/api/leagues', {
        credentials: 'include',
      });
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
      const response = await fetch(`/api/library/leagues/${leagueId}/seasons`, {
        credentials: 'include',
      });
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
      const response = await fetch(url, {
        credentials: 'include',
      });
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
      const response = await fetch(`/api/library/parts/${encodeURIComponent(sport)}`, {
        credentials: 'include',
      });
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

  const handlePartChange = (partName: string) => {
    const part = parts.find(p => p.name === partName);
    setSelectedPart(partName);
    setSelectedPartNumber(part?.partNumber ?? null);
  };

  const handleSave = () => {
    const selectedEvent = events.find(e => e.id === selectedEventId);
    const selectedLeague = leagues.find(l => l.id === selectedLeagueId);

    const mapping: FileMapping = {
      eventId: selectedEventId ?? undefined,
      eventTitle: selectedEvent?.title,
      leagueId: selectedLeagueId ?? undefined,
      leagueName: selectedLeague?.name,
      season: selectedSeason ?? undefined,
      partName: selectedPart ?? undefined,
      partNumber: selectedPartNumber ?? undefined,
      createNew: !selectedEventId
    };

    onSave(mapping);
    onClose();
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
    <Transition show={isOpen} as={React.Fragment}>
      <Dialog onClose={onClose} className="relative z-50">
        <Transition.Child
          as={React.Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/70" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={React.Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-2xl transform overflow-hidden rounded-xl bg-gray-900 border border-gray-700 shadow-xl transition-all">
                {/* Header */}
                <div className="px-6 py-4 border-b border-gray-700 flex items-center justify-between">
                  <Dialog.Title className="text-lg font-semibold text-white">
                    Edit File Details
                  </Dialog.Title>
                  <button
                    onClick={onClose}
                    className="text-gray-400 hover:text-white transition-colors"
                  >
                    <XMarkIcon className="w-6 h-6" />
                  </button>
                </div>

                {/* Content */}
                <div className="p-6 space-y-6 max-h-[70vh] overflow-y-auto">
                  {/* File Info */}
                  <div className="p-4 bg-gray-800 rounded-lg border border-gray-700">
                    <div className="flex items-start gap-3">
                      <FilmIcon className="w-5 h-5 text-gray-400 mt-0.5" />
                      <div className="flex-1 min-w-0">
                        <p className="text-white font-medium truncate">{fileName}</p>
                        {parsedTitle && (
                          <p className="text-sm text-gray-400">
                            Parsed: {parsedTitle}
                          </p>
                        )}
                        {parsedDate && (
                          <p className="text-sm text-gray-400">
                            Date: {new Date(parsedDate).toLocaleDateString()}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>

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
                          placeholder="Search events..."
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
                          No events found in this league
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
                                } ${event.hasFile ? 'opacity-50' : ''}`}
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

                  {/* Summary */}
                  {selectedEventId && (
                    <div className="p-4 bg-green-900/20 border border-green-700 rounded-lg">
                      <p className="text-sm text-green-400 font-medium">Import Summary:</p>
                      <p className="text-sm text-white mt-1">
                        File will be imported to: <strong>{selectedEvent?.title}</strong>
                      </p>
                      {selectedLeague && (
                        <p className="text-xs text-gray-400 mt-1">
                          League: {selectedLeague.name}
                          {selectedSeason && ` • Season: ${selectedSeason}`}
                          {selectedPart && ` • Part: ${selectedPart}`}
                        </p>
                      )}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div className="px-6 py-4 border-t border-gray-700 flex justify-end gap-3">
                  <button
                    onClick={onClose}
                    className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={handleSave}
                    disabled={!selectedEventId}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Save
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
