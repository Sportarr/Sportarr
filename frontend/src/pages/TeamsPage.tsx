import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ArrowPathIcon,
  CheckCircleIcon,
  CheckIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  GlobeAltIcon,
  MagnifyingGlassIcon,
  PlusIcon,
  TrashIcon,
  UserGroupIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../api/client';
import ColumnPicker from '../components/ColumnPicker';
import CompactTableFrame from '../components/CompactTableFrame';
import PageHeader from '../components/PageHeader';
import SortableFilterableHeader from '../components/SortableFilterableHeader';
import { useColumnVisibility } from '../hooks/useColumnVisibility';
import { useCompactView } from '../hooks/useCompactView';
import { applyTableSortFilter, useTableSortFilter } from '../hooks/useTableSortFilter';
import type { DiscoveredLeague, FollowedTeam, QualityProfile, Team } from '../types';

const SPORT_FILTERS = [
  { id: 'all', name: 'All Sports', icon: '🌍' },
  { id: 'Soccer', name: 'Soccer', icon: '⚽' },
  { id: 'Basketball', name: 'Basketball', icon: '🏀' },
  { id: 'Ice Hockey', name: 'Ice Hockey', icon: '🏒' },
];

const MONITOR_OPTIONS = [
  { value: 'Future', label: 'Future Events', description: 'Only monitor upcoming events' },
  { value: 'All', label: 'All Events', description: 'Monitor past and future events' },
  { value: 'None', label: 'None', description: 'Do not monitor events automatically' },
];

const PAGE_PADDING = 'p-4 md:p-8';
const TABLE_ROW_HOVER = 'text-sm transition-colors hover:bg-gray-800/50';
const BADGE_RED = 'whitespace-nowrap rounded bg-red-900/30 px-1.5 py-0.5 text-xs text-red-400';
const BADGE_GREEN = 'whitespace-nowrap rounded bg-green-900/30 px-1.5 py-0.5 text-xs text-green-400';
const SCROLLABLE_LIST = 'max-h-60 overflow-y-auto';

type TeamsColumnKey = 'badge' | 'name' | 'sport' | 'country' | 'status' | 'actions';

const TEAM_COLUMN_DEFS: Array<{
  key: TeamsColumnKey;
  label: string;
  alwaysVisible?: boolean;
}> = [
  { key: 'badge', label: 'Badge' },
  { key: 'name', label: 'Team', alwaysVisible: true },
  { key: 'sport', label: 'Sport' },
  { key: 'country', label: 'Country' },
  { key: 'status', label: 'Status' },
  { key: 'actions', label: 'Actions', alwaysVisible: true },
];

interface TeamApiResponse {
  Id?: number;
  id?: number;
  idTeam?: string;
  strTeam?: string;
  strTeamShort?: string;
  strAlternate?: string;
  strSport?: string;
  strCountry?: string;
  strTeamBadge?: string;
  intFormedYear?: string;
  Added?: string;
  added?: string;
}

const getSportIcon = (sport: string): string => {
  const sportLower = sport.toLowerCase();
  if (sportLower.includes('soccer') || sportLower.includes('football')) return '⚽';
  if (sportLower.includes('basketball')) return '🏀';
  if (sportLower.includes('hockey')) return '🏒';
  return '🏅';
};

export default function TeamsPage() {
  const queryClient = useQueryClient();
  const compactView = useCompactView();
  const {
    sortCol,
    sortDir,
    colFilters,
    activeFilterCol,
    handleColSort,
    onFilterChange,
    onFilterToggle,
  } = useTableSortFilter('name');
  const { isVisible, toggleCol } = useColumnVisibility<TeamsColumnKey>(
    'teams-col-visibility',
    { badge: true, name: true, sport: true, country: true, status: true, actions: true },
    ['name', 'actions']
  );
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [expandedTeamId, setExpandedTeamId] = useState<string | null>(null);
  const [discoveredLeagues, setDiscoveredLeagues] = useState<DiscoveredLeague[]>([]);
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [selectedLeagueIds, setSelectedLeagueIds] = useState<Set<string>>(new Set());
  const [monitorType, setMonitorType] = useState('Future');
  const [qualityProfileId, setQualityProfileId] = useState<number>(1);
  const [searchOnAdd, setSearchOnAdd] = useState(false);
  const [searchForUpgrades, setSearchForUpgrades] = useState(false);
  const [isAddingLeagues, setIsAddingLeagues] = useState(false);

  const { data: allTeams = [], isLoading: isLoadingTeams } = useQuery({
    queryKey: ['all-teams'],
    queryFn: async () => {
      const response = await apiClient.get<TeamApiResponse[]>('/teams/all');

      return (response.data || []).map((team): Team => ({
        id: team.Id ?? team.id ?? 0,
        externalId: team.idTeam,
        name: team.strTeam ?? '',
        shortName: team.strTeamShort,
        alternateName: team.strAlternate,
        sport: team.strSport ?? '',
        country: team.strCountry,
        badgeUrl: team.strTeamBadge,
        formedYear: team.intFormedYear ? Number.parseInt(team.intFormedYear, 10) : undefined,
        added: team.Added ?? team.added ?? new Date().toISOString(),
      }));
    },
    staleTime: 5 * 60 * 1000,
    refetchOnWindowFocus: false,
  });

  const { data: followedTeams = [] } = useQuery({
    queryKey: ['followed-teams'],
    queryFn: async () => {
      const response = await apiClient.get<FollowedTeam[]>('/followed-teams');
      return response.data || [];
    },
  });

  const { data: qualityProfiles } = useQuery({
    queryKey: ['quality-profiles'],
    queryFn: async () => {
      const response = await apiClient.get<QualityProfile[]>('/settings/quality-profiles');
      return response.data;
    },
  });

  const followedTeamIds = useMemo(() => {
    const ids = new Set<string>();
    followedTeams.forEach((team) => {
      if (team.externalId) {
        ids.add(team.externalId);
      }
    });
    return ids;
  }, [followedTeams]);

  const filteredTeams = useMemo(() => {
    let filtered = allTeams;

    if (selectedSport !== 'all') {
      filtered = filtered.filter((team) =>
        team.sport?.toLowerCase().includes(selectedSport.toLowerCase())
      );
    }

    if (searchQuery.trim()) {
      const query = searchQuery.toLowerCase();
      filtered = filtered.filter((team) =>
        team.name?.toLowerCase().includes(query) ||
        team.shortName?.toLowerCase().includes(query) ||
        team.alternateName?.toLowerCase().includes(query) ||
        team.country?.toLowerCase().includes(query)
      );
    }

    return filtered
      .filter((team) => {
        const name = team.name ?? '';
        return !name.startsWith('_') && !name.endsWith('_');
      })
      .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
  }, [allTeams, searchQuery, selectedSport]);

  const followTeamMutation = useMutation({
    mutationFn: async (team: Team) => apiClient.post<FollowedTeam>('/followed-teams', {
      externalId: team.externalId,
      name: team.name,
      sport: team.sport,
      badgeUrl: team.badgeUrl,
    }),
    onSuccess: async (response, team) => {
      await queryClient.invalidateQueries({ queryKey: ['followed-teams'] });
      toast.success(`Now following ${team.name}`);

      if (team.externalId && response.data?.id) {
        setExpandedTeamId(team.externalId);
        await discoverLeaguesById(response.data.id);
      }
    },
    onError: (error: Error) => {
      toast.error('Failed to follow team', { description: error.message });
    },
  });

  const unfollowTeamMutation = useMutation({
    mutationFn: async (teamId: number) => apiClient.delete(`/followed-teams/${teamId}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['followed-teams'] });
      toast.success('Team unfollowed');
      setExpandedTeamId(null);
      setDiscoveredLeagues([]);
      setSelectedLeagueIds(new Set());
    },
    onError: (error: Error) => {
      toast.error('Failed to unfollow team', { description: error.message });
    },
  });

  const discoverLeaguesById = async (followedTeamId: number) => {
    setIsDiscovering(true);
    setDiscoveredLeagues([]);
    setSelectedLeagueIds(new Set());

    try {
      const response = await apiClient.get<{
        teamId: number;
        teamName: string;
        leagues: DiscoveredLeague[];
      }>(`/followed-teams/${followedTeamId}/leagues`);

      const leagues = response.data.leagues || [];
      setDiscoveredLeagues(leagues);
      setSelectedLeagueIds(new Set(leagues.filter((league) => !league.isAdded).map((league) => league.externalId)));
    } catch {
      toast.error('Failed to discover leagues');
    } finally {
      setIsDiscovering(false);
    }
  };

  const discoverLeagues = async (teamExternalId: string) => {
    const followedTeam = followedTeams.find((team) => team.externalId === teamExternalId);
    if (!followedTeam) {
      toast.error('Team not found in followed teams');
      return;
    }

    await discoverLeaguesById(followedTeam.id);
  };

  const toggleTeamExpansion = (team: Team) => {
    if (!team.externalId) {
      return;
    }

    if (expandedTeamId === team.externalId) {
      setExpandedTeamId(null);
      setDiscoveredLeagues([]);
      setSelectedLeagueIds(new Set());
      return;
    }

    if (followedTeamIds.has(team.externalId)) {
      setExpandedTeamId(team.externalId);
      void discoverLeagues(team.externalId);
    }
  };

  const handleAddLeagues = async (teamExternalId: string) => {
    if (selectedLeagueIds.size === 0) {
      toast.error('No leagues selected');
      return;
    }

    const followedTeam = followedTeams.find((team) => team.externalId === teamExternalId);
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
          description: added.map((league: { name: string }) => league.name).join(', '),
        });
      }
      if (skipped?.length > 0) {
        toast.info(`Skipped ${skipped.length} league(s)`, {
          description: skipped.map((league: { name: string; reason: string }) => `${league.name}: ${league.reason}`).join(', '),
        });
      }
      if (errors?.length > 0) {
        toast.error(`Failed to add ${errors.length} league(s)`, {
          description: errors.map((league: { reason: string }) => league.reason).join(', '),
        });
      }

      await discoverLeagues(teamExternalId);
      void queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch {
      toast.error('Failed to add leagues');
    } finally {
      setIsAddingLeagues(false);
    }
  };

  const toggleLeagueSelection = (leagueId: string) => {
    setSelectedLeagueIds((previous) => {
      const next = new Set(previous);
      if (next.has(leagueId)) {
        next.delete(leagueId);
      } else {
        next.add(leagueId);
      }
      return next;
    });
  };

  const toggleSelectAll = () => {
    const notAddedLeagues = discoveredLeagues.filter((league) => !league.isAdded);
    if (selectedLeagueIds.size === notAddedLeagues.length) {
      setSelectedLeagueIds(new Set());
      return;
    }

    setSelectedLeagueIds(new Set(notAddedLeagues.map((league) => league.externalId)));
  };

  const getFollowedTeam = (externalId: string) =>
    followedTeams.find((team) => team.externalId === externalId);

  const expandedTeam = expandedTeamId
    ? filteredTeams.find((team) => team.externalId === expandedTeamId)
    : null;

  const renderExpandedLeagues = (teamName: string, teamExternalId: string) => (
    <div className="border-t border-gray-800 p-4 bg-gray-950/50">
      {isDiscovering ? (
        <div className="text-center py-8 text-gray-400">
          <ArrowPathIcon className="w-8 h-8 animate-spin mx-auto mb-2" />
          Discovering leagues...
        </div>
      ) : discoveredLeagues.length === 0 ? (
        <div className="text-center py-8 text-gray-400">
          No leagues found for {teamName}
        </div>
      ) : (
        <>
          <div className="bg-gray-900/50 border border-gray-800 rounded-lg p-4 mb-4">
            <h4 className="font-medium text-white mb-3">League Settings (applied to all selected)</h4>
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              <div>
                <label className="block text-sm text-gray-400 mb-1">Monitor Events</label>
                <select
                  value={monitorType}
                  onChange={(event) => setMonitorType(event.target.value)}
                  className="w-full bg-gray-800 border border-gray-700 rounded px-3 py-2 text-white"
                >
                  {MONITOR_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-sm text-gray-400 mb-1">Quality Profile</label>
                <select
                  value={qualityProfileId}
                  onChange={(event) => setQualityProfileId(Number(event.target.value))}
                  className="w-full bg-gray-800 border border-gray-700 rounded px-3 py-2 text-white"
                >
                  {qualityProfiles?.map((profile) => (
                    <option key={profile.id} value={profile.id}>
                      {profile.name}
                    </option>
                  ))}
                </select>
              </div>

              <div className="flex items-center gap-2">
                <input
                  type="checkbox"
                  id="searchOnAdd"
                  checked={searchOnAdd}
                  onChange={(event) => setSearchOnAdd(event.target.checked)}
                  className="w-4 h-4 rounded border-gray-600 text-red-600 focus:ring-red-500 bg-gray-800"
                />
                <label htmlFor="searchOnAdd" className="text-sm text-gray-300">
                  Search for missing events
                </label>
              </div>

              <div className="flex items-center gap-2">
                <input
                  type="checkbox"
                  id="searchForUpgrades"
                  checked={searchForUpgrades}
                  onChange={(event) => setSearchForUpgrades(event.target.checked)}
                  className="w-4 h-4 rounded border-gray-600 text-red-600 focus:ring-red-500 bg-gray-800"
                />
                <label htmlFor="searchForUpgrades" className="text-sm text-gray-300">
                  Search for quality upgrades
                </label>
              </div>
            </div>
          </div>

          <div className="flex items-center justify-between mb-3">
            <div className="flex items-center gap-4">
              <button
                onClick={toggleSelectAll}
                className="text-sm text-blue-400 hover:text-blue-300"
              >
                {selectedLeagueIds.size === discoveredLeagues.filter((league) => !league.isAdded).length
                  ? 'Deselect All'
                  : 'Select All'}
              </button>
              <span className="text-sm text-gray-400">
                {selectedLeagueIds.size} league(s) selected
              </span>
            </div>

            <button
              onClick={() => handleAddLeagues(teamExternalId)}
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

                  <div className="flex-1 min-w-0">
                    <p className="font-medium text-white truncate">{league.name}</p>
                    <p className="text-xs text-gray-400">
                      {league.country || league.sport} • {league.eventCount} events
                    </p>
                  </div>

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
  );

  const renderCompactTable = () => {
    const tableData = applyTableSortFilter(
      filteredTeams,
      colFilters,
      sortCol,
      sortDir,
      (col, team) => {
        switch (col) {
          case 'name':
            return String(team.name || '');
          case 'sport':
            return String(team.sport || '');
          case 'country':
            return String(team.country || '');
          default:
            return '';
        }
      }
    );

    const visibleColumnCount = TEAM_COLUMN_DEFS.filter((column) => isVisible(column.key)).length;

    return (
      <>
        {tableData.length === 0 ? (
          <div className="py-16 text-center">
            <p className="text-gray-400">
              {searchQuery || selectedSport !== 'all' ? 'No teams found' : 'No teams available'}
            </p>
          </div>
        ) : (
          <CompactTableFrame
            controls={
              <ColumnPicker
                columns={TEAM_COLUMN_DEFS}
                isVisible={(column) => isVisible(column as TeamsColumnKey)}
                onToggle={(column) => toggleCol(column as TeamsColumnKey)}
              />
            }
            className="rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black"
          >
            <thead>
              <tr className="sticky top-0 border-b border-gray-700 bg-gray-950 text-left text-xs uppercase text-gray-400">
                {isVisible('badge') && <th className="w-12 px-2 py-1.5">Badge</th>}
                <SortableFilterableHeader
                  col="name"
                  label="Team"
                  sortCol={sortCol}
                  sortDir={sortDir}
                  onSort={handleColSort}
                  colFilters={colFilters}
                  activeFilterCol={activeFilterCol}
                  onFilterChange={onFilterChange}
                  onFilterToggle={onFilterToggle}
                />
                {isVisible('sport') && (
                  <SortableFilterableHeader
                    col="sport"
                    label="Sport"
                    sortCol={sortCol}
                    sortDir={sortDir}
                    onSort={handleColSort}
                    colFilters={colFilters}
                    activeFilterCol={activeFilterCol}
                    onFilterChange={onFilterChange}
                    onFilterToggle={onFilterToggle}
                    className="px-2 py-1.5"
                  />
                )}
                {isVisible('country') && (
                  <SortableFilterableHeader
                    col="country"
                    label="Country"
                    sortCol={sortCol}
                    sortDir={sortDir}
                    onSort={handleColSort}
                    colFilters={colFilters}
                    activeFilterCol={activeFilterCol}
                    onFilterChange={onFilterChange}
                    onFilterToggle={onFilterToggle}
                    className="px-2 py-1.5"
                  />
                )}
                {isVisible('status') && <th className="px-2 py-1.5">Status</th>}
                <th className="px-2 py-1.5 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-700">
              {tableData.map((team) => {
                const isFollowed = team.externalId ? followedTeamIds.has(team.externalId) : false;
                const followedTeam = team.externalId ? getFollowedTeam(team.externalId) : null;
                const isExpanded = expandedTeamId === team.externalId;

                return (
                  <tr key={team.externalId || team.id} className={TABLE_ROW_HOVER}>
                    {isVisible('badge') && (
                      <td className="px-2 py-2">
                        <div className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded bg-black/50">
                          {team.badgeUrl ? (
                            <img
                              src={team.badgeUrl}
                              alt={team.name}
                              className="max-h-full max-w-full object-contain"
                            />
                          ) : (
                            <span className="text-lg opacity-50">{getSportIcon(team.sport || '')}</span>
                          )}
                        </div>
                      </td>
                    )}
                    <td className="px-3 py-2 font-medium text-white">
                      <div>
                        <div className="text-white">{team.name}</div>
                        {team.alternateName && (
                          <div className="text-xs text-gray-400">{team.alternateName}</div>
                        )}
                      </div>
                    </td>
                    {isVisible('sport') && (
                      <td className="px-2 py-2">
                        <span className={BADGE_RED}>{team.sport || 'Unknown'}</span>
                      </td>
                    )}
                    {isVisible('country') && (
                      <td className="px-2 py-2 text-sm text-gray-400">
                        {team.country ? (
                          <span className="flex items-center gap-1">
                            <GlobeAltIcon className="h-3 w-3 flex-shrink-0" />
                            {team.country}
                          </span>
                        ) : (
                          <span className="text-gray-600">-</span>
                        )}
                      </td>
                    )}
                    {isVisible('status') && (
                      <td className="px-2 py-2 text-sm">
                        {isFollowed ? (
                          <span className={BADGE_GREEN}>Following</span>
                        ) : (
                          <span className="text-gray-600">-</span>
                        )}
                      </td>
                    )}
                    <td className="px-2 py-2 text-right">
                      <div className="flex items-center justify-end gap-1">
                        {isFollowed ? (
                          <>
                            <button
                              type="button"
                              onClick={() => toggleTeamExpansion(team)}
                              className="rounded p-1.5 text-green-400 transition-colors hover:bg-green-900/30 hover:text-green-300"
                              title={isExpanded ? 'Collapse' : 'Expand'}
                            >
                              {isExpanded ? (
                                <ChevronUpIcon className="h-4 w-4" />
                              ) : (
                                <ChevronDownIcon className="h-4 w-4" />
                              )}
                            </button>
                            <button
                              type="button"
                              onClick={() => {
                                if (followedTeam && confirm(`Unfollow ${team.name}?`)) {
                                  unfollowTeamMutation.mutate(followedTeam.id);
                                }
                              }}
                              className="rounded p-1.5 text-gray-400 transition-colors hover:bg-red-900/30 hover:text-red-400"
                              title="Unfollow"
                            >
                              <TrashIcon className="h-4 w-4" />
                            </button>
                          </>
                        ) : (
                          <button
                            type="button"
                            onClick={() => followTeamMutation.mutate(team)}
                            disabled={followTeamMutation.isPending}
                            className="rounded p-1.5 text-red-400 transition-colors hover:bg-red-900/30 hover:text-red-300 disabled:opacity-50"
                            title="Follow"
                          >
                            {followTeamMutation.isPending ? (
                              <ArrowPathIcon className="h-4 w-4 animate-spin" />
                            ) : (
                              <UserGroupIcon className="h-4 w-4" />
                            )}
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
              {tableData.length === 0 && (
                <tr>
                  <td colSpan={visibleColumnCount} className="px-3 py-8 text-center text-gray-400">
                    No teams found
                  </td>
                </tr>
              )}
            </tbody>
          </CompactTableFrame>
        )}

        {expandedTeam?.externalId && renderExpandedLeagues(expandedTeam.name, expandedTeam.externalId)}
      </>
    );
  };

  return (
    <div className={PAGE_PADDING}>
      <div className="mx-auto max-w-7xl">
        <PageHeader
          title="Add Team"
          subtitle="Follow teams across multiple leagues. When you follow a team, you can add all their leagues at once."
        />

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

        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6 mb-6">
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-300 mb-2">
              Filter by Sport
            </label>
            <div className="flex flex-wrap gap-2">
              {SPORT_FILTERS.map((sport) => (
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

          <div className="relative">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
            <input
              type="text"
              value={searchQuery}
              onChange={(event) => setSearchQuery(event.target.value)}
              placeholder="Filter teams (e.g., Real Madrid, Lakers, Bruins)..."
              className="w-full pl-10 pr-4 py-3 bg-black border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
            />
          </div>

          <p className="text-sm text-gray-500 mt-3">
            Showing {isLoadingTeams ? '...' : filteredTeams.length} of {allTeams.length} teams
            {searchQuery && ` matching "${searchQuery}"`}
            {selectedSport !== 'all' && ` in ${SPORT_FILTERS.find((sport) => sport.id === selectedSport)?.name}`}
          </p>
        </div>

        {isLoadingTeams && (
          <div className="text-center py-16">
            <div className="animate-spin rounded-full h-16 w-16 border-b-2 border-red-600 mx-auto mb-4" />
            <h3 className="text-xl font-semibold text-gray-400 mb-2">
              Loading Teams...
            </h3>
            <p className="text-gray-500">
              Fetching all teams for supported sports from Sportarr
            </p>
          </div>
        )}

        {!isLoadingTeams && (
          <div>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-xl font-semibold text-white">
                {selectedSport === 'all' ? 'All Teams' : `${SPORT_FILTERS.find((sport) => sport.id === selectedSport)?.name} Teams`}
                {filteredTeams.length > 0 && ` (${filteredTeams.length})`}
              </h2>
            </div>

            {compactView ? (
              renderCompactTable()
            ) : filteredTeams.length > 0 ? (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {filteredTeams.map((team) => {
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
                      <div className="flex items-center p-4">
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
                              className="px-4 py-2 rounded-lg font-medium bg-red-600 hover:bg-red-700 text-white transition-colors flex items-center gap-2 disabled:opacity-60"
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

                      {isExpanded && team.externalId && renderExpandedLeagues(team.name, team.externalId)}
                    </div>
                  );
                })}
              </div>
            ) : (
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
        )}
      </div>
    </div>
  );
}
