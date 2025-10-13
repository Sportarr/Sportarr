import { useState, useCallback } from 'react';
import { MagnifyingGlassIcon, XMarkIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { useQualityProfiles } from '../api/hooks';
import apiClient from '../api/client';

interface SearchResult {
  tapologyId: string;
  title: string;
  organization: string;
  eventDate: string;
  venue?: string;
  location?: string;
  posterUrl?: string;
  fights?: {
    fighter1: string;
    fighter2: string;
    weightClass?: string;
    isMainEvent: boolean;
  }[];
}

export default function AddEventPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState<SearchResult | null>(null);
  const [isAdding, setIsAdding] = useState(false);
  const { data: qualityProfiles } = useQualityProfiles();

  // Form state for selected event
  const [monitored, setMonitored] = useState(true);
  const [qualityProfileId, setQualityProfileId] = useState(1);

  // Debounced search function
  const searchEvents = useCallback(async (query: string) => {
    if (!query || query.length < 3) {
      setSearchResults([]);
      return;
    }

    setIsSearching(true);
    try {
      // Call Fightarr-API to search for events
      const response = await apiClient.get<SearchResult[]>('/search/events', {
        params: { q: query },
      });
      setSearchResults(response.data);
    } catch (error) {
      console.error('Search failed:', error);
      setSearchResults([]);
    } finally {
      setIsSearching(false);
    }
  }, []);

  // Handle search input with debounce
  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const query = e.target.value;
    setSearchQuery(query);

    // Simple debounce - wait 500ms after user stops typing
    const timeoutId = setTimeout(() => {
      searchEvents(query);
    }, 500);

    return () => clearTimeout(timeoutId);
  };

  // Handle adding event to library
  const handleAddEvent = async () => {
    if (!selectedEvent) return;

    setIsAdding(true);
    try {
      await apiClient.post('/api/events', {
        tapologyId: selectedEvent.tapologyId,
        title: selectedEvent.title,
        organization: selectedEvent.organization,
        eventDate: selectedEvent.eventDate,
        venue: selectedEvent.venue,
        location: selectedEvent.location,
        monitored,
        qualityProfileId,
      });

      // Success - clear form and show success message
      setSelectedEvent(null);
      setSearchQuery('');
      setSearchResults([]);
      alert(`Successfully added ${selectedEvent.title} to your library!`);
    } catch (error) {
      console.error('Failed to add event:', error);
      alert('Failed to add event. Please try again.');
    } finally {
      setIsAdding(false);
    }
  };

  return (
    <div className="p-8">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-4xl font-bold text-white mb-2">Add New Event</h1>
        <p className="text-gray-400">Search for MMA events to add to your library</p>
      </div>

      {/* Search Box */}
      <div className="mb-8">
        <div className="relative max-w-2xl">
          <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none">
            <MagnifyingGlassIcon className="h-5 w-5 text-gray-400" />
          </div>
          <input
            type="text"
            value={searchQuery}
            onChange={handleSearchChange}
            placeholder="Search by event name, organization, or fighters..."
            className="w-full pl-12 pr-12 py-4 bg-gray-900 border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all"
            autoFocus
          />
          {searchQuery && (
            <button
              onClick={() => {
                setSearchQuery('');
                setSearchResults([]);
              }}
              className="absolute inset-y-0 right-0 pr-4 flex items-center text-gray-400 hover:text-white"
            >
              <XMarkIcon className="h-5 w-5" />
            </button>
          )}
        </div>
        {isSearching && (
          <p className="mt-3 text-sm text-gray-400 flex items-center">
            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-red-600 mr-2"></div>
            Searching...
          </p>
        )}
      </div>

      {/* Search Results */}
      {searchResults.length > 0 && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6 mb-8">
          {searchResults.map((event) => (
            <button
              key={event.tapologyId}
              onClick={() => setSelectedEvent(event)}
              className={`group bg-gradient-to-br from-gray-900 to-black rounded-lg overflow-hidden border transition-all duration-300 text-left ${
                selectedEvent?.tapologyId === event.tapologyId
                  ? 'border-red-600 ring-2 ring-red-600/50'
                  : 'border-red-900/30 hover:border-red-600/50'
              }`}
            >
              {/* Poster */}
              <div className="relative aspect-[2/3] bg-gray-950">
                {event.posterUrl ? (
                  <img
                    src={event.posterUrl}
                    alt={event.title}
                    className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                  />
                ) : (
                  <div className="w-full h-full flex items-center justify-center">
                    <svg className="w-24 h-24 text-gray-700" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                    </svg>
                  </div>
                )}
                {selectedEvent?.tapologyId === event.tapologyId && (
                  <div className="absolute top-2 right-2">
                    <CheckCircleIcon className="w-8 h-8 text-red-600" />
                  </div>
                )}
              </div>

              {/* Event Info */}
              <div className="p-4">
                <h3 className="text-lg font-bold text-white mb-2 line-clamp-2">{event.title}</h3>
                <p className="text-red-400 font-semibold text-sm mb-1">{event.organization}</p>
                <p className="text-gray-400 text-sm">
                  {new Date(event.eventDate).toLocaleDateString('en-US', {
                    year: 'numeric',
                    month: 'long',
                    day: 'numeric',
                  })}
                </p>
                {event.venue && <p className="text-gray-500 text-xs mt-1 line-clamp-1">{event.venue}</p>}
                {event.fights && event.fights.length > 0 && (
                  <p className="text-gray-500 text-xs mt-2">{event.fights.length} fights</p>
                )}
              </div>
            </button>
          ))}
        </div>
      )}

      {/* No Results Message */}
      {searchQuery.length >= 3 && !isSearching && searchResults.length === 0 && (
        <div className="text-center py-12">
          <div className="inline-block p-6 bg-red-950/20 rounded-full border-2 border-red-900/30 mb-4">
            <MagnifyingGlassIcon className="w-12 h-12 text-red-600/50" />
          </div>
          <h3 className="text-xl font-bold text-white mb-2">No Events Found</h3>
          <p className="text-gray-400">Try a different search term</p>
        </div>
      )}

      {/* Selected Event Details & Configuration */}
      {selectedEvent && (
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <h2 className="text-2xl font-bold text-white mb-6 flex items-center">
            <CheckCircleIcon className="w-8 h-8 text-red-600 mr-3" />
            Configure Event
          </h2>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
            {/* Monitored Toggle */}
            <div>
              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={monitored}
                  onChange={(e) => setMonitored(e.target.checked)}
                  className="w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                />
                <div>
                  <span className="text-white font-medium">Monitor Event</span>
                  <p className="text-sm text-gray-400">Automatically search for this event</p>
                </div>
              </label>
            </div>

            {/* Quality Profile Selection */}
            <div>
              <label className="block text-white font-medium mb-2">Quality Profile</label>
              <select
                value={qualityProfileId}
                onChange={(e) => setQualityProfileId(Number(e.target.value))}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20"
              >
                {qualityProfiles?.map((profile) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Event Details */}
          <div className="mb-6 p-4 bg-black/30 rounded-lg">
            <h3 className="text-xl font-bold text-white mb-3">{selectedEvent.title}</h3>
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <span className="text-gray-400">Organization:</span>
                <span className="text-white ml-2">{selectedEvent.organization}</span>
              </div>
              <div>
                <span className="text-gray-400">Date:</span>
                <span className="text-white ml-2">
                  {new Date(selectedEvent.eventDate).toLocaleDateString('en-US', {
                    year: 'numeric',
                    month: 'long',
                    day: 'numeric',
                  })}
                </span>
              </div>
              {selectedEvent.venue && (
                <div>
                  <span className="text-gray-400">Venue:</span>
                  <span className="text-white ml-2">{selectedEvent.venue}</span>
                </div>
              )}
              {selectedEvent.location && (
                <div>
                  <span className="text-gray-400">Location:</span>
                  <span className="text-white ml-2">{selectedEvent.location}</span>
                </div>
              )}
            </div>

            {/* Fight Card */}
            {selectedEvent.fights && selectedEvent.fights.length > 0 && (
              <div className="mt-4">
                <h4 className="text-white font-semibold mb-2">Fight Card ({selectedEvent.fights.length} fights)</h4>
                <div className="space-y-2">
                  {selectedEvent.fights.slice(0, 5).map((fight, idx) => (
                    <div key={idx} className="flex items-center justify-between text-sm">
                      <span className="text-white">
                        {fight.fighter1} vs {fight.fighter2}
                      </span>
                      <div className="flex items-center gap-2">
                        {fight.weightClass && <span className="text-gray-400">{fight.weightClass}</span>}
                        {fight.isMainEvent && (
                          <span className="px-2 py-0.5 bg-red-600 text-white text-xs rounded">MAIN EVENT</span>
                        )}
                      </div>
                    </div>
                  ))}
                  {selectedEvent.fights.length > 5 && (
                    <p className="text-gray-500 text-xs">+ {selectedEvent.fights.length - 5} more fights</p>
                  )}
                </div>
              </div>
            )}
          </div>

          {/* Action Buttons */}
          <div className="flex gap-4">
            <button
              onClick={handleAddEvent}
              disabled={isAdding}
              className="flex-1 px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed disabled:transform-none"
            >
              {isAdding ? 'Adding...' : 'Add Event to Library'}
            </button>
            <button
              onClick={() => setSelectedEvent(null)}
              className="px-6 py-3 bg-gray-800 hover:bg-gray-700 text-white font-semibold rounded-lg transition"
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
