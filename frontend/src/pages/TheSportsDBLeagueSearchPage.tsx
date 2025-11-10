import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, GlobeAltIcon, TrophyIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';

// Sport categories for filtering
const SPORT_FILTERS = [
  { id: 'all', name: 'All Sports', icon: '🌍' },
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
  const [searchResults, setSearchResults] = useState<League[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [addedLeagues, setAddedLeagues] = useState<AddedLeague>({});
  const queryClient = useQueryClient();

  const handleSearch = async () => {
    if (!searchQuery.trim()) {
      toast.error('Please enter a search query');
      return;
    }

    setIsSearching(true);
    try {
      const response = await fetch(`/api/leagues/search/${encodeURIComponent(searchQuery)}`);
      if (!response.ok) throw new Error('Failed to search leagues');

      const results = await response.json() as League[];

      // Filter by sport if not "all"
      const filtered = selectedSport === 'all'
        ? results
        : results.filter(league => league.strSport === selectedSport);

      setSearchResults(filtered);

      if (filtered.length === 0) {
        toast.info('No leagues found matching your search');
      } else {
        toast.success(`Found ${filtered.length} league${filtered.length !== 1 ? 's' : ''}`);
      }
    } catch (error) {
      console.error('Search error:', error);
      toast.error('Failed to search leagues');
      setSearchResults([]);
    } finally {
      setIsSearching(false);
    }
  };

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

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleSearch();
    }
  };

  return (
    <div className="p-8">
      <div className="max-w-7xl mx-auto">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-3xl font-bold text-white mb-2">Add League</h1>
          <p className="text-gray-400">
            Search and add leagues from TheSportsDB to monitor their events
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
          <div className="flex gap-3">
            <div className="flex-1 relative">
              <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onKeyPress={handleKeyPress}
                placeholder="Search for leagues (e.g., UFC, Premier League, NBA)..."
                className="w-full pl-10 pr-4 py-3 bg-black border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
              />
            </div>
            <button
              onClick={handleSearch}
              disabled={isSearching || !searchQuery.trim()}
              className="px-6 py-3 bg-red-600 hover:bg-red-700 disabled:bg-gray-700 disabled:cursor-not-allowed text-white font-medium rounded-lg transition-colors"
            >
              {isSearching ? 'Searching...' : 'Search'}
            </button>
          </div>

          <p className="text-sm text-gray-500 mt-3">
            💡 Try searching for: "UFC", "Premier League", "NBA", "NFL", "Formula 1", "Champions League"
          </p>
        </div>

        {/* Search Results */}
        {searchResults.length > 0 && (
          <div>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-xl font-semibold text-white">
                Search Results ({searchResults.length})
              </h2>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {searchResults.map(league => {
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
        {searchResults.length === 0 && !isSearching && (
          <div className="text-center py-16">
            <TrophyIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
            <h3 className="text-xl font-semibold text-gray-400 mb-2">
              Search for Leagues
            </h3>
            <p className="text-gray-500">
              Enter a league name to search TheSportsDB and add it to your library
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
