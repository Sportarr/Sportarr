import { useState, useEffect } from 'react';
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query';
import {
  MagnifyingGlassIcon,
  UserGroupIcon,
  TrashIcon,
  ArrowPathIcon,
  PlusIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  CheckIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../api/client';
import type { Team, FollowedTeam, DiscoveredLeague, QualityProfile } from '../types';

// Supported sports for team following
const SUPPORTED_SPORTS = ['Soccer', 'Basketball', 'Ice Hockey'];

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
  { value: 'future', label: 'Future Events', description: 'Only monitor upcoming events' },
  { value: 'all', label: 'All Events', description: 'Monitor past and future events' },
  { value: 'none', label: 'None', description: 'Do not monitor events automatically' },
];

export default function TeamsPage() {
  const queryClient = useQueryClient();
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<Team[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [expandedTeamId, setExpandedTeamId] = useState<number | null>(null);
  const [discoveredLeagues, setDiscoveredLeagues] = useState<DiscoveredLeague[]>([]);
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [selectedLeagueIds, setSelectedLeagueIds] = useState<Set<string>>(new Set());

  // League add settings
  const [monitorEvents, setMonitorEvents] = useState(true);
  const [qualityProfileId, setQualityProfileId] = useState<number>(1);
  const [searchOnAdd, setSearchOnAdd] = useState(false);
  const [searchForUpgrades, setSearchForUpgrades] = useState(false);
  const [isAddingLeagues, setIsAddingLeagues] = useState(false);

  // Fetch followed teams
  const { data: followedTeams, isLoading: isLoadingTeams, refetch: refetchTeams } = useQuery({
    queryKey: ['followed-teams'],
    queryFn: async () => {
      const response = await apiClient.get<FollowedTeam[]>('/followed-teams');
      return response.data;
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

  // Follow team mutation
  const followTeamMutation = useMutation({
    mutationFn: async (team: Team) => {
      return apiClient.post('/followed-teams', {
        externalId: team.externalId,
        name: team.name,
        sport: team.sport,
        badgeUrl: team.badgeUrl,
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['followed-teams'] });
      toast.success('Team followed successfully');
      setSearchQuery('');
      setSearchResults([]);
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
    },
    onError: (error: Error) => {
      toast.error('Failed to unfollow team', { description: error.message });
    },
  });

  // Search teams
  const handleSearch = async () => {
    if (!searchQuery.trim()) return;

    setIsSearching(true);
    try {
      const response = await apiClient.get<Team[]>(`/teams/search/${encodeURIComponent(searchQuery)}`);
      // Filter to only supported sports
      const filteredResults = (response.data || []).filter(team =>
        SUPPORTED_SPORTS.some(s => team.sport?.toLowerCase().includes(s.toLowerCase()))
      );
      setSearchResults(filteredResults);

      if (filteredResults.length === 0 && response.data?.length > 0) {
        toast.info('No teams found in supported sports', {
          description: 'Follow Team is only available for Soccer, Basketball, and Ice Hockey'
        });
      }
    } catch {
      toast.error('Search failed');
      setSearchResults([]);
    } finally {
      setIsSearching(false);
    }
  };

  // Discover leagues for a followed team
  const handleDiscoverLeagues = async (teamId: number) => {
    if (expandedTeamId === teamId) {
      setExpandedTeamId(null);
      setDiscoveredLeagues([]);
      setSelectedLeagueIds(new Set());
      return;
    }

    setExpandedTeamId(teamId);
    setIsDiscovering(true);
    setDiscoveredLeagues([]);
    setSelectedLeagueIds(new Set());

    try {
      const response = await apiClient.get<{
        teamId: number;
        teamName: string;
        leagues: DiscoveredLeague[];
      }>(`/followed-teams/${teamId}/leagues`);

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

  // Add selected leagues
  const handleAddLeagues = async (teamId: number) => {
    if (selectedLeagueIds.size === 0) {
      toast.error('No leagues selected');
      return;
    }

    setIsAddingLeagues(true);
    try {
      const response = await apiClient.post(`/followed-teams/${teamId}/add-leagues`, {
        leagueExternalIds: Array.from(selectedLeagueIds),
        monitorEvents,
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
      handleDiscoverLeagues(teamId);
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

  // Check if a team is already followed
  const isTeamFollowed = (externalId?: string) => {
    if (!externalId) return false;
    return followedTeams?.some(ft => ft.externalId === externalId);
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-gray-900 to-black p-4 md:p-6">
      {/* Header */}
      <div className="mb-6">
        <div className="flex items-center gap-3 mb-2">
          <UserGroupIcon className="w-8 h-8 text-red-500" />
          <h1 className="text-2xl font-bold text-white">Follow Teams</h1>
        </div>

        {/* Supported sports notice */}
        <div className="bg-gradient-to-r from-blue-900/30 to-purple-900/30 border border-blue-700/30 rounded-lg p-4 mb-4">
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
              href="https://discord.gg/sportarr"
              target="_blank"
              rel="noopener noreferrer"
              className="text-indigo-400 hover:text-indigo-300 underline"
            >
              Discord
            </a>.
          </p>
        </div>

        {/* Search bar */}
        <div className="flex gap-2">
          <div className="relative flex-1">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder="Search for a team to follow (e.g., Real Madrid, Lakers, Bruins)..."
              className="w-full pl-10 pr-4 py-3 bg-gray-800/50 border border-gray-700 rounded-lg text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-red-500 focus:border-transparent"
            />
          </div>
          <button
            onClick={handleSearch}
            disabled={isSearching || !searchQuery.trim()}
            className="px-4 py-3 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded-lg font-medium transition-colors flex items-center gap-2"
          >
            {isSearching ? (
              <ArrowPathIcon className="w-5 h-5 animate-spin" />
            ) : (
              <MagnifyingGlassIcon className="w-5 h-5" />
            )}
            Search
          </button>
        </div>
      </div>

      {/* Search Results */}
      {searchResults.length > 0 && (
        <div className="mb-8">
          <h2 className="text-lg font-semibold text-white mb-3">Search Results</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {searchResults.map((team) => (
              <div
                key={team.externalId || team.id}
                className="bg-gradient-to-br from-gray-900 to-black border border-gray-800 rounded-lg p-4 flex items-center gap-4"
              >
                {team.badgeUrl ? (
                  <img
                    src={team.badgeUrl}
                    alt={team.name}
                    className="w-12 h-12 object-contain rounded"
                  />
                ) : (
                  <div className="w-12 h-12 bg-gray-800 rounded flex items-center justify-center text-2xl">
                    {SPORT_ICONS[team.sport] || '🏅'}
                  </div>
                )}
                <div className="flex-1 min-w-0">
                  <h3 className="font-semibold text-white truncate">{team.name}</h3>
                  <p className="text-sm text-gray-400">
                    {SPORT_ICONS[team.sport] || ''} {team.sport} • {team.country || 'Unknown'}
                  </p>
                </div>
                <button
                  onClick={() => followTeamMutation.mutate(team)}
                  disabled={isTeamFollowed(team.externalId) || followTeamMutation.isPending}
                  className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                    isTeamFollowed(team.externalId)
                      ? 'bg-green-900/50 text-green-400 cursor-not-allowed'
                      : 'bg-red-600 hover:bg-red-700 text-white'
                  }`}
                >
                  {isTeamFollowed(team.externalId) ? 'Following' : 'Follow'}
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Followed Teams */}
      <div>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-white">
            Followed Teams ({followedTeams?.length || 0})
          </h2>
          <button
            onClick={() => refetchTeams()}
            className="text-gray-400 hover:text-white transition-colors"
          >
            <ArrowPathIcon className="w-5 h-5" />
          </button>
        </div>

        {isLoadingTeams ? (
          <div className="text-center py-8 text-gray-400">Loading...</div>
        ) : followedTeams?.length === 0 ? (
          <div className="text-center py-12 bg-gray-900/50 border border-gray-800 rounded-lg">
            <UserGroupIcon className="w-12 h-12 mx-auto text-gray-600 mb-3" />
            <p className="text-gray-400 mb-2">No teams followed yet</p>
            <p className="text-sm text-gray-500">
              Search for a team above to start following them across all leagues
            </p>
          </div>
        ) : (
          <div className="space-y-4">
            {followedTeams?.map((team) => (
              <div
                key={team.id}
                className="bg-gradient-to-br from-gray-900 to-black border border-gray-800 rounded-lg overflow-hidden"
              >
                {/* Team Header */}
                <div className="p-4 flex items-center gap-4">
                  {team.badgeUrl ? (
                    <img
                      src={team.badgeUrl}
                      alt={team.name}
                      className="w-14 h-14 object-contain rounded"
                    />
                  ) : (
                    <div className="w-14 h-14 bg-gray-800 rounded flex items-center justify-center text-3xl">
                      {SPORT_ICONS[team.sport] || '🏅'}
                    </div>
                  )}
                  <div className="flex-1 min-w-0">
                    <h3 className="font-semibold text-white text-lg">{team.name}</h3>
                    <p className="text-sm text-gray-400">
                      {SPORT_ICONS[team.sport] || ''} {team.sport}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => handleDiscoverLeagues(team.id)}
                      className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium transition-colors flex items-center gap-2"
                    >
                      {isDiscovering && expandedTeamId === team.id ? (
                        <ArrowPathIcon className="w-4 h-4 animate-spin" />
                      ) : expandedTeamId === team.id ? (
                        <ChevronUpIcon className="w-4 h-4" />
                      ) : (
                        <ChevronDownIcon className="w-4 h-4" />
                      )}
                      Discover Leagues
                    </button>
                    <button
                      onClick={() => {
                        if (confirm(`Unfollow ${team.name}?`)) {
                          unfollowTeamMutation.mutate(team.id);
                        }
                      }}
                      className="p-2 text-gray-400 hover:text-red-400 transition-colors"
                    >
                      <TrashIcon className="w-5 h-5" />
                    </button>
                  </div>
                </div>

                {/* Expanded Leagues Section */}
                {expandedTeamId === team.id && (
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
                            {/* Monitor Events */}
                            <div>
                              <label className="block text-sm text-gray-400 mb-1">Monitor Events</label>
                              <select
                                value={monitorEvents ? 'future' : 'none'}
                                onChange={(e) => setMonitorEvents(e.target.value !== 'none')}
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
                                id={`searchOnAdd-${team.id}`}
                                checked={searchOnAdd}
                                onChange={(e) => setSearchOnAdd(e.target.checked)}
                                className="w-4 h-4 rounded border-gray-600 text-red-600 focus:ring-red-500 bg-gray-800"
                              />
                              <label htmlFor={`searchOnAdd-${team.id}`} className="text-sm text-gray-300">
                                Search for missing events
                              </label>
                            </div>

                            {/* Search for Upgrades */}
                            <div className="flex items-center gap-2">
                              <input
                                type="checkbox"
                                id={`searchForUpgrades-${team.id}`}
                                checked={searchForUpgrades}
                                onChange={(e) => setSearchForUpgrades(e.target.checked)}
                                className="w-4 h-4 rounded border-gray-600 text-red-600 focus:ring-red-500 bg-gray-800"
                              />
                              <label htmlFor={`searchForUpgrades-${team.id}`} className="text-sm text-gray-300">
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
                            onClick={() => handleAddLeagues(team.id)}
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
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
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
                                    {SPORT_ICONS[league.sport] || '🏆'}
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
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
