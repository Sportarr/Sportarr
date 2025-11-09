import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { CalendarIcon, MapPinIcon, TvIcon, MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import AddEventModal from '../components/AddEventModal';

// Sport categories for filtering
const SPORT_FILTERS = [
  { id: 'Soccer', name: 'Soccer', icon: '⚽' },
  { id: 'Basketball', name: 'Basketball', icon: '🏀' },
  { id: 'Fighting', name: 'Fighting', icon: '🥊' },
  { id: 'Baseball', name: 'Baseball', icon: '⚾' },
  { id: 'Football', name: 'Football', icon: '🏈' },
  { id: 'Hockey', name: 'Hockey', icon: '🏒' },
  { id: 'Tennis', name: 'Tennis', icon: '🎾' },
  { id: 'Golf', name: 'Golf', icon: '⛳' },
  { id: 'Racing', name: 'Racing', icon: '🏎️' },
];

interface TVScheduleEvent {
  idEvent: string;
  strEvent: string;
  strSport: string;
  strLeague: string;
  strHomeTeam?: string;
  strAwayTeam?: string;
  dateEvent: string;
  strTime?: string;
  strTimeLocal?: string;
  strVenue?: string;
  strCountry?: string;
  strCity?: string;
  strPoster?: string;
  strThumb?: string;
  strBanner?: string;
  strStatus?: string;
  strSeason?: string;
  intRound?: string;
}

export default function TheSportsDBEventSearchPage() {
  const [selectedSport, setSelectedSport] = useState('Soccer');
  const [selectedDate, setSelectedDate] = useState(getTodayDateString());
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedEvent, setSelectedEvent] = useState<any>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);

  // Fetch TV schedule for selected sport and date
  const { data: tvSchedule, isLoading, error, refetch } = useQuery({
    queryKey: ['tv-schedule', selectedSport, selectedDate],
    queryFn: async () => {
      const params = new URLSearchParams({
        sport: selectedSport,
        date: selectedDate,
      });
      const response = await fetch(`/api/events/tv-schedule?${params}`);
      if (!response.ok) throw new Error('Failed to fetch TV schedule');
      return response.json() as Promise<TVScheduleEvent[]>;
    },
  });

  // Filter events by search query
  const filteredEvents = tvSchedule?.filter(event => {
    if (!searchQuery) return true;
    const query = searchQuery.toLowerCase();
    return (
      event.strEvent.toLowerCase().includes(query) ||
      event.strLeague?.toLowerCase().includes(query) ||
      event.strHomeTeam?.toLowerCase().includes(query) ||
      event.strAwayTeam?.toLowerCase().includes(query)
    );
  }) || [];

  function getTodayDateString(): string {
    const today = new Date();
    return today.toISOString().split('T')[0];
  }

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  };

  const handleAddEvent = (event: TVScheduleEvent) => {
    // Transform TheSportsDB event to AddEventModal format
    const isCombatSport = event.strSport === 'Fighting' || event.strSport === 'Boxing' || event.strSport === 'MMA';

    setSelectedEvent({
      externalId: event.idEvent,
      title: event.strEvent,
      sport: event.strSport,
      eventDate: event.dateEvent + (event.strTime ? `T${event.strTime}` : 'T00:00:00'),
      venue: event.strVenue,
      location: event.strCity && event.strCountry ? `${event.strCity}, ${event.strCountry}` : (event.strCountry || undefined),
      posterUrl: event.strPoster || event.strThumb || event.strBanner,
      status: event.strStatus,
      season: event.strSeason,
      round: event.intRound,

      // Combat sports fields
      ...(isCombatSport && {
        organization: event.strLeague,
      }),

      // Team sports fields
      ...(!isCombatSport && {
        league: {
          id: 0, // Will be resolved in backend
          name: event.strLeague,
          sport: event.strSport,
        },
        homeTeam: event.strHomeTeam ? {
          id: 0, // Will be resolved in backend
          name: event.strHomeTeam,
        } : undefined,
        awayTeam: event.strAwayTeam ? {
          id: 0, // Will be resolved in backend
          name: event.strAwayTeam,
        } : undefined,
      }),
    });
    setIsModalOpen(true);
  };

  const handleModalSuccess = () => {
    setIsModalOpen(false);
    setSelectedEvent(null);
    // Optionally refetch or show success message
  };

  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <p className="text-red-500 text-xl mb-4">Failed to load events</p>
          <button
            onClick={() => refetch()}
            className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-7xl mx-auto px-4 py-8">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-3xl font-bold text-white mb-2">Add Events</h1>
        <p className="text-gray-400">Browse and add sporting events from TheSportsDB</p>
      </div>

      {/* Sport Filter Tabs */}
      <div className="mb-6">
        <div className="flex gap-2 overflow-x-auto pb-2">
          {SPORT_FILTERS.map(sport => (
            <button
              key={sport.id}
              onClick={() => setSelectedSport(sport.id)}
              className={`
                flex items-center gap-2 px-4 py-2 rounded-lg whitespace-nowrap font-medium transition-all
                ${selectedSport === sport.id
                  ? 'bg-red-600 text-white'
                  : 'bg-gray-900 text-gray-400 hover:bg-gray-800 hover:text-white border border-red-900/30'
                }
              `}
            >
              <span className="text-xl">{sport.icon}</span>
              <span>{sport.name}</span>
            </button>
          ))}
        </div>
      </div>

      {/* Date Picker and Search */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-8">
        {/* Date Picker */}
        <div>
          <label className="block text-sm font-medium text-gray-400 mb-2">
            <CalendarIcon className="inline w-4 h-4 mr-1" />
            Select Date
          </label>
          <input
            type="date"
            value={selectedDate}
            onChange={(e) => setSelectedDate(e.target.value)}
            className="w-full px-4 py-3 bg-gray-900 border border-red-900/30 rounded-lg text-white focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20"
          />
        </div>

        {/* Search Filter */}
        <div>
          <label className="block text-sm font-medium text-gray-400 mb-2">
            <MagnifyingGlassIcon className="inline w-4 h-4 mr-1" />
            Filter Events
          </label>
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search by event, league, or team..."
            className="w-full px-4 py-3 bg-gray-900 border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20"
          />
        </div>
      </div>

      {/* Loading State */}
      {isLoading && (
        <div className="text-center py-12">
          <div className="inline-block animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-red-600 mb-4"></div>
          <p className="text-gray-400">Loading events...</p>
        </div>
      )}

      {/* No Results */}
      {!isLoading && filteredEvents.length === 0 && (
        <div className="text-center py-12 bg-gray-800/50 rounded-lg border border-gray-700">
          <p className="text-gray-400 text-lg">No events found</p>
          <p className="text-gray-500 text-sm mt-2">
            {searchQuery ? 'Try a different search term' : 'No events scheduled for this date'}
          </p>
        </div>
      )}

      {/* Events Grid */}
      {!isLoading && filteredEvents.length > 0 && (
        <div className="space-y-4">
          <p className="text-gray-400 mb-4">
            Found {filteredEvents.length} event{filteredEvents.length !== 1 ? 's' : ''} on {formatDate(selectedDate)}
          </p>

          {filteredEvents.map((event) => (
            <div
              key={event.idEvent}
              className="bg-gradient-to-r from-gray-900 to-gray-800 border border-gray-700 rounded-lg p-6 hover:border-red-600 transition-colors"
            >
              <div className="flex gap-6">
                {/* Poster */}
                <div className="flex-shrink-0">
                  {event.strPoster || event.strThumb || event.strBanner ? (
                    <img
                      src={event.strPoster || event.strThumb || event.strBanner}
                      alt={event.strEvent}
                      className="w-32 h-48 object-cover rounded-lg"
                      onError={(e) => {
                        e.currentTarget.style.display = 'none';
                      }}
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
                      <h3 className="text-2xl font-bold text-white mb-1">{event.strEvent}</h3>
                      <div className="flex items-center gap-4 text-sm text-gray-400">
                        <span className="px-2 py-1 bg-red-900/30 text-red-400 rounded">
                          {event.strSport}
                        </span>
                        <span className="px-2 py-1 bg-gray-800 text-gray-300 rounded">
                          {event.strLeague}
                        </span>
                      </div>
                    </div>
                    <button
                      onClick={() => handleAddEvent(event)}
                      className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg font-semibold transition-colors"
                    >
                      + Add to Library
                    </button>
                  </div>

                  {/* Event Info */}
                  <div className="grid grid-cols-2 gap-3 mb-4">
                    <div className="flex items-center gap-2 text-gray-300">
                      <CalendarIcon className="w-5 h-5 text-gray-500" />
                      <span>{formatDate(event.dateEvent)}</span>
                      {event.strTimeLocal && (
                        <span className="text-gray-400 text-sm">at {event.strTimeLocal}</span>
                      )}
                    </div>
                    {event.strVenue && (
                      <div className="flex items-center gap-2 text-gray-300">
                        <MapPinIcon className="w-5 h-5 text-gray-500" />
                        <span>{event.strVenue}</span>
                      </div>
                    )}
                    {(event.strCity || event.strCountry) && (
                      <div className="flex items-center gap-2 text-gray-300 text-sm">
                        <span className="text-gray-500">Location:</span>
                        <span>{event.strCity && event.strCountry ? `${event.strCity}, ${event.strCountry}` : (event.strCountry || event.strCity)}</span>
                      </div>
                    )}
                  </div>

                  {/* Matchup (Team Sports) */}
                  {event.strHomeTeam && event.strAwayTeam && (
                    <div className="mt-4 pt-4 border-t border-gray-700">
                      <div className="flex items-center justify-center gap-4 text-lg">
                        <span className="text-white font-semibold">{event.strHomeTeam}</span>
                        <span className="text-gray-500">vs</span>
                        <span className="text-white font-semibold">{event.strAwayTeam}</span>
                      </div>
                      {event.strSeason && (
                        <p className="text-center text-gray-400 text-sm mt-2">
                          Season {event.strSeason}
                          {event.intRound && ` • Round ${event.intRound}`}
                        </p>
                      )}
                    </div>
                  )}

                  {/* Status Badge */}
                  {event.strStatus && event.strStatus !== 'Scheduled' && (
                    <div className="mt-3">
                      <span className={`px-3 py-1 rounded text-sm font-semibold ${
                        event.strStatus === 'Live' ? 'bg-green-900/30 text-green-400' :
                        event.strStatus === 'Completed' ? 'bg-gray-800 text-gray-400' :
                        'bg-yellow-900/30 text-yellow-400'
                      }`}>
                        {event.strStatus}
                      </span>
                    </div>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Add Event Modal */}
      {selectedEvent && (
        <AddEventModal
          isOpen={isModalOpen}
          onClose={() => {
            setIsModalOpen(false);
            setSelectedEvent(null);
          }}
          event={selectedEvent}
          onSuccess={handleModalSuccess}
        />
      )}

      {/* Help Text */}
      {!isLoading && filteredEvents.length === 0 && !searchQuery && (
        <div className="mt-12 text-center">
          <p className="text-gray-500 mb-4">Browse events by:</p>
          <div className="flex flex-wrap justify-center gap-3">
            <span className="px-4 py-2 bg-gray-800 text-gray-400 rounded-lg">Select a sport</span>
            <span className="px-4 py-2 bg-gray-800 text-gray-400 rounded-lg">Choose a date</span>
            <span className="px-4 py-2 bg-gray-800 text-gray-400 rounded-lg">Search by name</span>
          </div>
        </div>
      )}
    </div>
  );
}
