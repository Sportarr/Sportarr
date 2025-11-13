import { useState, useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, GlobeAltIcon, TrophyIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';

// Sport categories for filtering (complete TheSportsDB sport types)
const SPORT_FILTERS = [
  { id: 'all', name: 'All Sports', icon: '🌍' },
  { id: 'American Football', name: 'American Football', icon: '🏈' },
  { id: 'Athletics', name: 'Athletics', icon: '🏃' },
  { id: 'Australian Football', name: 'Australian Football', icon: '🏉' },
  { id: 'Badminton', name: 'Badminton', icon: '🏸' },
  { id: 'Baseball', name: 'Baseball', icon: '⚾' },
  { id: 'Basketball', name: 'Basketball', icon: '🏀' },
  { id: 'Climbing', name: 'Climbing', icon: '🧗' },
  { id: 'Cricket', name: 'Cricket', icon: '🏏' },
  { id: 'Cycling', name: 'Cycling', icon: '🚴' },
  { id: 'Darts', name: 'Darts', icon: '🎯' },
  { id: 'Esports', name: 'Esports', icon: '🎮' },
  { id: 'Equestrian', name: 'Equestrian', icon: '🏇' },
  { id: 'Extreme Sports', name: 'Extreme Sports', icon: '🪂' },
  { id: 'Field Hockey', name: 'Field Hockey', icon: '🏑' },
  { id: 'Fighting', name: 'Fighting', icon: '🥊' },
  { id: 'Gaelic', name: 'Gaelic', icon: '🏐' },
  { id: 'Gambling', name: 'Gambling', icon: '🎰' },
  { id: 'Golf', name: 'Golf', icon: '⛳' },
  { id: 'Gymnastics', name: 'Gymnastics', icon: '🤸' },
  { id: 'Handball', name: 'Handball', icon: '🤾' },
  { id: 'Ice Hockey', name: 'Ice Hockey', icon: '🏒' },
  { id: 'Lacrosse', name: 'Lacrosse', icon: '🥍' },
  { id: 'Motorsport', name: 'Motorsport', icon: '🏎️' },
  { id: 'Multi Sports', name: 'Multi Sports', icon: '🏅' },
  { id: 'Netball', name: 'Netball', icon: '🏀' },
  { id: 'Rugby', name: 'Rugby', icon: '🏉' },
  { id: 'Shooting', name: 'Shooting', icon: '🎯' },
  { id: 'Skating', name: 'Skating', icon: '⛸️' },
  { id: 'Skiing', name: 'Skiing', icon: '⛷️' },
  { id: 'Snooker', name: 'Snooker', icon: '🎱' },
  { id: 'Soccer', name: 'Soccer', icon: '⚽' },
  { id: 'Table Tennis', name: 'Table Tennis', icon: '🏓' },
  { id: 'Tennis', name: 'Tennis', icon: '🎾' },
  { id: 'Volleyball', name: 'Volleyball', icon: '🏐' },
  { id: 'Watersports', name: 'Watersports', icon: '🏄' },
  { id: 'Weightlifting', name: 'Weightlifting', icon: '🏋️' },
  { id: 'Wintersports', name: 'Wintersports', icon: '🎿' },
];

interface League {
  idLeague: string;
  strLeague: string;
  strSport: string;
  strLeagueAlternate?: string;
  intFormedYear?: string;
  strCountry?: string;
  strDescriptionEN?: string;
  strBadge?: string;
  strLogo?: string;
  strBanner?: string;
  strPoster?: string;
  strWebsite?: string;
}

interface AddedLeague {
  [key: string]: boolean;
}

export default function TheSportsDBLeagueSearchPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [addedLeagues, setAddedLeagues] = useState<AddedLeague>({});
  const queryClient = useQueryClient();

  // Fetch all leagues on mount
  const { data: allLeagues = [], isLoading } = useQuery({
    queryKey: ['leagues', 'all'],
    queryFn: async () => {
      const response = await fetch('/api/leagues/all');
      if (!response.ok) throw new Error('Failed to fetch leagues');
      return response.json() as Promise<League[]>;
    },
  });

  // Real-time filtering based on search query and selected sport
  const filteredLeagues = useMemo(() => {
    let filtered = allLeagues;

    // Filter by sport category
    if (selectedSport !== 'all') {
      filtered = filtered.filter(league => league.strSport === selectedSport);
    }

    // Filter by search query
    if (searchQuery.trim()) {
      const query = searchQuery.toLowerCase();
      filtered = filtered.filter(league =>
        league.strLeague.toLowerCase().includes(query) ||
        league.strLeagueAlternate?.toLowerCase().includes(query) ||
        league.strSport.toLowerCase().includes(query) ||
        league.strCountry?.toLowerCase().includes(query)
      );
    }

    return filtered;
  }, [allLeagues, selectedSport, searchQuery]);

  const addLeagueMutation = useMutation({
    mutationFn: async (league: League) => {
      const response = await fetch('/api/leagues', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          externalId: league.idLeague,
          name: league.strLeague,
          sport: league.strSport,
          country: league.strCountry,
          description: league.strDescriptionEN,
          monitored: true,
          logoUrl: league.strBadge || league.strLogo,
          bannerUrl: league.strBanner,
          posterUrl: league.strPoster,
          website: league.strWebsite,
          formedYear: league.intFormedYear ? parseInt(league.intFormedYear) : null,
        }),
      });

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to add league');
      }

      return response.json();
    },
    onSuccess: (data, variables) => {
      toast.success(`Added ${variables.strLeague} to your library!`);
      setAddedLeagues(prev => ({ ...prev, [variables.idLeague]: true }));
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    },
    onError: (error: Error) => {
      toast.error(error.message);
    },
  });

  const handleAddLeague = (league: League) => {
    addLeagueMutation.mutate(league);
  };

  return (
    <div className="p-8">
      <div className="max-w-7xl mx-auto">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-3xl font-bold text-white mb-2">Add League</h1>
          <p className="text-gray-400">
            Browse and add leagues from TheSportsDB to monitor their events
          </p>
        </div>

        {/* Search Controls */}
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6 mb-6">
          {/* Sport Filter */}
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-300 mb-2">
              Filter by Sport
            </label>
            <div className="flex flex-wrap gap-2">
              {SPORT_FILTERS.map(sport => (
                <button
                  key={sport.id}
                  onClick={() => setSelectedSport(sport.id)}
                  className={`px-4 py-2 rounded-lg font-medium transition-all ${
                    selectedSport === sport.id
                      ? 'bg-red-600 text-white shadow-lg shadow-red-900/30'
                      : 'bg-gray-800 text-gray-300 hover:bg-gray-700'
                  }`}
                >
                  <span className="mr-2">{sport.icon}</span>
                  {sport.name}
                </button>
              ))}
            </div>
          </div>

          {/* Search Input */}
          <div className="relative">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Filter leagues (e.g., UFC, Premier League, NBA)..."
              className="w-full pl-10 pr-4 py-3 bg-black border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
            />
          </div>

          <p className="text-sm text-gray-500 mt-3">
            💡 Showing {isLoading ? '...' : filteredLeagues.length} of {allLeagues.length} leagues
            {searchQuery && ` matching "${searchQuery}"`}
            {selectedSport !== 'all' && ` in ${SPORT_FILTERS.find(s => s.id === selectedSport)?.name}`}
          </p>
        </div>

        {/* Loading State */}
        {isLoading && (
          <div className="text-center py-16">
            <div className="animate-spin rounded-full h-16 w-16 border-b-2 border-red-600 mx-auto mb-4"></div>
            <h3 className="text-xl font-semibold text-gray-400 mb-2">
              Loading Leagues...
            </h3>
            <p className="text-gray-500">
              Fetching all available leagues from TheSportsDB
            </p>
          </div>
        )}

        {/* Search Results */}
        {!isLoading && filteredLeagues.length > 0 && (
          <div>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-xl font-semibold text-white">
                {selectedSport === 'all' ? 'All Leagues' : `${SPORT_FILTERS.find(s => s.id === selectedSport)?.name} Leagues`}
                {' '}({filteredLeagues.length})
              </h2>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {filteredLeagues.map(league => {
                const isAdded = addedLeagues[league.idLeague];
                const logoUrl = league.strBadge || league.strLogo;

                return (
                  <div
                    key={league.idLeague}
                    className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden hover:border-red-700/50 transition-all"
                  >
                    {/* League Badge/Logo */}
                    {logoUrl && (
                      <div className="h-48 bg-black/50 flex items-center justify-center p-6">
                        <img
                          src={logoUrl}
                          alt={league.strLeague}
                          className="max-h-full max-w-full object-contain"
                        />
                      </div>
                    )}

                    {/* League Info */}
                    <div className="p-4">
                      <div className="flex items-start justify-between mb-2">
                        <div className="flex-1">
                          <h3 className="text-lg font-bold text-white mb-1">
                            {league.strLeague}
                          </h3>
                          {league.strLeagueAlternate && (
                            <p className="text-sm text-gray-400 mb-2">
                              {league.strLeagueAlternate}
                            </p>
                          )}
                        </div>
                      </div>

                      {/* Sport Badge */}
                      <div className="flex items-center gap-2 mb-3">
                        <span className="px-2 py-1 bg-red-600/20 text-red-400 text-xs rounded font-medium">
                          {league.strSport}
                        </span>
                        {league.strCountry && (
                          <span className="flex items-center gap-1 text-xs text-gray-400">
                            <GlobeAltIcon className="w-3 h-3" />
                            {league.strCountry}
                          </span>
                        )}
                        {league.intFormedYear && (
                          <span className="text-xs text-gray-500">
                            Est. {league.intFormedYear}
                          </span>
                        )}
                      </div>

                      {/* Description */}
                      {league.strDescriptionEN && (
                        <p className="text-sm text-gray-400 mb-4 line-clamp-3">
                          {league.strDescriptionEN}
                        </p>
                      )}

                      {/* Add Button */}
                      <button
                        onClick={() => handleAddLeague(league)}
                        disabled={isAdded || addLeagueMutation.isPending}
                        className={`w-full py-2 rounded-lg font-medium transition-all ${
                          isAdded
                            ? 'bg-green-900/30 text-green-400 border border-green-700 cursor-not-allowed'
                            : 'bg-red-600 hover:bg-red-700 text-white'
                        } ${addLeagueMutation.isPending ? 'opacity-50 cursor-wait' : ''}`}
                      >
                        {isAdded ? (
                          <span className="flex items-center justify-center gap-2">
                            <CheckCircleIcon className="w-5 h-5" />
                            Added to Library
                          </span>
                        ) : addLeagueMutation.isPending ? (
                          'Adding...'
                        ) : (
                          <span className="flex items-center justify-center gap-2">
                            <TrophyIcon className="w-5 h-5" />
                            Add to Library
                          </span>
                        )}
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {/* Empty State */}
        {!isLoading && filteredLeagues.length === 0 && (
          <div className="text-center py-16">
            <TrophyIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
            <h3 className="text-xl font-semibold text-gray-400 mb-2">
              {searchQuery || selectedSport !== 'all'
                ? 'No Leagues Found'
                : 'No Leagues Available'}
            </h3>
            <p className="text-gray-500">
              {searchQuery || selectedSport !== 'all'
                ? 'Try adjusting your search or filter to see more results'
                : 'No leagues are available in the database'}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
