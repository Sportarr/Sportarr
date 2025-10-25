import { useState, useCallback, useEffect, useRef } from 'react';
import { useEvents } from '../api/hooks';
import { MagnifyingGlassIcon, XMarkIcon } from '@heroicons/react/24/outline';
import AddEventModal from '../components/AddEventModal';
import EventDetailsModal from '../components/EventDetailsModal';
import apiClient from '../api/client';
import type { Event } from '../types';

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

export default function EventsPage() {
  const { data: events, isLoading, error, refetch } = useEvents();
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [showResults, setShowResults] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState<SearchResult | null>(null);
  const [selectedEventDetails, setSelectedEventDetails] = useState<Event | null>(null);
  const searchRef = useRef<HTMLDivElement>(null);

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
    refetch();
  };

  const getFighterName = (fighter: Fighter | string): string => {
    return typeof fighter === 'string' ? fighter : fighter.name;
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="animate-spin rounded-full h-16 w-16 border-b-4 border-red-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="bg-red-950 border border-red-700 text-red-100 px-6 py-4 rounded-lg">
          <p className="font-bold text-lg">Error Loading Events</p>
          <p className="text-sm mt-2">{(error as Error).message}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-4xl font-bold text-white mb-2">Events</h1>
        <p className="text-gray-400">
          {events && events.length > 0
            ? `${events.length} ${events.length === 1 ? 'event' : 'events'} in your library`
            : 'Start building your MMA collection by searching for events below'
          }
        </p>
      </div>

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
                              {new Date(event.eventDate).toLocaleDateString('en-US', {
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

      {/* Events Grid or Empty State */}
      {events && events.length > 0 ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
          {events.map((event) => (
            <div
              key={event.id}
              onClick={() => setSelectedEventDetails(event)}
              className="group bg-gradient-to-br from-gray-900 to-black rounded-lg overflow-hidden border border-red-900/30 hover:border-red-600/50 shadow-xl hover:shadow-2xl hover:shadow-red-900/20 transition-all duration-300 cursor-pointer"
            >
            {/* Event Poster */}
            <div className="relative aspect-[2/3] bg-gray-950">
              {event.images?.[0] ? (
                <img
                  src={event.images[0].remoteUrl}
                  alt={event.title}
                  className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                />
              ) : (
                <div className="w-full h-full flex items-center justify-center bg-gradient-to-br from-gray-900 to-black">
                  <svg
                    className="w-24 h-24 text-gray-700"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth={1}
                      d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z"
                    />
                  </svg>
                </div>
              )}

              {/* Status Overlay */}
              <div className="absolute top-2 right-2 flex gap-2">
                {event.monitored && (
                  <span className="px-2 py-1 bg-red-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                    MONITORED
                  </span>
                )}
                {event.hasFile && (
                  <span className="px-2 py-1 bg-green-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                    ✓
                  </span>
                )}
              </div>
            </div>

            {/* Event Info */}
            <div className="p-4">
              <h3 className="text-lg font-bold text-white mb-2 line-clamp-2 group-hover:text-red-400 transition-colors">
                {event.title}
              </h3>

              <div className="space-y-1 text-sm">
                <p className="text-red-400 font-semibold">{event.organization}</p>

                <p className="text-gray-400">
                  {new Date(event.eventDate).toLocaleDateString('en-US', {
                    year: 'numeric',
                    month: 'long',
                    day: 'numeric',
                  })}
                </p>

                {event.venue && (
                  <p className="text-gray-500 text-xs line-clamp-1">{event.venue}</p>
                )}

                {event.location && (
                  <p className="text-gray-500 text-xs line-clamp-1">{event.location}</p>
                )}
              </div>

              {/* Quality Badge */}
              {event.quality && (
                <div className="mt-3 pt-3 border-t border-gray-800">
                  <span className="inline-block px-2 py-1 bg-gray-800 text-gray-400 text-xs rounded">
                    {event.quality}
                  </span>
                </div>
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
              Use the search bar above to find and add MMA events to your library. Try searching for UFC, Bellator, PFL, or any other combat sports organization.
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
    </div>
  );
}
