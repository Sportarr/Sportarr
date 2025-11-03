import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { MagnifyingGlassIcon, PlusCircleIcon, CalendarIcon, MapPinIcon, TvIcon } from '@heroicons/react/24/outline';
import { apiPost } from '../utils/api';

interface SearchResult {
  id: number;
  slug: string;
  title: string;
  organization: string;
  eventNumber: string | null;
  eventDate: string;
  eventType: string | null;
  venue: string | null;
  location: string | null;
  broadcaster: string | null;
  status: string;
  posterUrl: string | null;
  fightCount: number;
  fights: Array<{
    id: number;
    fighter1: {
      id: number;
      name: string;
      nickname: string | null;
    };
    fighter2: {
      id: number;
      name: string;
      nickname: string | null;
    };
    weightClass: string | null;
    isTitleFight: boolean;
    isMainEvent: boolean;
  }>;
}

export default function EventSearchPage() {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);
  const navigate = useNavigate();

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();

    if (query.length < 3) {
      return;
    }

    setSearching(true);
    setHasSearched(true);

    try {
      const response = await fetch(`/api/search/events?q=${encodeURIComponent(query)}`);
      const data = await response.json();
      setResults(data || []);
    } catch (error) {
      console.error('Search failed:', error);
      setResults([]);
    } finally {
      setSearching(false);
    }
  };

  const handleAddEvent = async (event: SearchResult) => {
    try {
      // Create event in local database
      await apiPost('/api/events', {
        title: event.title,
        organization: event.organization,
        eventDate: new Date(event.eventDate),
        venue: event.venue,
        location: event.location,
        images: event.posterUrl ? [event.posterUrl] : [],
        monitored: true,
        metadataId: event.id.toString(), // Store original metadata API ID
        slug: event.slug,
        fights: event.fights.map((fight, index) => ({
          fighter1: fight.fighter1.name,
          fighter2: fight.fighter2.name,
          weightClass: fight.weightClass || '',
          isTitleFight: fight.isTitleFight,
          isMainEvent: fight.isMainEvent,
          fightOrder: index + 1,
        })),
      });

      // Navigate to events page
      navigate('/events');
    } catch (error) {
      console.error('Failed to add event:', error);
      toast.error('Operation Failed', { description: 'alert('Failed to add event to library');'.replace("alert('", '').replace("');", '') });
    }
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  };

  return (
    <div className="max-w-7xl mx-auto px-4 py-8">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-3xl font-bold text-white mb-2">Search Events</h1>
        <p className="text-gray-400">Search for MMA events from UFC, Bellator, PFL, and more</p>
      </div>

      {/* Search Bar */}
      <form onSubmit={handleSearch} className="mb-8">
        <div className="relative">
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search for events (e.g., UFC 300, Jon Jones, Bellator)..."
            className="w-full px-6 py-4 pl-14 bg-gray-800 border border-gray-700 rounded-lg text-white text-lg focus:outline-none focus:border-red-600 transition-colors"
            autoFocus
          />
          <MagnifyingGlassIcon className="absolute left-4 top-1/2 transform -translate-y-1/2 w-6 h-6 text-gray-500" />
          <button
            type="submit"
            disabled={query.length < 3 || searching}
            className="absolute right-2 top-1/2 transform -translate-y-1/2 px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg font-semibold disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {searching ? 'Searching...' : 'Search'}
          </button>
        </div>
        {query.length > 0 && query.length < 3 && (
          <p className="mt-2 text-sm text-yellow-500">Please enter at least 3 characters</p>
        )}
      </form>

      {/* Results */}
      {searching && (
        <div className="text-center py-12">
          <div className="inline-block animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-red-600 mb-4"></div>
          <p className="text-gray-400">Searching events...</p>
        </div>
      )}

      {!searching && hasSearched && results.length === 0 && (
        <div className="text-center py-12 bg-gray-800/50 rounded-lg border border-gray-700">
          <p className="text-gray-400 text-lg">No events found for "{query}"</p>
          <p className="text-gray-500 text-sm mt-2">Try a different search term</p>
        </div>
      )}

      {!searching && results.length > 0 && (
        <div className="space-y-4">
          <p className="text-gray-400 mb-4">Found {results.length} event{results.length !== 1 ? 's' : ''}</p>

          {results.map((event) => (
            <div
              key={event.id}
              className="bg-gradient-to-r from-gray-900 to-gray-800 border border-gray-700 rounded-lg p-6 hover:border-red-600 transition-colors"
            >
              <div className="flex gap-6">
                {/* Poster */}
                <div className="flex-shrink-0">
                  {event.posterUrl ? (
                    <img
                      src={event.posterUrl}
                      alt={event.title}
                      className="w-32 h-48 object-cover rounded-lg"
                    />
                  ) : (
                    <div className="w-32 h-48 bg-gray-700 rounded-lg flex items-center justify-center">
                      <CalendarIcon className="w-12 h-12 text-gray-500" />
                    </div>
                  )}
                </div>

                {/* Event Details */}
                <div className="flex-grow">
                  <div className="flex items-start justify-between mb-3">
                    <div>
                      <h3 className="text-2xl font-bold text-white mb-1">{event.title}</h3>
                      <div className="flex items-center gap-4 text-sm text-gray-400">
                        <span className="px-2 py-1 bg-red-900/30 text-red-400 rounded">
                          {event.organization}
                        </span>
                        {event.eventNumber && (
                          <span>{event.eventNumber}</span>
                        )}
                      </div>
                    </div>
                    <button
                      onClick={() => handleAddEvent(event)}
                      className="flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg font-semibold transition-colors"
                    >
                      <PlusCircleIcon className="w-5 h-5" />
                      Add to Library
                    </button>
                  </div>

                  {/* Event Info */}
                  <div className="grid grid-cols-2 gap-3 mb-4">
                    <div className="flex items-center gap-2 text-gray-300">
                      <CalendarIcon className="w-5 h-5 text-gray-500" />
                      <span>{formatDate(event.eventDate)}</span>
                    </div>
                    {event.location && (
                      <div className="flex items-center gap-2 text-gray-300">
                        <MapPinIcon className="w-5 h-5 text-gray-500" />
                        <span>{event.location}</span>
                      </div>
                    )}
                    {event.venue && (
                      <div className="flex items-center gap-2 text-gray-300 text-sm">
                        <span className="text-gray-500">Venue:</span>
                        <span>{event.venue}</span>
                      </div>
                    )}
                    {event.broadcaster && (
                      <div className="flex items-center gap-2 text-gray-300 text-sm">
                        <TvIcon className="w-5 h-5 text-gray-500" />
                        <span>{event.broadcaster}</span>
                      </div>
                    )}
                  </div>

                  {/* Main Event */}
                  {event.fights && event.fights.length > 0 && (
                    <div className="mt-4 pt-4 border-t border-gray-700">
                      <p className="text-sm text-gray-500 mb-2">{event.fightCount} fights</p>
                      {event.fights
                        .filter((fight) => fight.isMainEvent)
                        .map((fight) => (
                          <div key={fight.id} className="flex items-center gap-2">
                            <span className="px-2 py-1 bg-yellow-900/30 text-yellow-400 text-xs rounded font-semibold">
                              MAIN EVENT
                            </span>
                            <span className="text-white font-semibold">
                              {fight.fighter1.name} vs {fight.fighter2.name}
                            </span>
                            {fight.isTitleFight && (
                              <span className="px-2 py-1 bg-red-900/30 text-red-400 text-xs rounded font-semibold">
                                TITLE FIGHT
                              </span>
                            )}
                            {fight.weightClass && (
                              <span className="text-gray-400 text-sm">
                                ({fight.weightClass})
                              </span>
                            )}
                          </div>
                        ))}
                    </div>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Help Text */}
      {!hasSearched && (
        <div className="mt-12 text-center">
          <p className="text-gray-500 mb-4">Search for MMA events by:</p>
          <div className="flex flex-wrap justify-center gap-3">
            <span className="px-4 py-2 bg-gray-800 text-gray-400 rounded-lg">Event name (UFC 300)</span>
            <span className="px-4 py-2 bg-gray-800 text-gray-400 rounded-lg">Fighter name (Jon Jones)</span>
            <span className="px-4 py-2 bg-gray-800 text-gray-400 rounded-lg">Organization (Bellator)</span>
          </div>
        </div>
      )}
    </div>
  );
}
