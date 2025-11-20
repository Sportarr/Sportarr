import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, GlobeAltIcon, TrophyIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import AddLeagueModal from '../components/AddLeagueModal';
import ConfirmationModal from '../components/ConfirmationModal';

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

interface AddedLeagueInfo {
  id: number;
  externalId: string;
}

export default function TheSportsDBLeagueSearchPage() {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [leagueToAdd, setLeagueToAdd] = useState<League | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editMode, setEditMode] = useState(false);
  const [editingLeagueId, setEditingLeagueId] = useState<number | null>(null);
  const [hoveredLeagueId, setHoveredLeagueId] = useState<string | null>(null);
  const [deleteConfirmation, setDeleteConfirmation] = useState<{ isOpen: boolean; leagueId: number; leagueName: string; eventCount: number }>({
    isOpen: false,
    leagueId: 0,
    leagueName: '',
    eventCount: 0,
  });
  const queryClient = useQueryClient();

  // Fetch all leagues from TheSportsDB
  const { data: allLeagues = [], isLoading } = useQuery({
    queryKey: ['thesportsdb-leagues', 'all'],
    queryFn: async () => {
      const response = await fetch('/api/leagues/all');
      if (!response.ok) throw new Error('Failed to fetch leagues');
      return response.json() as Promise<League[]>;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes - data doesn't change often
    refetchOnWindowFocus: false, // Don't refetch on tab focus
  });

  // Fetch user's added leagues to check which ones are already in library
  const { data: userLeagues = [] } = useQuery({
    queryKey: ['leagues'],
    queryFn: async () => {
      const response = await fetch('/api/leagues');
      if (!response.ok) throw new Error('Failed to fetch user leagues');
      return response.json();
    },
  });

  // Create a map of added leagues by external ID
  const addedLeaguesMap = useMemo(() => {
    const map = new Map<string, AddedLeagueInfo>();
    userLeagues.forEach((league: any) => {
      if (league.externalId) {
        map.set(league.externalId, { id: league.id, externalId: league.externalId });
      }
    });
    return map;
  }, [userLeagues]);

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
    mutationFn: async ({
      league,
      monitoredTeamIds,
      monitorType,
      qualityProfileId,
      searchForMissingEvents,
      searchForCutoffUnmetEvents,
      monitoredParts
    }: {
      league: League;
      monitoredTeamIds: string[];
      monitorType: string;
      qualityProfileId: number | null;
      searchForMissingEvents: boolean;
      searchForCutoffUnmetEvents: boolean;
      monitoredParts: string | null;
    }) => {
      const response = await fetch('/api/leagues', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          externalId: league.idLeague,
          name: league.strLeague,
          sport: league.strSport,
          country: league.strCountry,
          description: league.strDescriptionEN,
          monitored: monitoredTeamIds.length > 0, // Only monitor if teams are selected
          monitorType: monitorType,
          qualityProfileId: qualityProfileId,
          searchForMissingEvents: searchForMissingEvents,
          searchForCutoffUnmetEvents: searchForCutoffUnmetEvents,
          monitoredParts: monitoredParts,
          logoUrl: league.strBadge || league.strLogo,
          bannerUrl: league.strBanner,
          posterUrl: league.strPoster,
          website: league.strWebsite,
          formedYear: league.intFormedYear || null,
          monitoredTeamIds: monitoredTeamIds.length > 0 ? monitoredTeamIds : null,
        }),
      });

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to add league');
      }

      return response.json();
    },
    onSuccess: (data, variables) => {
      const teamCount = variables.monitoredTeamIds.length;
      const message = teamCount > 0
        ? `Added ${variables.league.strLeague} with ${teamCount} monitored team${teamCount !== 1 ? 's' : ''}!`
        : `Added ${variables.league.strLeague} (not monitored - no teams selected)`;

      toast.success(message);
      setIsModalOpen(false);
      setLeagueToAdd(null);
      setEditMode(false);
      setEditingLeagueId(null);
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
      queryClient.invalidateQueries({ queryKey: ['thesportsdb-leagues'] });
    },
    onError: (error: Error) => {
      toast.error(error.message);
    },
  });

  const updateLeagueSettingsMutation = useMutation({
    mutationFn: async ({
      leagueId,
      monitoredTeamIds,
      monitorType,
      qualityProfileId,
      searchForMissingEvents,
      searchForCutoffUnmetEvents,
      monitoredParts
    }: {
      leagueId: number;
      monitoredTeamIds: string[];
      monitorType: string;
      qualityProfileId: number | null;
      searchForMissingEvents: boolean;
      searchForCutoffUnmetEvents: boolean;
      monitoredParts: string | null;
    }) => {
      // First update the league settings
      const settingsResponse = await fetch(`/api/leagues/${leagueId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          monitored: monitoredTeamIds.length > 0,
          monitorType: monitorType,
          qualityProfileId: qualityProfileId,
          searchForMissingEvents: searchForMissingEvents,
          searchForCutoffUnmetEvents: searchForCutoffUnmetEvents,
          monitoredParts: monitoredParts,
        }),
      });

      if (!settingsResponse.ok) {
        const error = await settingsResponse.json();
        throw new Error(error.error || 'Failed to update league settings');
      }

      // Then update the monitored teams
      const teamsResponse = await fetch(`/api/leagues/${leagueId}/teams`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          monitoredTeamIds: monitoredTeamIds.length > 0 ? monitoredTeamIds : null,
        }),
      });

      if (!teamsResponse.ok) {
        const error = await teamsResponse.json();
        throw new Error(error.error || 'Failed to update monitored teams');
      }

      return teamsResponse.json();
    },
    onSuccess: (data) => {
      const teamCount = data.teamCount || 0;
      const message = teamCount > 0
        ? `Updated league settings and ${teamCount} monitored team${teamCount !== 1 ? 's' : ''}`
        : 'League settings updated (no teams selected)';

      toast.success(message);
      setIsModalOpen(false);
      setLeagueToAdd(null);
      setEditMode(false);
      setEditingLeagueId(null);
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
      queryClient.invalidateQueries({ queryKey: ['league', editingLeagueId] });
      queryClient.invalidateQueries({ queryKey: ['league-events', editingLeagueId] });
    },
    onError: (error: Error) => {
      toast.error(error.message);
    },
  });

  const deleteLeagueMutation = useMutation({
    mutationFn: async (leagueId: number) => {
      const response = await fetch(`/api/leagues/${leagueId}`, {
        method: 'DELETE',
      });

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to delete league');
      }

      return response.json();
    },
    onSuccess: () => {
      toast.success('League removed from library');
      setDeleteConfirmation({ isOpen: false, leagueId: 0, leagueName: '', eventCount: 0 });
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    },
    onError: (error: Error) => {
      toast.error(error.message);
    },
  });

  const handleOpenModal = (league: League) => {
    setLeagueToAdd(league);
    setIsModalOpen(true);
    setEditMode(false);
    setEditingLeagueId(null);
  };

  const handleEditTeams = (league: League, leagueId: number) => {
    setLeagueToAdd(league);
    setIsModalOpen(true);
    setEditMode(true);
    setEditingLeagueId(leagueId);
  };

  const handleAddLeague = (
    league: League,
    monitoredTeamIds: string[],
    monitorType: string,
    qualityProfileId: number | null,
    searchForMissingEvents: boolean,
    searchForCutoffUnmetEvents: boolean,
    monitoredParts: string | null
  ) => {
    if (editMode && editingLeagueId) {
      updateLeagueSettingsMutation.mutate({
        leagueId: editingLeagueId,
        monitoredTeamIds,
        monitorType,
        qualityProfileId,
        searchForMissingEvents,
        searchForCutoffUnmetEvents,
        monitoredParts
      });
    } else {
      addLeagueMutation.mutate({
        league,
        monitoredTeamIds,
        monitorType,
        qualityProfileId,
        searchForMissingEvents,
        searchForCutoffUnmetEvents,
        monitoredParts
      });
    }
  };

  const handleCloseModal = () => {
    if (!addLeagueMutation.isPending && !updateLeagueSettingsMutation.isPending) {
      setIsModalOpen(false);
      setLeagueToAdd(null);
      setEditMode(false);
      setEditingLeagueId(null);
    }
  };

  const handleOpenDeleteConfirmation = (leagueId: number, leagueName: string) => {
    // Fetch event count for the league
    const userLeague = userLeagues.find((l: any) => l.id === leagueId);
    const eventCount = userLeague?.eventCount || 0;

    setDeleteConfirmation({
      isOpen: true,
      leagueId,
      leagueName,
      eventCount,
    });
  };

  const handleConfirmDelete = () => {
    deleteLeagueMutation.mutate(deleteConfirmation.leagueId);
  };

  const handleCardClick = (league: League, isAdded: boolean, addedLeagueInfo?: AddedLeagueInfo) => {
    if (isAdded && addedLeagueInfo) {
      // Navigate to league detail page
      navigate(`/leagues/${addedLeagueInfo.id}`);
    } else {
      // Open add modal
      handleOpenModal(league);
    }
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
                const addedLeagueInfo = addedLeaguesMap.get(league.idLeague);
                const isAdded = !!addedLeagueInfo;
                const logoUrl = league.strBadge || league.strLogo;

                return (
                  <div
                    key={league.idLeague}
                    onClick={() => handleCardClick(league, isAdded, addedLeagueInfo)}
                    className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden hover:border-red-700/50 transition-all cursor-pointer"
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

                      {/* Buttons */}
                      {isAdded ? (
                        <div className="flex gap-2">
                          <button
                            onMouseEnter={() => setHoveredLeagueId(league.idLeague)}
                            onMouseLeave={() => setHoveredLeagueId(null)}
                            onClick={(e) => {
                              e.stopPropagation();
                              addedLeagueInfo && handleOpenDeleteConfirmation(addedLeagueInfo.id, league.strLeague);
                            }}
                            className={`flex-1 py-2 rounded-lg font-medium border transition-all flex items-center justify-center gap-2 ${
                              hoveredLeagueId === league.idLeague
                                ? 'bg-red-900/40 text-red-300 border-red-700 hover:bg-red-900/60'
                                : 'bg-green-900/30 text-green-400 border-green-700'
                            }`}
                          >
                            <CheckCircleIcon className="w-5 h-5" />
                            {hoveredLeagueId === league.idLeague ? 'Remove from Library' : 'Added to Library'}
                          </button>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              addedLeagueInfo && handleEditTeams(league, addedLeagueInfo.id);
                            }}
                            className="px-4 py-2 rounded-lg font-medium bg-blue-600 hover:bg-blue-700 text-white transition-colors"
                          >
                            Edit
                          </button>
                        </div>
                      ) : (
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            handleOpenModal(league);
                          }}
                          className="w-full py-2 rounded-lg font-medium transition-all bg-red-600 hover:bg-red-700 text-white"
                        >
                          <span className="flex items-center justify-center gap-2">
                            <TrophyIcon className="w-5 h-5" />
                            Add to Library
                          </span>
                        </button>
                      )}
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

      {/* Add League Modal */}
      <AddLeagueModal
        league={leagueToAdd}
        isOpen={isModalOpen}
        onClose={handleCloseModal}
        onAdd={handleAddLeague}
        isAdding={addLeagueMutation.isPending || updateLeagueSettingsMutation.isPending}
        editMode={editMode}
        leagueId={editingLeagueId}
      />

      {/* Delete Confirmation Modal */}
      <ConfirmationModal
        isOpen={deleteConfirmation.isOpen}
        onClose={() => setDeleteConfirmation({ isOpen: false, leagueId: 0, leagueName: '', eventCount: 0 })}
        onConfirm={handleConfirmDelete}
        title="Remove League from Library"
        message={`Are you sure you want to remove "${deleteConfirmation.leagueName}" from your library?${
          deleteConfirmation.eventCount > 0
            ? ` This will remove the league and all ${deleteConfirmation.eventCount} event${deleteConfirmation.eventCount !== 1 ? 's' : ''}.`
            : ''
        }`}
        confirmText="Remove League"
        confirmButtonClass="bg-red-600 hover:bg-red-700"
        isLoading={deleteLeagueMutation.isPending}
      />
    </div>
  );
}
