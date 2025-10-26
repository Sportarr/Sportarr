import { useState, useCallback, useEffect } from 'react';
import { MagnifyingGlassIcon, XMarkIcon, InformationCircleIcon } from '@heroicons/react/24/outline';
import AddEventSearchResult from '../components/AddEventSearchResult';
import AddEventModal from '../components/AddEventModal';
import apiClient from '../api/client';

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

export default function AddEventPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState<SearchResult | null>(null);
  const [hasSearched, setHasSearched] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Debounced search function
  const searchEvents = useCallback(async (query: string) => {
    if (!query || query.length < 1) {
      setSearchResults([]);
      setHasSearched(false);
      setError(null);
      return;
    }

    setIsSearching(true);
    setError(null);
    try {
      // Call backend API which proxies to Fightarr-API
      const response = await apiClient.get<SearchResult[]>('/search/events', {
        params: { q: query },
      });
      // Ensure response.data is an array
      const results = Array.isArray(response.data) ? response.data : [];
      setSearchResults(results);
      setHasSearched(true);
    } catch (err) {
      console.error('Search failed:', err);
      setError('Failed to search events. Please try again.');
      setSearchResults([]);
      setHasSearched(true);
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

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchQuery(e.target.value);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
    setSearchResults([]);
    setHasSearched(false);
    setError(null);
  };

  const handleSelectEvent = (event: SearchResult) => {
    setSelectedEvent(event);
  };

  const handleCloseModal = () => {
    setSelectedEvent(null);
  };

  const handleAddSuccess = () => {
    // Clear search after successful add
    setSearchQuery('');
    setSearchResults([]);
    setHasSearched(false);
  };

  return (
    <div className="p-8 max-w-7xl mx-auto">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-4xl font-bold text-white mb-2">Add New Event</h1>
        <p className="text-gray-400">Search for MMA events from Tapology and add them to your library</p>
      </div>

      {/* Search Box */}
      <div className="mb-8">
        <div className="relative max-w-3xl">
          <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none">
            <MagnifyingGlassIcon className="h-6 w-6 text-gray-400" />
          </div>
          <input
            type="text"
            value={searchQuery}
            onChange={handleSearchChange}
            placeholder="Search by event name, organization (e.g., UFC, Bellator), or fighter..."
            className="w-full pl-14 pr-12 py-4 bg-gray-900 border border-red-900/30 rounded-lg text-white text-lg placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all"
            autoFocus
          />
          {searchQuery && (
            <button
              onClick={handleClearSearch}
              className="absolute inset-y-0 right-0 pr-4 flex items-center text-gray-400 hover:text-white transition-colors"
            >
              <XMarkIcon className="h-6 w-6" />
            </button>
          )}
        </div>

        {/* Search Status */}
        <div className="mt-3 flex items-center justify-between max-w-3xl">
          {isSearching && (
            <p className="text-sm text-gray-400 flex items-center">
              <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-red-600 mr-2"></div>
              Searching Fightarr-API...
            </p>
          )}
          {!isSearching && hasSearched && searchResults.length > 0 && (
            <p className="text-sm text-gray-400">
              Found {searchResults.length} {searchResults.length === 1 ? 'event' : 'events'}
            </p>
          )}
        </div>
      </div>

      {/* Help Text - Show when no search */}
      {!hasSearched && !searchQuery && (
        <div className="max-w-3xl">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-blue-900/30 rounded-lg p-6">
            <div className="flex items-start">
              <InformationCircleIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
              <div>
                <h3 className="text-lg font-semibold text-white mb-2">How to Add Events</h3>
                <ul className="space-y-2 text-gray-300 text-sm">
                  <li className="flex items-start">
                    <span className="text-red-400 mr-2">•</span>
                    <span>Search for MMA events by name, organization (UFC, Bellator, PFL, etc.), or fighters</span>
                  </li>
                  <li className="flex items-start">
                    <span className="text-red-400 mr-2">•</span>
                    <span>Click on a search result to configure monitoring and quality settings</span>
                  </li>
                  <li className="flex items-start">
                    <span className="text-red-400 mr-2">•</span>
                    <span>Fightarr will automatically search for and download the event when it becomes available</span>
                  </li>
                  <li className="flex items-start">
                    <span className="text-red-400 mr-2">•</span>
                    <span>Already have events? You can import them from your existing library</span>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Error Message */}
      {error && (
        <div className="mb-8 max-w-3xl">
          <div className="bg-red-950/30 border border-red-900/50 rounded-lg p-4">
            <p className="text-red-400">{error}</p>
          </div>
        </div>
      )}

      {/* Search Results */}
      {searchResults.length > 0 && (
        <div className="space-y-4 mb-8">
          {searchResults.map((event) => (
            <AddEventSearchResult
              key={event.tapologyId}
              event={event}
              onSelect={() => handleSelectEvent(event)}
            />
          ))}
        </div>
      )}

      {/* No Results Message */}
      {hasSearched && !isSearching && searchResults.length === 0 && !error && searchQuery.length >= 1 && (
        <div className="text-center py-12 max-w-3xl mx-auto">
          <div className="inline-block p-6 bg-red-950/20 rounded-full border-2 border-red-900/30 mb-4">
            <MagnifyingGlassIcon className="w-12 h-12 text-red-600/50" />
          </div>
          <h3 className="text-xl font-bold text-white mb-2">No Events Found</h3>
          <p className="text-gray-400 mb-4">
            We couldn't find any events matching "{searchQuery}"
          </p>
          <div className="text-sm text-gray-500">
            <p className="mb-2">Try searching for:</p>
            <ul className="space-y-1">
              <li>• A specific organization (e.g., UFC, Bellator)</li>
              <li>• An event name (e.g., UFC 300)</li>
              <li>• A fighter's name</li>
            </ul>
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
    </div>
  );
}
