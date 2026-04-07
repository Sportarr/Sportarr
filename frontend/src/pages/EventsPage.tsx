import { useState, useCallback, useEffect, useRef } from 'react';
import { useEvents } from '../api/hooks';
import { useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, XMarkIcon, PencilSquareIcon } from '@heroicons/react/24/outline';
import SortableFilterableHeader from '../components/SortableFilterableHeader';
import ColumnPicker from '../components/ColumnPicker';
import CompactTableFrame from '../components/CompactTableFrame';
import PageHeader from '../components/PageHeader';
import PageShell, { PageErrorState, PageLoadingState } from '../components/PageShell';
import { useCompactView } from '../hooks/useCompactView';
import { useTableSortFilter, applyTableSortFilter } from '../hooks/useTableSortFilter';
import { useColumnVisibility } from '../hooks/useColumnVisibility';
import AddEventModal from '../components/AddEventModal';
import EventDetailsModal from '../components/EventDetailsModal';
import BulkEditModal from '../components/BulkEditModal';
import apiClient from '../api/client';
import type { Event } from '../types';
import { useUISettings } from '../hooks/useUISettings';
import { TABLE_ROW_HOVER, BADGE_RED, BADGE_GREEN, BADGE_GRAY } from '../utils/designTokens';
import { formatDateInTimezone } from '../utils/timezone';


interface Fighter {
  id: number;
  name: string;
  slug: string;
  nickname?: string;
  weightClass?: string;
  nationality?: string;
  wins: number;
  losses: number;
  draws: number;
  noContests: number;
  birthDate?: string;
  height?: string;
  reach?: string;
  imageUrl?: string;
  bio?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

interface SearchResult {
  tapologyId: string;
  title: string;
  organization: string;
  eventDate: string;
  venue?: string;
  location?: string;
  posterUrl?: string;
  fights?: {
    fighter1: Fighter | string;
    fighter2: Fighter | string;
    weightClass?: string;
    isMainEvent: boolean;
  }[];
}

type EventsColumnKey =
  | 'title'
  | 'organization'
  | 'date'
  | 'venue'
  | 'monitored'
  | 'hasFile'
  | 'quality';

export default function EventsPage() {
  const { data: events, isLoading, error } = useEvents();
  const queryClient = useQueryClient();
  const { timezone } = useUISettings();
  const compactView = useCompactView();
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [showResults, setShowResults] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState<SearchResult | null>(null);
  const [selectedEventDetails, setSelectedEventDetails] = useState<Event | null>(null);
  const [selectedEventIds, setSelectedEventIds] = useState<Set<number>>(new Set());
  const [showBulkEditModal, setShowBulkEditModal] = useState(false);
  const searchRef = useRef<HTMLDivElement>(null);

  const { sortCol, sortDir, colFilters, activeFilterCol, handleColSort, onFilterChange, onFilterToggle } = useTableSortFilter('eventDate');

  // Column visibility for compact table — title and date always shown.
  const { isVisible, toggleCol } = useColumnVisibility<EventsColumnKey>(
    'events-col-visibility',
    { title: true, organization: true, date: true, venue: true, monitored: true, hasFile: true, quality: true },
    ['title', 'date']
  );

  // Debounced search function
  const searchEvents = useCallback(async (query: string) => {
    if (!query || query.length < 3) {
      setSearchResults([]);
      setShowResults(false);
      return;
    }

    setIsSearching(true);
    try {
      const response = await apiClient.get<SearchResult[]>('/search/events', {
        params: { q: query },
      });
      const results = Array.isArray(response.data) ? response.data : [];
      setSearchResults(results);
      setShowResults(true);
    } catch (err) {
      console.error('Search failed:', err);
      setSearchResults([]);
      setShowResults(false);
    } finally {
      setIsSearching(false);
    }
  }, []);

  // Handle search input with debounce
  useEffect(() => {
    const timeoutId = setTimeout(() => {
      searchEvents(searchQuery);
    }, 500);

    return () => clearTimeout(timeoutId);
  }, [searchQuery, searchEvents]);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (searchRef.current && !searchRef.current.contains(event.target as Node)) {
        setShowResults(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const handleSelectEvent = (event: SearchResult) => {
    setSelectedEvent(event);
    setShowResults(false);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
    setSearchResults([]);
    setShowResults(false);
  };

  const handleCloseModal = () => {
    setSelectedEvent(null);
  };

  const handleAddSuccess = () => {
    setSelectedEvent(null);
    setSearchQuery('');
    setSearchResults([]);
    // Refresh events list to show newly added event
    queryClient.invalidateQueries({ queryKey: ['events'] });
  };

  const getFighterName = (fighter: Fighter | string): string => {
    return typeof fighter === 'string' ? fighter : fighter.name;
  };

  const toggleEventSelection = (eventId: number) => {
    const newSelected = new Set(selectedEventIds);
    if (newSelected.has(eventId)) {
      newSelected.delete(eventId);
    } else {
      newSelected.add(eventId);
    }
    setSelectedEventIds(newSelected);
  };

  const selectAll = () => {
    if (events) {
      setSelectedEventIds(new Set(events.map(e => e.id)));
    }
  };

  const deselectAll = () => {
    setSelectedEventIds(new Set());
  };

  const handleBulkEditSuccess = () => {
    setSelectedEventIds(new Set());
    queryClient.invalidateQueries({ queryKey: ['events'] });
  };

  if (isLoading) {
    return <PageLoadingState label="Loading events..." className="h-full" />;
  }

  if (error) {
    return (
      <PageErrorState
        title="Error Loading Events"
        message={(error as Error).message}
      />
    );
  }

  const renderCompactTable = () => {
    if (!events || events.length === 0) {
      return (
        <div className="text-center py-16">
          <p className="text-gray-400">No events in your library. Start building your MMA collection by searching for events above.</p>
        </div>
      );
    }

    const paginatedEvents = applyTableSortFilter(events, colFilters, sortCol, sortDir, (col, item) => {
      switch (col) {
        case 'title': return String(item.title || '');
        case 'leagueName': return String(item.organization || item.leagueName || '');
        case 'eventDate': return String(item.eventDate || '');
        default: return '';
      }
    });

    const colDefs = [
      { key: 'title', label: 'Title', alwaysVisible: true },
      { key: 'organization', label: 'Organization' },
      { key: 'date', label: 'Date', alwaysVisible: true },
      { key: 'venue', label: 'Venue' },
      { key: 'monitored', label: 'Monitored' },
      { key: 'hasFile', label: 'Has File' },
      { key: 'quality', label: 'Quality' },
    ];

    return (
      <CompactTableFrame
        controls={
          <ColumnPicker
            columns={colDefs}
            isVisible={isVisible as (col: string) => boolean}
            onToggle={toggleCol as (col: string) => void}
          />
        }
      >
          <thead>
            <tr className="text-xs text-gray-400 uppercase text-left border-b border-gray-700 bg-gray-950 sticky top-0">
              <th className="px-2 py-1.5 w-10">
                <input
                  type="checkbox"
                  checked={events.length > 0 && selectedEventIds.size === events.length}
                  onChange={() => { if (selectedEventIds.size === events.length) { deselectAll(); } else { selectAll(); } }}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-500 cursor-pointer"
                />
              </th>
              <SortableFilterableHeader col="title" label="Title" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-2 py-1.5" />
              {isVisible('organization') && <SortableFilterableHeader col="leagueName" label="Organization" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-2 py-1.5" />}
              <SortableFilterableHeader col="eventDate" label="Date" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-2 py-1.5" />
              {isVisible('venue') && <th className="px-2 py-1.5">Venue</th>}
              {isVisible('monitored') && <th className="px-2 py-1.5 text-center">Monitored</th>}
              {isVisible('hasFile') && <th className="px-2 py-1.5 text-center">Has File</th>}
              {isVisible('quality') && <th className="px-2 py-1.5">Quality</th>}
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-700">
            {paginatedEvents.map((event) => (
              <tr key={event.id} onClick={() => setSelectedEventDetails(event)} className={`${TABLE_ROW_HOVER} cursor-pointer`}>
                <td className="px-2 py-1.5 text-center" onClick={(e) => e.stopPropagation()}>
                  <input type="checkbox" checked={selectedEventIds.has(event.id)} onChange={() => toggleEventSelection(event.id)} className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-500 cursor-pointer" />
                </td>
                <td className="px-3 py-1.5 font-medium text-white truncate">{event.title}</td>
                {isVisible('organization') && <td className="px-2 py-1.5 text-gray-300 truncate">{event.organization || '—'}</td>}
                <td className="px-2 py-1.5 text-gray-400 text-xs whitespace-nowrap">
                  {formatDateInTimezone(event.eventDate, timezone, { month: 'short', day: 'numeric' })}
                </td>
                {isVisible('venue') && <td className="px-2 py-1.5 text-gray-400 text-xs truncate">{event.venue || event.location || '—'}</td>}
                {isVisible('monitored') && (
                  <td className="px-2 py-1.5 text-center">
                    {event.monitored
                      ? <span className={BADGE_RED}>●</span>
                      : <span className="px-1.5 py-0.5 bg-gray-700/50 text-gray-500 text-xs rounded whitespace-nowrap">○</span>}
                  </td>
                )}
                {isVisible('hasFile') && (
                  <td className="px-2 py-1.5 text-center">
                    {event.hasFile
                      ? <span className={BADGE_GREEN}>✓</span>
                      : <span className="text-gray-600">—</span>}
                  </td>
                )}
                {isVisible('quality') && (
                  <td className="px-2 py-1.5 text-xs">
                    {event.quality ? <span className={BADGE_GRAY}>{event.quality}</span> : '—'}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
      </CompactTableFrame>
    );
  };

  const headerSubtitle =
    events && events.length > 0
      ? `${events.length} ${events.length === 1 ? 'event' : 'events'} in your library`
      : 'Start building your MMA collection by searching for events below';

  return (
    <PageShell>
      <PageHeader title="Events" subtitle={headerSubtitle} />

      {/* Search Bar with Live Results */}
      <div ref={searchRef} className="mb-6 relative">
        <div className="relative max-w-3xl">
          <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none">
            <MagnifyingGlassIcon className="h-5 w-5 text-gray-400" />
          </div>
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search for events to add (UFC, Bellator, PFL, etc.)..."
            className="w-full pl-12 pr-12 py-3 bg-gray-900 border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all"
            autoComplete="off"
          />
          {searchQuery && (
            <button
              onClick={handleClearSearch}
              className="absolute inset-y-0 right-0 pr-4 flex items-center text-gray-400 hover:text-white transition-colors"
            >
              <XMarkIcon className="h-5 w-5" />
            </button>
          )}

          {/* Search Results Dropdown */}
          {showResults && (
            <div className="absolute top-full left-0 right-0 mt-2 bg-gray-900 border border-red-900/30 rounded-lg shadow-2xl shadow-black/50 max-h-96 overflow-y-auto z-50">
              {isSearching ? (
                <div className="p-4 text-center">
                  <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-red-600 mx-auto"></div>
                  <p className="text-gray-400 text-sm mt-2">Searching...</p>
                </div>
              ) : searchResults.length > 0 ? (
                <div className="py-2">
                  {searchResults.map((event) => {
                    const mainFight = event.fights?.find(f => f.isMainEvent);
                    return (
                      <button
                        key={event.tapologyId}
                        onClick={() => handleSelectEvent(event)}
                        className="w-full px-4 py-3 hover:bg-red-900/20 transition-colors text-left border-b border-gray-800 last:border-0"
                      >
                        <div className="flex items-start gap-3">
                          {/* Event Poster Thumbnail */}
                          <div className="w-12 h-16 bg-gray-950 rounded overflow-hidden flex-shrink-0">
                            {event.posterUrl ? (
                              <img
                                src={event.posterUrl}
                                alt={event.title}
                                className="w-full h-full object-cover"
                              />
                            ) : (
                              <div className="w-full h-full flex items-center justify-center">
                                <svg className="w-6 h-6 text-gray-700" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                                </svg>
                              </div>
                            )}
                          </div>

                          {/* Event Info */}
                          <div className="flex-1 min-w-0">
                            <h3 className="text-white font-semibold line-clamp-1">{event.title}</h3>
                            <p className="text-red-400 text-sm font-medium">{event.organization}</p>
                            <p className="text-gray-400 text-sm">
                              {formatDateInTimezone(event.eventDate, timezone, {
                                year: 'numeric',
                                month: 'short',
                                day: 'numeric',
                              })}
                            </p>
                            {mainFight && (
                              <p className="text-gray-500 text-xs mt-1">
                                Main Event: {getFighterName(mainFight.fighter1)} vs {getFighterName(mainFight.fighter2)}
                              </p>
                            )}
                          </div>
                        </div>
                      </button>
                    );
                  })}
                </div>
              ) : (
                <div className="p-4 text-center text-gray-400 text-sm">
                  No events found for "{searchQuery}"
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Selection Toolbar */}
      {events && events.length > 0 && (
        <div className="mb-6 flex items-center justify-between bg-gray-900 border border-red-900/30 rounded-lg px-4 py-3">
          <div className="flex items-center gap-3">
            <button
              onClick={selectAll}
              className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-white text-sm font-medium rounded-lg transition-colors"
            >
              Select All
            </button>
            <button
              onClick={deselectAll}
              className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-white text-sm font-medium rounded-lg transition-colors"
            >
              Deselect All
            </button>
            <span className="text-sm text-gray-400">
              {selectedEventIds.size} of {events.length} selected
            </span>
          </div>
          <button
            onClick={() => setShowBulkEditModal(true)}
            disabled={selectedEventIds.size === 0}
            className="flex items-center gap-2 px-4 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm font-semibold rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <PencilSquareIcon className="w-5 h-5" />
            Edit Selected
          </button>
        </div>
      )}

      {/* Events Grid/Table or Empty State */}
      {compactView ? (
        renderCompactTable()
      ) : events && events.length > 0 ? (
        <div className="flex flex-col gap-3">
          {events.map((event) => (
            <div
              key={event.id}
              onClick={() => setSelectedEventDetails(event)}
              className="bg-gray-800 rounded-lg p-4 hover:bg-gray-750 transition-colors border border-gray-700 cursor-pointer"
            >
              <div className="flex items-start justify-between gap-4">
                <div className="flex items-start gap-3 min-w-0 flex-1">
                  <input
                    type="checkbox"
                    checked={selectedEventIds.has(event.id)}
                    onChange={() => toggleEventSelection(event.id)}
                    onClick={(e) => e.stopPropagation()}
                    className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500 focus:ring-2 cursor-pointer flex-shrink-0"
                  />
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2 flex-wrap mb-1">
                      <h3 className="text-base font-semibold text-white">{event.title}</h3>
                      <span className="px-2 py-0.5 bg-red-900/30 text-red-400 text-xs rounded">{event.organization}</span>
                      {event.monitored && (
                        <span className={BADGE_RED}>Monitored</span>
                      )}
                      {event.hasFile && (
                        <span className={BADGE_GREEN}>✓ Downloaded</span>
                      )}
                    </div>
                    <div className="flex items-center gap-3 text-sm text-gray-500 flex-wrap">
                      <span>{formatDateInTimezone(event.eventDate, timezone, { year: 'numeric', month: 'short', day: 'numeric' })}</span>
                      {event.venue && <><span className="text-gray-600">•</span><span>{event.venue}</span></>}
                      {event.location && <><span className="text-gray-600">•</span><span>{event.location}</span></>}
                    </div>
                  </div>
                </div>
                {event.quality && (
                  <span className="px-2 py-0.5 bg-purple-900/30 text-purple-400 text-xs rounded flex-shrink-0">
                    {event.quality}
                  </span>
                )}
              </div>
            </div>
          ))}
        </div>
        ) : (
          <div className="flex items-center justify-center py-16">
            <div className="text-center max-w-md">
              <div className="mb-8">
                <div className="inline-block p-6 bg-red-950/30 rounded-full border-2 border-red-900/50">
                  <MagnifyingGlassIcon className="w-16 h-16 text-red-600" />
                </div>
              </div>
              <h2 className="text-3xl font-bold mb-4 text-white">No Events in Library</h2>
              <p className="text-gray-400">
                Use the search bar above to find and add events to your library. Try searching for UFC, Bellator, EPL, or any other sports organization.
              </p>
            </div>
          </div>
        )}

      {/* Add Event Modal */}
      {selectedEvent && (
        <AddEventModal
          isOpen={!!selectedEvent}
          onClose={handleCloseModal}
          event={selectedEvent}
          onSuccess={handleAddSuccess}
        />
      )}

      {/* Event Details Modal */}
      {selectedEventDetails && (
        <EventDetailsModal
          isOpen={!!selectedEventDetails}
          onClose={() => setSelectedEventDetails(null)}
          event={selectedEventDetails}
        />
      )}

      {/* Bulk Edit Modal */}
      {showBulkEditModal && events && (
        <BulkEditModal
          isOpen={showBulkEditModal}
          onClose={() => setShowBulkEditModal(false)}
          selectedEvents={events.filter(e => selectedEventIds.has(e.id))}
          onSaveSuccess={handleBulkEditSuccess}
        />
      )}
    </PageShell>
  );
}
