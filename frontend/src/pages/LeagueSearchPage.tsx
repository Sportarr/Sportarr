import { useState, useMemo, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, GlobeAltIcon, TrophyIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import CompactTableFrame from '../components/CompactTableFrame';
import PageHeader from '../components/PageHeader';
import SortableFilterableHeader from '../components/SortableFilterableHeader';
import { useCompactView } from '../hooks/useCompactView';
import { useTableSortFilter, applyTableSortFilter } from '../hooks/useTableSortFilter';
import AddLeagueModal from '../components/AddLeagueModal';
import ConfirmationModal from '../components/ConfirmationModal';
import { apiGet, apiPost, apiPut, apiDelete } from '../utils/api';
import { PAGE_PADDING, TABLE_ROW_HOVER } from '../utils/designTokens';
import { isMotorsport, isGolf, isIndividualTennis, isFightingSport } from '../utils/leagueSportRules';
import { getSportIcon } from '../utils/sportIcons';
import { BUTTON_PRIMARY, BUTTON_INFO } from '../utils/designTokens';

interface League {
  // Sportarr API field names
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
  // Sportarr serialized field names (camelCase from backend League model)
  externalId?: string;
  name?: string;
  sport?: string;
  country?: string;
  description?: string;
  logoUrl?: string;
  bannerUrl?: string;
  posterUrl?: string;
  website?: string;
  formedYear?: string;
}

interface AddedLeagueInfo {
  id: number;
  externalId: string;
}

const isInternalLeagueName = (name: string) => {
  const normalized = name.trim();
  return normalized.startsWith('_') || normalized.endsWith('_');
};

export default function LeagueSearchPage() {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editMode, setEditMode] = useState(false);
  const [hoveredLeagueId, setHoveredLeagueId] = useState<string | null>(null);
  const [isDeleteConfirmOpen, setIsDeleteConfirmOpen] = useState(false);
  const compactView = useCompactView();
  const { sortCol, sortDir, colFilters, activeFilterCol, handleColSort, onFilterChange, onFilterToggle } = useTableSortFilter('strLeague');
  const queryClient = useQueryClient();

  // CRITICAL: Store stable modal data in refs to prevent modal unmounting during state changes
  // This prevents the modal from unmounting before the Transition can clean up inert attributes
  const addModalDataRef = useRef<{ league: League; leagueId: number | null; editMode: boolean } | null>(null);
  const deleteModalDataRef = useRef<{ leagueId: number; leagueName: string; eventCount: number } | null>(null);

  // Fetch all leagues from Sportarr API
  const { data: allLeagues = [], isLoading } = useQuery({
    queryKey: ['sportarr-leagues', 'all'],
    queryFn: async () => {
      const response = await apiGet('/api/leagues/all');
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
      const response = await apiGet('/api/leagues');
      if (!response.ok) throw new Error('Failed to fetch user leagues');
      return response.json();
    },
  });

  // Create a map of added leagues by external ID (includes logo URLs from database)
  const addedLeaguesMap = useMemo(() => {
    const map = new Map<string, AddedLeagueInfo & { logoUrl?: string }>();
    userLeagues.forEach((league: any) => {
      if (league.externalId) {
        map.set(league.externalId, {
          id: league.id,
          externalId: league.externalId,
          logoUrl: league.logoUrl, // Logo stored in database when league was added
        });
      }
    });
    return map;
  }, [userLeagues]);

  // Real-time filtering based on search query and selected sport, then sort alphabetically
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
        (league.strLeague ?? '').toLowerCase().includes(query) ||
        league.strLeagueAlternate?.toLowerCase().includes(query) ||
        (league.strSport ?? '').toLowerCase().includes(query) ||
        league.strCountry?.toLowerCase().includes(query)
      );
    }

    // Sort alphabetically; entries with _ prefix/suffix are internal records — hidden entirely
    return filtered
      .filter(l => !isInternalLeagueName(l.strLeague ?? ''))
      .sort((a, b) => (a.strLeague ?? '').localeCompare(b.strLeague ?? ''));
  }, [allLeagues, selectedSport, searchQuery]);

  // Apply column filters and column sort on top of filteredLeagues (compact table only)
  const tableLeagues = useMemo(
    () => applyTableSortFilter(filteredLeagues, colFilters, sortCol, sortDir, (col, item) => {
      switch (col) {
        case 'strLeague': return String(item.strLeague || '');
        case 'strSport': return String(item.strSport || '');
        case 'strCountry': return String(item.strCountry || '');
        case 'intFormedYear': return String(item.intFormedYear || '');
        default: return '';
      }
    }),
    [filteredLeagues, colFilters, sortCol, sortDir]
  );

  const sportFilters = useMemo(() => {
    const filters = [{ id: 'all', name: 'All Sports', icon: '🌍' }];
    const uniqueSports = Array.from(new Set(
      allLeagues
        .filter(l => !isInternalLeagueName(l.strLeague ?? ''))
        .map(l => l.strSport)
        .filter(Boolean)
    )).sort((a, b) => a.localeCompare(b));

    uniqueSports.forEach(sport => {
      filters.push({ id: sport, name: sport, icon: getSportIcon(sport) });
    });

    return filters;
  }, [allLeagues]);

  const addLeagueMutation = useMutation({
    mutationFn: async ({
      league,
      monitoredTeamIds,
      monitorType,
      qualityProfileId,
      searchForMissingEvents,
      searchForCutoffUnmetEvents,
      monitoredParts,
      monitoredSessionTypes,
      monitoredEventTypes,
      searchQueryTemplate,
      tags
    }: {
      league: League;
      monitoredTeamIds: string[];
      monitorType: string;
      qualityProfileId: number | null;
      searchForMissingEvents: boolean;
      searchForCutoffUnmetEvents: boolean;
      monitoredParts: string | null;
      monitoredSessionTypes: string | null;
      monitoredEventTypes: string | null;
      searchQueryTemplate?: string | null;
      tags?: number[];
    }) => {
      // For motorsports, golf, individual tennis (ATP, WTA), and UFC-style fighting leagues, league is always monitored
      // For other sports, league is monitored only if teams are selected
      const isMotorsportLeague = isMotorsport(league.strSport);
      const isGolfLeague = isGolf(league.strSport);
      const isIndividualTennisLeague = isIndividualTennis(league.strSport, league.strLeague);
      const isFightingEventTypeLeague = isFightingSport(league.strSport) && (league.strLeague.toLowerCase().includes('ufc') || league.strLeague.toLowerCase().includes('ultimate fighting'));
      const monitored = isMotorsportLeague || isGolfLeague || isIndividualTennisLeague || isFightingEventTypeLeague ? true : monitoredTeamIds.length > 0;

      const response = await apiPost('/api/leagues', {
        externalId: league.idLeague,
        name: league.strLeague,
        sport: league.strSport,
        country: league.strCountry,
        description: league.strDescriptionEN,
        monitored: monitored,
        monitorType: monitorType,
        qualityProfileId: qualityProfileId,
        searchForMissingEvents: searchForMissingEvents,
        searchForCutoffUnmetEvents: searchForCutoffUnmetEvents,
        monitoredParts: monitoredParts,
        monitoredSessionTypes: monitoredSessionTypes,
        monitoredEventTypes: monitoredEventTypes,
        searchQueryTemplate: searchQueryTemplate,
        tags: tags,
        logoUrl: league.strBadge || league.strLogo,
        bannerUrl: league.strBanner,
        posterUrl: league.strPoster,
        website: league.strWebsite,
        formedYear: league.intFormedYear || null,
        monitoredTeamIds: monitoredTeamIds.length > 0 ? monitoredTeamIds : null,
      });

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to add league');
      }

      return response.json();
    },
    onSuccess: (data, variables) => {
      const isMotorsportLeague = isMotorsport(variables.league.strSport);
      const isGolfLeague = isGolf(variables.league.strSport);
      const isIndividualTennisLeague = isIndividualTennis(variables.league.strSport, variables.league.strLeague);
      let message: string;

      if (isMotorsportLeague) {
        const sessionCount = variables.monitoredSessionTypes ? variables.monitoredSessionTypes.split(',').length : 0;
        message = sessionCount > 0
          ? `Added ${variables.league.strLeague} with ${sessionCount} monitored session type${sessionCount !== 1 ? 's' : ''}!`
          : `Added ${variables.league.strLeague} (all session types monitored)`;
      } else if (isGolfLeague || isIndividualTennisLeague) {
        message = `Added ${variables.league.strLeague} (all events monitored)`;
      } else {
        const teamCount = variables.monitoredTeamIds.length;
        message = teamCount > 0
          ? `Added ${variables.league.strLeague} with ${teamCount} monitored team${teamCount !== 1 ? 's' : ''}!`
          : `Added ${variables.league.strLeague} (not monitored - no teams selected)`;
      }

      toast.success(message);
      closeAddModal();
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
      queryClient.invalidateQueries({ queryKey: ['sportarr-leagues'] });
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
      monitoredParts,
      monitoredSessionTypes,
      monitoredEventTypes,
      applyMonitoredPartsToEvents,
      searchQueryTemplate,
      tags,
      sport,
      leagueName
    }: {
      leagueId: number;
      monitoredTeamIds: string[];
      monitorType: string;
      qualityProfileId: number | null;
      searchForMissingEvents: boolean;
      searchForCutoffUnmetEvents: boolean;
      monitoredParts: string | null;
      monitoredSessionTypes: string | null;
      monitoredEventTypes: string | null;
      applyMonitoredPartsToEvents: boolean;
      searchQueryTemplate?: string | null;
      tags?: number[];
      sport: string;
      leagueName: string;
    }) => {
      // For motorsports, golf, individual tennis (ATP, WTA), and UFC-style fighting leagues, league is always monitored
      // For other sports, league is monitored only if teams are selected
      const isMotorsportLeague = isMotorsport(sport);
      const isGolfLeague = isGolf(sport);
      const isIndividualTennisLeague = isIndividualTennis(sport, leagueName);
      const isFightingEventTypeLeague = isFightingSport(sport) && (leagueName.toLowerCase().includes('ufc') || leagueName.toLowerCase().includes('ultimate fighting'));
      const monitored = isMotorsportLeague || isGolfLeague || isIndividualTennisLeague || isFightingEventTypeLeague ? true : monitoredTeamIds.length > 0;

      // First update the league settings
      const settingsResponse = await apiPut(`/api/leagues/${leagueId}`, {
        monitored: monitored,
        monitorType: monitorType,
        qualityProfileId: qualityProfileId,
        searchForMissingEvents: searchForMissingEvents,
        searchForCutoffUnmetEvents: searchForCutoffUnmetEvents,
        monitoredParts: monitoredParts,
        monitoredSessionTypes: monitoredSessionTypes,
        monitoredEventTypes: monitoredEventTypes,
        applyMonitoredPartsToEvents: applyMonitoredPartsToEvents,
        searchQueryTemplate: searchQueryTemplate,
        tags: tags,
      });

      if (!settingsResponse.ok) {
        const error = await settingsResponse.json();
        throw new Error(error.error || 'Failed to update league settings');
      }

      // Then update the monitored teams (only for sports that use team selection)
      if (!isMotorsportLeague && !isGolfLeague && !isIndividualTennisLeague) {
        const teamsResponse = await apiPut(`/api/leagues/${leagueId}/teams`, {
          monitoredTeamIds: monitoredTeamIds.length > 0 ? monitoredTeamIds : null,
        });

        if (!teamsResponse.ok) {
          const error = await teamsResponse.json();
          throw new Error(error.error || 'Failed to update monitored teams');
        }

        return teamsResponse.json();
      }

      return settingsResponse.json();
    },
    onSuccess: async (data, variables) => {
      const isMotorsportLeague = isMotorsport(variables.sport);
      const isGolfLeague = isGolf(variables.sport);
      const isIndividualTennisLeague = isIndividualTennis(variables.sport, variables.leagueName);
      let message: string;

      if (isMotorsportLeague) {
        const partsCount = variables.monitoredParts ? variables.monitoredParts.split(',').length : 0;
        message = partsCount > 0
          ? `Updated settings with ${partsCount} monitored session${partsCount !== 1 ? 's' : ''}`
          : 'League settings updated (no sessions selected)';
      } else if (isGolfLeague || isIndividualTennisLeague) {
        message = 'League settings updated (all events monitored)';
      } else {
        const teamCount = data.teamCount || variables.monitoredTeamIds.length;
        message = teamCount > 0
          ? `Updated monitored teams (${teamCount} team${teamCount !== 1 ? 's' : ''})`
          : 'League set to not monitored (no teams selected)';
      }

      toast.success(message);
      const leagueId = addModalDataRef.current?.leagueId;
      // Invalidate and refetch queries BEFORE closing modal to ensure fresh data
      // Use refetchQueries instead of invalidateQueries for immediate UI update
      // Convert leagueId to string for query key consistency with useParams()
      const leagueIdStr = leagueId?.toString();
      await queryClient.refetchQueries({ queryKey: ['leagues'] });
      if (leagueIdStr) {
        // Refetch immediately (not just invalidate) to ensure UI shows fresh data
        await queryClient.refetchQueries({ queryKey: ['league', leagueIdStr] });
        await queryClient.refetchQueries({ queryKey: ['league-events', leagueIdStr] });
      }
      closeAddModal();
    },
    onError: (error: Error) => {
      toast.error(error.message);
    },
  });

  const deleteLeagueMutation = useMutation({
    mutationFn: async (leagueId: number) => {
      const response = await apiDelete(`/api/leagues/${leagueId}`);

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to delete league');
      }

      return response.json();
    },
    onSuccess: () => {
      toast.success('League removed from library');
      closeDeleteModal();
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    },
    onError: (error: Error) => {
      toast.error(error.message);
    },
  });

  // Helper to open add modal with stable data stored in ref
  const openAddModal = (league: League) => {
    addModalDataRef.current = { league, leagueId: null, editMode: false };
    setEditMode(false);
    setIsModalOpen(true);
  };

  // Helper to open edit modal with stable data stored in ref
  const openEditModal = (league: League, leagueId: number) => {
    addModalDataRef.current = { league, leagueId, editMode: true };
    setEditMode(true);
    setIsModalOpen(true);
  };

  // Helper to close add/edit modal and clean up ref
  const closeAddModal = () => {
    setIsModalOpen(false);
    setEditMode(false);
    // Clear ref after modal transition completes
    setTimeout(() => {
      addModalDataRef.current = null;
    }, 300);
  };

  // Helper to open delete confirmation with stable data
  const openDeleteModal = (leagueId: number, leagueName: string) => {
    const userLeague = userLeagues.find((l: any) => l.id === leagueId);
    const eventCount = userLeague?.eventCount || 0;
    deleteModalDataRef.current = { leagueId, leagueName, eventCount };
    setIsDeleteConfirmOpen(true);
  };

  // Helper to close delete confirmation and clean up ref
  const closeDeleteModal = () => {
    setIsDeleteConfirmOpen(false);
    setTimeout(() => {
      deleteModalDataRef.current = null;
    }, 300);
  };

  const handleAddLeague = (
    league: League,
    monitoredTeamIds: string[],
    monitorType: string,
    qualityProfileId: number | null,
    searchForMissingEvents: boolean,
    searchForCutoffUnmetEvents: boolean,
    monitoredParts: string | null,
    applyMonitoredPartsToEvents: boolean,
    monitoredSessionTypes: string | null,
    monitoredEventTypes: string | null,
    searchQueryTemplate: string | null,
    tags: number[],
  ) => {
    const modalData = addModalDataRef.current;
    if (modalData?.editMode && modalData.leagueId) {
      updateLeagueSettingsMutation.mutate({
        leagueId: modalData.leagueId,
        monitoredTeamIds,
        monitorType,
        qualityProfileId,
        searchForMissingEvents,
        searchForCutoffUnmetEvents,
        monitoredParts,
        monitoredSessionTypes,
        monitoredEventTypes,
        applyMonitoredPartsToEvents,
        searchQueryTemplate,
        tags,
        sport: league.strSport,
        leagueName: league.strLeague
      });
    } else {
      addLeagueMutation.mutate({
        league,
        monitoredTeamIds,
        monitorType,
        qualityProfileId,
        searchForMissingEvents,
        searchForCutoffUnmetEvents,
        monitoredParts,
        monitoredSessionTypes,
        monitoredEventTypes,
        searchQueryTemplate,
        tags
      });
    }
  };

  const handleCloseModal = () => {
    if (!addLeagueMutation.isPending && !updateLeagueSettingsMutation.isPending) {
      closeAddModal();
    }
  };

  const handleConfirmDelete = () => {
    if (deleteModalDataRef.current) {
      deleteLeagueMutation.mutate(deleteModalDataRef.current.leagueId);
    }
  };

  const handleCardClick = (league: League, isAdded: boolean, addedLeagueInfo?: AddedLeagueInfo) => {
    if (isAdded && addedLeagueInfo) {
      // Navigate to league detail page
      navigate(`/leagues/${addedLeagueInfo.id}`);
    } else {
      // Open add modal
      openAddModal(league);
    }
  };

  const renderLeagueTable = (leagues: typeof tableLeagues) => (
    <CompactTableFrame>
        <thead>
          <tr className="text-xs text-gray-400 uppercase text-left border-b border-gray-700 bg-gray-950 sticky top-0">
            <th className="px-3 py-2">Logo</th>
            <SortableFilterableHeader col="strLeague" label="League" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-2" />
            <SortableFilterableHeader col="strSport" label="Sport" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-2" />
            <SortableFilterableHeader col="strCountry" label="Country" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-2" />
            <SortableFilterableHeader col="intFormedYear" label="Est." sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-2" />
            <th className="px-3 py-2 text-right">Action</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-700">
          {leagues.map(league => {
            const addedLeagueInfo = addedLeaguesMap.get(league.idLeague);
            const isAdded = !!addedLeagueInfo;
            const logoUrl = addedLeagueInfo?.logoUrl || league.strBadge || league.strLogo || league.logoUrl;
            return (
              <tr
                key={league.idLeague}
                onClick={() => { if (!isAdded) handleCardClick(league, isAdded, addedLeagueInfo); }}
                className={`${TABLE_ROW_HOVER} cursor-pointer ${isAdded ? 'bg-green-900/10' : ''}`}
              >
                <td className="px-3 py-3">
                  <div className="h-10 w-10 bg-black/50 flex items-center justify-center rounded flex-shrink-0">
                    {logoUrl ? (
                      <img src={logoUrl} alt={league.strLeague} className="max-h-full max-w-full object-contain" />
                    ) : (
                      <span className="text-lg" title={league.strSport || 'Sport'}>
                        {getSportIcon(league.strSport)}
                      </span>
                    )}
                  </div>
                </td>
                <td className="px-3 py-3 font-medium text-white">
                  <div className="text-white">{league.strLeague}</div>
                  {league.strLeagueAlternate && <div className="text-xs text-gray-400">{league.strLeagueAlternate}</div>}
                </td>
                <td className="px-3 py-3 text-gray-400 text-sm">
                  <span className="inline-flex items-center gap-1 px-1.5 py-0.5 bg-red-600/20 text-red-400 text-xs rounded whitespace-nowrap">
                    <span className="text-sm leading-none">{getSportIcon(league.strSport)}</span>
                    <span>{league.strSport}</span>
                  </span>
                </td>
                <td className="px-3 py-3 text-gray-400 text-sm">
                  {league.strCountry ? (
                    <span className="flex items-center gap-1"><GlobeAltIcon className="w-3 h-3 flex-shrink-0" />{league.strCountry}</span>
                  ) : <span className="text-gray-600">—</span>}
                </td>
                <td className="px-3 py-3 text-gray-400 text-sm">
                  {league.intFormedYear ? <span>{league.intFormedYear}</span> : <span className="text-gray-600">—</span>}
                </td>
                <td className="px-3 py-3 text-right">
                  {isAdded ? (
                    <div className="flex items-center justify-end gap-1">
                      <button
                        onMouseEnter={() => setHoveredLeagueId(league.idLeague)}
                        onMouseLeave={() => setHoveredLeagueId(null)}
                        onClick={(e) => { e.stopPropagation(); addedLeagueInfo && openDeleteModal(addedLeagueInfo.id, league.strLeague); }}
                        className="rounded-lg border border-green-700 px-4 py-2 text-sm font-medium text-green-400 transition-colors hover:border-red-700 hover:bg-red-900/30 hover:text-red-300"
                        title="Remove from Library"
                      >
                        {hoveredLeagueId === league.idLeague ? 'Remove' : 'Added'}
                      </button>
                      <button
                        onClick={(e) => { e.stopPropagation(); addedLeagueInfo && openEditModal(league, addedLeagueInfo.id); }}
                        className={BUTTON_INFO}
                        title="Edit League"
                      >
                        Edit
                      </button>
                    </div>
                  ) : (
                    <button
                      onClick={(e) => { e.stopPropagation(); openAddModal(league); }}
                      className={BUTTON_PRIMARY}
                      title="Add to Library"
                    >
                      Add
                    </button>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
    </CompactTableFrame>
  );

  return (
    <div className={PAGE_PADDING}>
        <PageHeader
          title="Add League"
          subtitle="Browse and add leagues from the Sportarr database to monitor their events"
        />

        {/* Search Controls */}
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-4 md:p-5 mb-4 md:mb-6">
          {/* Sport Filter */}
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-300 mb-2">
              Filter by Sport
            </label>
            <div className="flex flex-wrap gap-2">
              {sportFilters.map(sport => (
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

            <p className="mt-2 text-sm text-gray-500">
              💡 Showing {isLoading ? '...' : filteredLeagues.length} of {allLeagues.length} leagues
              {searchQuery && ` matching "${searchQuery}"`}
              {selectedSport !== 'all' && ` in ${selectedSport}`}
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
              Fetching all available leagues from Sportarr
            </p>
          </div>
        )}

        {/* Search Results */}
        {!isLoading && filteredLeagues.length > 0 && (
          <div>
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-xl font-semibold text-white">
                {selectedSport === 'all' ? 'All Leagues' : `${selectedSport} Leagues`}
                {' '}({filteredLeagues.length})
              </h2>
            </div>

            {compactView ? (
              renderLeagueTable(tableLeagues)
            ) : (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                {filteredLeagues.map(league => {
                  const addedLeagueInfo = addedLeaguesMap.get(league.idLeague);
                  const isAdded = !!addedLeagueInfo;
                  // Use logo from database (if league is added) or from API response
                  const logoUrl = addedLeagueInfo?.logoUrl || league.strBadge || league.strLogo || league.logoUrl;

                  return (
                    <div
                      key={league.idLeague}
                      onClick={() => handleCardClick(league, isAdded, addedLeagueInfo)}
                      className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden hover:border-red-700/50 transition-all cursor-pointer"
                    >
                      {/* League Badge/Logo */}
                      <div className="h-28 bg-black/50 flex items-center justify-center p-3 md:h-32">
                        {logoUrl ? (
                          <img
                            src={logoUrl}
                            alt={league.strLeague}
                            className="max-h-full max-w-full object-contain"
                          />
                        ) : (
                          <span className="text-4xl opacity-60" title={league.strSport || 'Sport'}>
                            {getSportIcon(league.strSport)}
                          </span>
                        )}
                      </div>

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
                          <span className="inline-flex items-center gap-1 px-2 py-1 bg-red-600/20 text-red-400 text-xs rounded font-medium">
                            <span className="text-sm leading-none">{getSportIcon(league.strSport)}</span>
                            <span>{league.strSport}</span>
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
                                addedLeagueInfo && openDeleteModal(addedLeagueInfo.id, league.strLeague);
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
                                addedLeagueInfo && openEditModal(league, addedLeagueInfo.id);
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
                              openAddModal(league);
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
            )}
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

      {/* Add League Modal - Always render but control with isOpen for proper transition cleanup */}
      <AddLeagueModal
        league={addModalDataRef.current?.league || null}
        isOpen={isModalOpen}
        onClose={handleCloseModal}
        onAdd={handleAddLeague}
        isAdding={addLeagueMutation.isPending || updateLeagueSettingsMutation.isPending}
        editMode={addModalDataRef.current?.editMode || false}
        leagueId={addModalDataRef.current?.leagueId || null}
      />

      {/* Delete Confirmation Modal - Always rendered, uses show prop for proper transition cleanup */}
      <ConfirmationModal
        isOpen={isDeleteConfirmOpen}
        onClose={closeDeleteModal}
        onConfirm={handleConfirmDelete}
        title={deleteModalDataRef.current ? "Remove League from Library" : undefined}
        message={deleteModalDataRef.current ? `Are you sure you want to remove "${deleteModalDataRef.current.leagueName}" from your library?${
          deleteModalDataRef.current.eventCount > 0
            ? ` This will remove the league and all ${deleteModalDataRef.current.eventCount} event${deleteModalDataRef.current.eventCount !== 1 ? 's' : ''}.`
            : ''
        }` : undefined}
        confirmText="Remove League"
        confirmButtonClass="bg-red-600 hover:bg-red-700"
        isLoading={deleteLeagueMutation.isPending}
      />
    </div>
  );
}
