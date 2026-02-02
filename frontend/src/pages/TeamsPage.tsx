import { useState, useMemo, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  MagnifyingGlassIcon,
  GlobeAltIcon,
  UserGroupIcon,
  CheckCircleIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  PlusIcon,
  ArrowPathIcon,
  CheckIcon,
  TrashIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../api/client';
import type { Team, FollowedTeam, DiscoveredLeague, QualityProfile } from '../types';

// Supported sports for team following
const SPORT_FILTERS = [
  { id: 'all', name: 'All Sports', icon: '🌍' },
  { id: 'Soccer', name: 'Soccer', icon: '⚽' },
  { id: 'Basketball', name: 'Basketball', icon: '🏀' },
  { id: 'Ice Hockey', name: 'Ice Hockey', icon: '🏒' },
];

// Sport icons for display
const SPORT_ICONS: Record<string, string> = {
  'Soccer': '⚽',
  'Football': '⚽',
  'Basketball': '🏀',
  'Ice Hockey': '🏒',
  'Hockey': '🏒',
};

// Monitor type options
const MONITOR_OPTIONS = [
  { value: 'Future', label: 'Future Events', description: 'Only monitor upcoming events' },
  { value: 'All', label: 'All Events', description: 'Monitor past and future events' },
  { value: 'None', label: 'None', description: 'Do not monitor events automatically' },
];

// Helper to get sport icon
const getSportIcon = (sport: string): string => {
  const sportLower = sport.toLowerCase();
  if (sportLower.includes('soccer') || sportLower.includes('football')) return '⚽';
  if (sportLower.includes('basketball')) return '🏀';
  if (sportLower.includes('hockey')) return '🏒';
  return '🏅';
};

export default function TeamsPage() {
  const queryClient = useQueryClient();
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [expandedTeamId, setExpandedTeamId] = useState<string | null>(null);
  const [discoveredLeagues, setDiscoveredLeagues] = useState<DiscoveredLeague[]>([]);
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [selectedLeagueIds, setSelectedLeagueIds] = useState<Set<string>>(new Set());

  // League add settings
  const [monitorType, setMonitorType] = useState('Future');
  const [qualityProfileId, setQualityProfileId] = useState<number>(1);
  const [searchOnAdd, setSearchOnAdd] = useState(false);
  const [searchForUpgrades, setSearchForUpgrades] = useState(false);
  const [isAddingLeagues, setIsAddingLeagues] = useState(false);

  // Fetch all teams for supported sports
  const { data: allTeams = [], isLoading: isLoadingTeams } = useQuery({
    queryKey: ['all-teams'],
    queryFn: async () => {
      const response = await apiClient.get<Team[]>('/teams/all');
      return response.data || [];
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    refetchOnWindowFocus: false,
  });

  // Fetch followed teams to check which ones are already followed
  const { data: followedTeams = [] } = useQuery({
    queryKey: ['followed-teams'],
    queryFn: async () => {
      const response = await apiClient.get<FollowedTeam[]>('/followed-teams');
      return response.data || [];
    },
  });

  // Fetch quality profiles for the dropdown
  const { data: qualityProfiles } = useQuery({
    queryKey: ['quality-profiles'],
    queryFn: async () => {
      const response = await apiClient.get<QualityProfile[]>('/settings/quality-profiles');
      return response.data;
    },
  });

  // Create a set of followed team external IDs for quick lookup
  const followedTeamIds = useMemo(() => {
    const ids = new Set<string>();
    followedTeams.forEach(ft => {
      if (ft.externalId) ids.add(ft.externalId);
    });
    return ids;
  }, [followedTeams]);

  // Filter teams based on selected sport and search query
  const filteredTeams = useMemo(() => {
    let filtered = allTeams;

    // Filter by sport
    if (selectedSport !== 'all') {
      filtered = filtered.filter(team =>
        team.sport?.toLowerCase().includes(selectedSport.toLowerCase())
      );
    }

    // Filter by search query
    if (searchQuery.trim()) {
      const query = searchQuery.toLowerCase();
      filtered = filtered.filter(team =>
        team.name?.toLowerCase().includes(query) ||
        team.shortName?.toLowerCase().includes(query) ||
        team.alternateName?.toLowerCase().includes(query) ||
        team.country?.toLowerCase().includes(query)
      );
    }

    return filtered;
  }, [allTeams, selectedSport, searchQuery]);

  // Follow team mutation
  const followTeamMutation = useMutation({
    mutationFn: async (team: Team) => {
      return apiClient.post<FollowedTeam>('/followed-teams', {
        externalId: team.externalId,
        name: team.name,
        sport: team.sport,
        badgeUrl: team.badgeUrl,
      });
    },
    onSuccess: async (response, team) => {
      // Invalidate and wait for refetch before discovering leagues
      await queryClient.invalidateQueries({ queryKey: ['followed-teams'] });
      toast.success(`Now following ${team.name}`);
      // Expand the team card to show discovered leagues
      if (team.externalId && response.data?.id) {
        setExpandedTeamId(team.externalId);
        // Use the returned followed team ID directly to avoid race condition
        discoverLeaguesById(response.data.id, team.externalId);
      }
    },
    onError: (error: Error) => {
      toast.error('Failed to follow team', { description: error.message });
    },
  });

  // Unfollow team mutation
  const unfollowTeamMutation = useMutation({
    mutationFn: async (teamId: number) => {
      return apiClient.delete(`/followed-teams/${teamId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['followed-teams'] });
      toast.success('Team unfollowed');
      setExpandedTeamId(null);
      setDiscoveredLeagues([]);
    },
    onError: (error: Error) => {
      toast.error('Failed to unfollow team', { description: error.message });
    },
  });

  // Discover leagues for a team by followed team ID (avoids race condition)
  const discoverLeaguesById = async (followedTeamId: number, teamExternalId: string) => {
    setIsDiscovering(true);
    setDiscoveredLeagues([]);
    setSelectedLeagueIds(new Set());

    try {
      const response = await apiClient.get<{
        teamId: number;
        teamName: string;
        leagues: DiscoveredLeague[];
      }>(`/followed-teams/${followedTeamId}/leagues`);

      setDiscoveredLeagues(response.data.leagues || []);

      // Auto-select leagues that aren't already added
      const notAddedIds = new Set(
        (response.data.leagues || [])
          .filter(l => !l.isAdded)
          .map(l => l.externalId)
      );
      setSelectedLeagueIds(notAddedIds);
    } catch {
      toast.error('Failed to discover leagues');
    } finally {
      setIsDiscovering(false);
    }
  };

  // Discover leagues for a team (looks up followed team by external ID)
  const discoverLeagues = async (teamExternalId: string) => {
    const followedTeam = followedTeams.find(ft => ft.externalId === teamExternalId);
    if (!followedTeam) {
      toast.error('Team not found in followed teams');
      return;
    }
    await discoverLeaguesById(followedTeam.id, teamExternalId);
  };

  // Toggle team expansion
  const toggleTeamExpansion = (team: Team) => {
    if (!team.externalId) return;

    if (expandedTeamId === team.externalId) {
      setExpandedTeamId(null);
      setDiscoveredLeagues([]);
      setSelectedLeagueIds(new Set());
    } else {
      const isFollowed = followedTeamIds.has(team.externalId);
      if (isFollowed) {
        setExpandedTeamId(team.externalId);
        discoverLeagues(team.externalId);
      }
    }
  };

  // Add selected leagues
  const handleAddLeagues = async (teamExternalId: string) => {
    if (selectedLeagueIds.size === 0) {
      toast.error('No leagues selected');
      return;
    }

    const followedTeam = followedTeams.find(ft => ft.externalId === teamExternalId);
    if (!followedTeam) {
      toast.error('Team not found');
      return;
    }

    setIsAddingLeagues(true);
    try {
      const response = await apiClient.post(`/followed-teams/${followedTeam.id}/add-leagues`, {
        leagueExternalIds: Array.from(selectedLeagueIds),
        monitorType,
        qualityProfileId,
        searchOnAdd,
        searchForUpgrades,
      });

      const { added, skipped, errors } = response.data;

      if (added?.length > 0) {
        toast.success(`Added ${added.length} league(s)`, {
          description: added.map((l: { name: string }) => l.name).join(', '),
        });
      }
      if (skipped?.length > 0) {
        toast.info(`Skipped ${skipped.length} league(s)`, {
          description: skipped.map((l: { name: string; reason: string }) => `${l.name}: ${l.reason}`).join(', '),
        });
      }
      if (errors?.length > 0) {
        toast.error(`Failed to add ${errors.length} league(s)`, {
          description: errors.map((l: { reason: string }) => l.reason).join(', '),
        });
      }

      // Refresh the discovered leagues to update isAdded status
      discoverLeagues(teamExternalId);
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch {
      toast.error('Failed to add leagues');
    } finally {
      setIsAddingLeagues(false);
    }
  };

  // Toggle league selection
  const toggleLeagueSelection = (leagueId: string) => {
    setSelectedLeagueIds(prev => {
      const next = new Set(prev);
      if (next.has(leagueId)) {
        next.delete(leagueId);
      } else {
        next.add(leagueId);
      }
      return next;
    });
  };

  // Select/deselect all leagues
  const toggleSelectAll = () => {
    const notAddedLeagues = discoveredLeagues.filter(l => !l.isAdded);
    if (selectedLeagueIds.size === notAddedLeagues.length) {
      setSelectedLeagueIds(new Set());
    } else {
      setSelectedLeagueIds(new Set(notAddedLeagues.map(l => l.externalId)));
    }
  };

  // Get followed team by external ID
  const getFollowedTeam = (externalId: string) => {
    return followedTeams.find(ft => ft.externalId === externalId);
  };

  return (
    <div className="p-8">
      <div className="max-w-7xl mx-auto">
        {/* Header */}
        <div className="mb-6">
          <h1 className="text-3xl font-bold text-white mb-2">Add Team</h1>
          <p className="text-gray-400">
            Follow teams across multiple leagues. When you follow a team, you can add all their leagues at once.
          </p>
        </div>

        {/* Supported Sports Notice */}
        <div className="bg-gradient-to-r from-blue-900/30 to-purple-900/30 border border-blue-700/30 rounded-lg p-4 mb-6">
          <p className="text-sm text-gray-300">
            <span className="font-semibold text-white">Follow Team</span> is currently available for{' '}
            <span className="text-blue-400">Soccer</span>,{' '}
            <span className="text-orange-400">Basketball</span>, and{' '}
            <span className="text-cyan-400">Ice Hockey</span>.
            {' '}Want support for other sports?{' '}
            <a
              href="https://github.com/Sportarr/Sportarr/issues"
              target="_blank"
              rel="noopener noreferrer"
              className="text-red-400 hover:text-red-300 underline"
            >
              Open a GitHub issue
            </a>
            {' '}or ask on{' '}
            <a
              href="https://discord.gg/YjHVWGWjjG"
              target="_blank"
              rel="noopener noreferrer"
              className="text-indigo-400 hover:text-indigo-300 underline"
            >
              Discord
            </a>.
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
              placeholder="Filter teams (e.g., Real Madrid, Lakers, Bruins)..."
              className="w-full pl-10 pr-4 py-3 bg-black border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
            />
          </div>

          <p className="text-sm text-gray-500 mt-3">
            💡 Showing {isLoadingTeams ? '...' : filteredTeams.length} of {allTeams.length} teams
            {searchQuery && ` matching "${searchQuery}"`}
            {selectedSport !== 'all' && ` in ${SPORT_FILTERS.find(s => s.id === selectedSport)?.name}`}
          </p>
        </div>

        {/* Loading State */}
        {isLoadingTeams && (
          <div className="text-center py-16">
            <div className="animate-spin rounded-full h-16 w-16 border-b-2 border-red-600 mx-auto mb-4"></div>
            <h3 className="text-xl font-semibold text-gray-400 mb-2">
              Loading Teams...
            </h3>
            <p className="text-gray-500">
              Fetching all teams for supported sports from TheSportsDB
            </p>
          </div>
        )}

        {/* Teams Grid */}
        {!isLoadingTeams && filteredTeams.length > 0 && (
          <div>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-xl font-semibold text-white">
                {selectedSport === 'all' ? 'All Teams' : `${SPORT_FILTERS.find(s => s.id === selectedSport)?.name} Teams`}
                {' '}({filteredTeams.length})
              </h2>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {filteredTeams.map(team => {
                const isFollowed = team.externalId ? followedTeamIds.has(team.externalId) : false;
                const isExpanded = expandedTeamId === team.externalId;
                const followedTeam = team.externalId ? getFollowedTeam(team.externalId) : null;

                return (
                  <div
                    key={team.externalId || team.id}
                    className={`bg-gradient-to-br from-gray-900 to-black border rounded-lg overflow-hidden transition-all ${
                      isExpanded
                        ? 'border-red-600 col-span-1 md:col-span-2 lg:col-span-3'
                        : 'border-red-900/30 hover:border-red-700/50'
                    }`}
                  >
                    {/* Team Card */}
                    <div className="flex items-center p-4">
                      {/* Team Badge */}
                      <div className="h-16 w-16 bg-black/50 flex items-center justify-center rounded-lg mr-4 flex-shrink-0">
                        {team.badgeUrl ? (
                          <img
                            src={team.badgeUrl}
                            alt={team.name}
                            className="max-h-full max-w-full object-contain"
                          />
                        ) : (
                          <span className="text-3xl opacity-50">
                            {getSportIcon(team.sport || '')}
                          </span>
                        )}
                      </div>

                      {/* Team Info */}
                      <div className="flex-1 min-w-0">
                        <h3 className="text-lg font-bold text-white truncate">
                          {team.name}
                        </h3>
                        {team.alternateName && (
                          <p className="text-sm text-gray-400 truncate">
                            {team.alternateName}
                          </p>
                        )}
                        <div className="flex items-center gap-2 mt-1">
                          <span className="px-2 py-0.5 bg-red-600/20 text-red-400 text-xs rounded font-medium">
                            {team.sport}
                          </span>
                          {team.country && (
                            <span className="flex items-center gap-1 text-xs text-gray-400">
                              <GlobeAltIcon className="w-3 h-3" />
                              {team.country}
                            </span>
                          )}
                        </div>
                      </div>

                      {/* Action Buttons */}
                      <div className="flex items-center gap-2 ml-4">
                        {isFollowed ? (
                          <>
                            <button
                              onClick={() => toggleTeamExpansion(team)}
                              className="px-4 py-2 rounded-lg font-medium bg-green-900/30 text-green-400 border border-green-700 hover:bg-green-900/50 transition-colors flex items-center gap-2"
                            >
                              <CheckCircleIcon className="w-5 h-5" />
                              Following
                              {isExpanded ? (
                                <ChevronUpIcon className="w-4 h-4" />
                              ) : (
                                <ChevronDownIcon className="w-4 h-4" />
                              )}
                            </button>
                            <button
                              onClick={() => {
                                if (followedTeam && confirm(`Unfollow ${team.name}?`)) {
                                  unfollowTeamMutation.mutate(followedTeam.id);
                                }
                              }}
                              className="p-2 text-gray-400 hover:text-red-400 transition-colors"
                              title="Unfollow team"
                            >
                              <TrashIcon className="w-5 h-5" />
                            </button>
                          </>
                        ) : (
                          <button
                            onClick={() => followTeamMutation.mutate(team)}
                            disabled={followTeamMutation.isPending}
                            className="px-4 py-2 rounded-lg font-medium bg-red-600 hover:bg-red-700 text-white transition-colors flex items-center gap-2"
                          >
                            {followTeamMutation.isPending ? (
                              <ArrowPathIcon className="w-5 h-5 animate-spin" />
                            ) : (
                              <UserGroupIcon className="w-5 h-5" />
                            )}
                            Follow
                          </button>
                        )}
                      </div>
                    </div>

                    {/* Expanded Leagues Section */}
                    {isExpanded && (
                      <div className="border-t border-gray-800 p-4 bg-gray-950/50">
                        {isDiscovering ? (
                          <div className="text-center py-8 text-gray-400">
                            <ArrowPathIcon className="w-8 h-8 animate-spin mx-auto mb-2" />
                            Discovering leagues...
                          </div>
                        ) : discoveredLeagues.length === 0 ? (
                          <div className="text-center py-8 text-gray-400">
                            No leagues found for this team
                          </div>
                        ) : (
                          <>
                            {/* League Settings */}
                            <div className="bg-gray-900/50 border border-gray-800 rounded-lg p-4 mb-4">
                              <h4 className="font-medium text-white mb-3">League Settings (applied to all selected)</h4>
                              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                                {/* Monitor Type */}
                                <div>
                                  <label className="block text-sm text-gray-400 mb-1">Monitor Events</label>
                                  <select
                                    value={monitorType}
                                    onChange={(e) => setMonitorType(e.target.value)}
                                    className="w-full bg-gray-800 border border-gray-700 rounded px-3 py-2 text-white"
                                  >
                                    {MONITOR_OPTIONS.map(opt => (
                                      <option key={opt.value} value={opt.value}>{opt.label}</option>
                                    ))}
                                  </select>
                                </div>

                                {/* Quality Profile */}
                                <div>
                                  <label className="block text-sm text-gray-400 mb-1">Quality Profile</label>
                                  <select
                                    value={qualityProfileId}
                                    onChange={(e) => setQualityProfileId(Number(e.target.value))}
                                    className="w-full bg-gray-800 border border-gray-700 rounded px-3 py-2 text-white"
                                  >
                                    {qualityProfiles?.map(qp => (
                                      <option key={qp.id} value={qp.id}>{qp.name}</option>
                                    ))}
                                  </select>
                                </div>

                                {/* Search on Add */}
                                <div className="flex items-center gap-2">
                                  <input
                                    type="checkbox"
                                    id="searchOnAdd"
                                    checked={searchOnAdd}
                                    onChange={(e) => setSearchOnAdd(e.target.checked)}
                                    className="w-4 h-4 rounded border-gray-600 text-red-600 focus:ring-red-500 bg-gray-800"
                                  />
                                  <label htmlFor="searchOnAdd" className="text-sm text-gray-300">
                                    Search for missing events
                                  </label>
                                </div>

                                {/* Search for Upgrades */}
                                <div className="flex items-center gap-2">
                                  <input
                                    type="checkbox"
                                    id="searchForUpgrades"
                                    checked={searchForUpgrades}
                                    onChange={(e) => setSearchForUpgrades(e.target.checked)}
                                    className="w-4 h-4 rounded border-gray-600 text-red-600 focus:ring-red-500 bg-gray-800"
                                  />
                                  <label htmlFor="searchForUpgrades" className="text-sm text-gray-300">
                                    Search for quality upgrades
                                  </label>
                                </div>
                              </div>
                            </div>

                            {/* League Selection */}
                            <div className="flex items-center justify-between mb-3">
                              <div className="flex items-center gap-4">
                                <button
                                  onClick={toggleSelectAll}
                                  className="text-sm text-blue-400 hover:text-blue-300"
                                >
                                  {selectedLeagueIds.size === discoveredLeagues.filter(l => !l.isAdded).length
                                    ? 'Deselect All'
                                    : 'Select All'}
                                </button>
                                <span className="text-sm text-gray-400">
                                  {selectedLeagueIds.size} league(s) selected
                                </span>
                              </div>
                              <button
                                onClick={() => team.externalId && handleAddLeagues(team.externalId)}
                                disabled={selectedLeagueIds.size === 0 || isAddingLeagues}
                                className="px-4 py-2 bg-green-600 hover:bg-green-700 disabled:bg-gray-600 text-white rounded-lg text-sm font-medium transition-colors flex items-center gap-2"
                              >
                                {isAddingLeagues ? (
                                  <ArrowPathIcon className="w-4 h-4 animate-spin" />
                                ) : (
                                  <PlusIcon className="w-4 h-4" />
                                )}
                                Add Selected Leagues ({selectedLeagueIds.size})
                              </button>
                            </div>

                            {/* Leagues List */}
                            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                              {discoveredLeagues.map((league) => (
                                <div
                                  key={league.externalId}
                                  onClick={() => !league.isAdded && toggleLeagueSelection(league.externalId)}
                                  className={`p-3 rounded-lg border transition-colors cursor-pointer ${
                                    league.isAdded
                                      ? 'bg-green-900/20 border-green-800/50 cursor-not-allowed'
                                      : selectedLeagueIds.has(league.externalId)
                                      ? 'bg-blue-900/30 border-blue-600'
                                      : 'bg-gray-900/50 border-gray-700 hover:border-gray-600'
                                  }`}
                                >
                                  <div className="flex items-center gap-3">
                                    {/* Checkbox */}
                                    <div className={`w-5 h-5 rounded border flex items-center justify-center ${
                                      league.isAdded
                                        ? 'bg-green-600 border-green-600'
                                        : selectedLeagueIds.has(league.externalId)
                                        ? 'bg-blue-600 border-blue-600'
                                        : 'border-gray-600'
                                    }`}>
                                      {(league.isAdded || selectedLeagueIds.has(league.externalId)) && (
                                        <CheckIcon className="w-3 h-3 text-white" />
                                      )}
                                    </div>

                                    {/* League Badge */}
                                    {league.badgeUrl ? (
                                      <img
                                        src={league.badgeUrl}
                                        alt={league.name}
                                        className="w-8 h-8 object-contain rounded"
                                      />
                                    ) : (
                                      <div className="w-8 h-8 bg-gray-800 rounded flex items-center justify-center text-lg">
                                        {getSportIcon(league.sport)}
                                      </div>
                                    )}

                                    {/* League Info */}
                                    <div className="flex-1 min-w-0">
                                      <p className="font-medium text-white truncate">{league.name}</p>
                                      <p className="text-xs text-gray-400">
                                        {league.country || league.sport} • {league.eventCount} events
                                      </p>
                                    </div>

                                    {/* Status Badge */}
                                    {league.isAdded && (
                                      <span className="px-2 py-0.5 bg-green-900/50 text-green-400 text-xs rounded">
                                        Already Added
                                      </span>
                                    )}
                                  </div>
                                </div>
                              ))}
                            </div>
                          </>
                        )}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {/* Empty State */}
        {!isLoadingTeams && filteredTeams.length === 0 && (
          <div className="text-center py-16">
            <UserGroupIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
            <h3 className="text-xl font-semibold text-gray-400 mb-2">
              {searchQuery || selectedSport !== 'all'
                ? 'No Teams Found'
                : 'No Teams Available'}
            </h3>
            <p className="text-gray-500">
              {searchQuery || selectedSport !== 'all'
                ? 'Try adjusting your search or filter to see more results'
                : 'No teams are available for the supported sports'}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
