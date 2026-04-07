import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, CheckIcon, TrashIcon, ArrowPathIcon } from '@heroicons/react/24/outline';
import SortableFilterableHeader from '../components/SortableFilterableHeader';
import ColumnPicker from '../components/ColumnPicker';
import CompactTableFrame from '../components/CompactTableFrame';
import { useCompactView } from '../hooks/useCompactView';
import { useTableSortFilter, applyTableSortFilter } from '../hooks/useTableSortFilter';
import { useColumnVisibility } from '../hooks/useColumnVisibility';
import { toast } from 'sonner';
import apiClient from '../api/client';
import type { League } from '../types';
import { LeagueProgressLine } from '../components/LeagueProgressBar';
import { PAGE_PADDING, TABLE_ROW_HOVER, SCROLLABLE_LIST, BADGE_GRAY } from '../utils/designTokens';
import { getSportIcon } from '../utils/sportIcons';

type LeaguesColumnKey = 'sport' | 'logo' | 'name' | 'monitored' | 'eventCount' | 'downloaded' | 'missing' | 'progress' | 'quality';

const isInternalLeagueName = (name: string) => {
  const normalized = name.trim();
  return normalized.startsWith('_') || normalized.endsWith('_');
};

export default function LeaguesPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [selectedLeagueIds, setSelectedLeagueIds] = useState<Set<number>>(new Set());
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [deleteLeagueFolder, setDeleteLeagueFolder] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [showRenameDialog, setShowRenameDialog] = useState(false);
  const [isRenaming, setIsRenaming] = useState(false);
  const [renamePreview, setRenamePreview] = useState<Array<{leagueId: number; leagueName: string; existingPath: string; newPath: string; existingFileName?: string; newFileName?: string; folderChanged?: boolean; changes: Array<{field: string; oldValue: string; newValue: string}>}>>([]);
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const compactView = useCompactView();
  const { sortCol, sortDir, colFilters, activeFilterCol, handleColSort, onFilterChange, onFilterToggle } = useTableSortFilter('name');

  // Column visibility for the compact table — persisted to localStorage.
  // 'name' (League) is always visible; all others can be hidden by the user.
  const { isVisible, toggleCol } = useColumnVisibility<LeaguesColumnKey>(
    'leagues-col-visibility',
    { sport: true, logo: true, name: true, monitored: true, eventCount: true, downloaded: true, missing: true, progress: true, quality: true },
    ['name']
  );

  const { data: leagues, isLoading, error, refetch } = useQuery({
    queryKey: ['leagues'],
    queryFn: async () => {
      const response = await apiClient.get<League[]>('/leagues');
      return response.data;
    },
    staleTime: 2 * 60 * 1000, // 2 minutes - library data changes less frequently
    refetchOnWindowFocus: false, // Don't refetch on tab focus
  });

  // Filter leagues by selected sport and search query, then sort alphabetically.
  // Entries whose name starts or ends with '_' are internal/test records — hidden everywhere.
  const filteredLeagues = (leagues?.filter(league => {
    const name = league.name ?? '';
    if (isInternalLeagueName(name)) return false;
    const matchesSport = selectedSport === 'all' || league.sport === selectedSport;
    const matchesSearch = name.toLowerCase().includes(searchQuery.toLowerCase());
    return matchesSport && matchesSearch;
  }) || []).sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));

  // Group leagues by sport for statistics (also excludes _ entries so counts stay accurate)
  const leaguesBySport = leagues?.reduce((acc, league) => {
    const name = league.name ?? '';
    if (league.sport && !isInternalLeagueName(name)) {
      acc[league.sport] = (acc[league.sport] || 0) + 1;
    }
    return acc;
  }, {} as Record<string, number>) || {};

  // Dynamically generate sport filters based on user's leagues
  const sportFilters = useMemo(() => {
    const filters = [{ id: 'all', name: 'All Sports', icon: '🌍' }];

    // Get unique sports from user's leagues
    const uniqueSports = Array.from(new Set(
      (leagues || [])
        .filter(l => !isInternalLeagueName(l.name ?? ''))
        .map(l => l.sport).filter(Boolean)
    )).sort((a, b) => a.localeCompare(b));

    // Add sport filters for each unique sport the user has
    uniqueSports.forEach(sport => {
      filters.push({
        id: sport,
        name: sport,
        icon: getSportIcon(sport),
      });
    });

    return filters;
  }, [leagues]);

  // Selection mode helpers
  const toggleLeagueSelection = (leagueId: number, e: React.MouseEvent) => {
    e.stopPropagation(); // Prevent navigation when clicking checkbox
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

  const selectAllFiltered = () => {
    setSelectedLeagueIds(new Set(filteredLeagues.map(l => l.id)));
  };

  const clearSelection = () => {
    setSelectedLeagueIds(new Set());
  };

  const handleDeleteSelected = async () => {
    if (selectedLeagueIds.size === 0) return;

    setIsDeleting(true);
    try {
      await Promise.all(
        Array.from(selectedLeagueIds).map(leagueId =>
          apiClient.delete(`/leagues/${leagueId}`, {
            params: { deleteFiles: deleteLeagueFolder }
          })
        )
      );
      setShowDeleteDialog(false);
      setDeleteLeagueFolder(false);
      setSelectedLeagueIds(new Set());
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch (error) {
      console.error('Failed to delete leagues:', error);
    } finally {
      setIsDeleting(false);
    }
  };

  const handleOpenRenameDialog = async () => {
    if (selectedLeagueIds.size === 0) return;

    setShowRenameDialog(true);
    setIsLoadingPreview(true);
    setRenamePreview([]);

    try {
      const response = await apiClient.post('/leagues/rename-preview', {
        leagueIds: Array.from(selectedLeagueIds)
      });
      setRenamePreview(response.data || []);
    } catch (error) {
      console.error('Failed to load rename preview:', error);
      toast.error('Failed to load rename preview');
    } finally {
      setIsLoadingPreview(false);
    }
  };

  const handleRenameSelected = async () => {
    if (selectedLeagueIds.size === 0) return;

    setIsRenaming(true);
    try {
      const response = await apiClient.post('/leagues/rename', {
        leagueIds: Array.from(selectedLeagueIds)
      });
      const { totalRenamed } = response.data;

      toast.success('Files Renamed Successfully', {
        description: `${totalRenamed} file(s) have been renamed according to your naming scheme.`,
      });
      setShowRenameDialog(false);
      setSelectedLeagueIds(new Set());
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch (error) {
      console.error('Failed to rename files:', error);
      toast.error('Failed to rename files');
    } finally {
      setIsRenaming(false);
    }
  };

  const selectedLeagues = useMemo(() => {
    return leagues?.filter(l => selectedLeagueIds.has(l.id)) || [];
  }, [leagues, selectedLeagueIds]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <p className="text-red-500 text-xl mb-4">Failed to load leagues</p>
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

  const renderCompactTable = () => {
    if (filteredLeagues.length === 0) {
      return (
        <div className="text-center py-12">
          <p className="text-gray-400 text-lg">
            {searchQuery ? 'No leagues found' : selectedSport === 'all' ? 'No leagues yet' : `No ${selectedSport} leagues`}
          </p>
          <p className="text-gray-500 text-sm mt-2">
            Click "Add League" to start tracking sports competitions
          </p>
        </div>
      );
    }

    // Apply column filters + sort via shared utility. missingCount is derived
    // from eventCount - downloadedMonitoredCount since it's not stored on the object.
    const tableLeagues = applyTableSortFilter(
      filteredLeagues,
      colFilters,
      sortCol,
      sortDir,
      (col, item) => {
        switch (col) {
          case 'sport': return String(item.sport || '');
          case 'name': return String(item.name || '');
          case 'eventCount': return String(item.eventCount || 0);
          case 'downloadedMonitoredCount': return String(item.downloadedMonitoredCount || 0);
          case 'missingCount': return String((item.eventCount || 0) - (item.downloadedMonitoredCount || 0));
          default: return '';
        }
      }
    );

    const colDefs = [
      { key: 'sport', label: 'Sport' },
      { key: 'logo', label: 'Logo' },
      { key: 'name', label: 'League', alwaysVisible: true },
      { key: 'monitored', label: 'Monitored' },
      { key: 'eventCount', label: 'Total Events' },
      { key: 'downloaded', label: 'Downloaded' },
      { key: 'missing', label: 'Missing' },
      { key: 'progress', label: 'Progress' },
      { key: 'quality', label: 'Quality' },
    ];

    return (
      <CompactTableFrame
        controls={
          <ColumnPicker
            columns={colDefs}
            isVisible={isVisible as (col: string) => boolean}
            onToggle={toggleCol as (col: string) => void}
          />
        }
      >
          <thead>
            <tr className="text-xs text-gray-400 uppercase text-left border-b border-gray-700 bg-gray-950 sticky top-0">
              <th className="px-3 py-1.5 w-10 text-center">
                <input
                  type="checkbox"
                  checked={filteredLeagues.length > 0 && selectedLeagueIds.size === filteredLeagues.length}
                  onChange={() => {
                    if (selectedLeagueIds.size === filteredLeagues.length) {
                      clearSelection();
                    } else {
                      selectAllFiltered();
                    }
                  }}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-500 cursor-pointer"
                  title="Select all leagues"
                />
              </th>
              {isVisible('sport') && <SortableFilterableHeader col="sport" label="Sport" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-1.5" />}
              {isVisible('logo') && <th className="px-3 py-1.5">Logo</th>}
              <SortableFilterableHeader col="name" label="League" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-1.5" />
              {isVisible('monitored') && <th className="px-3 py-1.5 text-center">Monitored</th>}
              {isVisible('eventCount') && <SortableFilterableHeader col="eventCount" label="Total Events" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-1.5 text-center" centered />}
              {isVisible('downloaded') && <SortableFilterableHeader col="downloadedMonitoredCount" label="Downloaded" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-1.5 text-center" centered />}
              {isVisible('missing') && <SortableFilterableHeader col="missingCount" label="Missing" sortCol={sortCol} sortDir={sortDir} onSort={handleColSort} colFilters={colFilters} activeFilterCol={activeFilterCol} onFilterChange={onFilterChange} onFilterToggle={onFilterToggle} className="px-3 py-1.5 text-center" centered />}
              {isVisible('progress') && <th className="px-3 py-1.5">Progress</th>}
              {isVisible('quality') && <th className="px-3 py-1.5">Quality</th>}
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-700">
            {tableLeagues.map((league: typeof filteredLeagues[0]) => {
              const isSelected = selectedLeagueIds.has(league.id);
              const missingCount = (league.eventCount || 0) - (league.downloadedMonitoredCount || 0);

              return (
                <tr
                  key={league.id}
                  onClick={() => {
                    if (!isSelected) {
                      navigate(`/leagues/${league.id}`);
                    }
                  }}
                  className={`${TABLE_ROW_HOVER} cursor-pointer ${
                    isSelected ? 'bg-red-900/20' : ''
                  }`}
                >
                  <td className="px-3 py-1.5 text-center" onClick={(e) => e.stopPropagation()}>
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={(e) => {
                        e.stopPropagation();
                        toggleLeagueSelection(league.id, e as any);
                      }}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-500 cursor-pointer"
                    />
                  </td>
                  {isVisible('sport') && (
                    <td className="px-3 py-1.5 text-gray-400">
                      <span className="inline-flex items-center gap-1">
                        <span className="text-sm leading-none">{getSportIcon(league.sport || '')}</span>
                        <span>{league.sport || '—'}</span>
                      </span>
                    </td>
                  )}
                  {isVisible('logo') && (
                    <td className="px-3 py-3">
                      <div className="h-12 w-12 bg-black/50 flex items-center justify-center rounded flex-shrink-0">
                        {league.logoUrl ? (
                          <img src={league.logoUrl} alt={league.name} className="max-h-full max-w-full object-contain" />
                        ) : (
                          <span className="text-[10px] leading-tight text-center text-gray-400 px-1">{league.sport || 'No logo'}</span>
                        )}
                      </div>
                    </td>
                  )}
                  <td className="px-3 py-1.5 font-medium text-white">{league.name}</td>
                  {isVisible('monitored') && (
                    <td className="px-3 py-1.5 text-center">
                      {league.monitored ? <span className="text-green-400 text-lg">●</span> : <span className="text-gray-600 text-lg">○</span>}
                    </td>
                  )}
                  {isVisible('eventCount') && (
                    <td className="px-2 py-1.5 text-center text-gray-300">{league.eventCount || 0}</td>
                  )}
                  {isVisible('downloaded') && (
                    <td className="px-2 py-1.5 text-center">
                      <span className="text-green-400">{league.downloadedMonitoredCount || 0}</span>
                    </td>
                  )}
                  {isVisible('missing') && (
                    <td className="px-2 py-1.5 text-center">
                      {missingCount > 0 ? <span className="text-red-400">{missingCount}</span> : <span className="text-gray-600">—</span>}
                    </td>
                  )}
                  {isVisible('progress') && (
                    <td className="px-3 py-1.5">
                      {league.progressPercent !== undefined && (
                        <LeagueProgressLine progressPercent={league.progressPercent} progressStatus={league.progressStatus || 'unmonitored'} />
                      )}
                    </td>
                  )}
                  {isVisible('quality') && (
                    <td className="px-2 py-1.5">
                      {league.qualityProfileId ? <span className={BADGE_GRAY}>#{league.qualityProfileId}</span> : <span className="text-gray-600">—</span>}
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
      </CompactTableFrame>
    );
  };

  return (
    <div className={PAGE_PADDING}>
      <div className="mb-3 md:mb-4 flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
        <div className="min-w-0">
          <h1 className="text-2xl font-bold text-white md:text-3xl">Leagues</h1>
          <p className="mt-1 text-sm text-gray-400 md:text-base">
            Manage your monitored leagues and competitions
          </p>
        </div>

        <div className="overflow-x-auto xl:max-w-[calc(100%-16rem)]">
          <div className="flex min-w-max flex-wrap items-center justify-start gap-2 xl:justify-end">
            {sportFilters.length > 1 && (
              <div className="flex flex-wrap items-center gap-2">
                {sportFilters.map(sport => (
                  <button
                    key={sport.id}
                    onClick={() => setSelectedSport(sport.id)}
                    className={`
                      flex items-center gap-1.5 rounded-lg whitespace-nowrap font-medium transition-all text-sm px-4 py-2.5 md:px-4 md:py-2.5 md:text-base
                      ${selectedSport === sport.id
                        ? 'bg-red-600 text-white'
                        : 'bg-gray-900 text-gray-400 hover:bg-gray-800 hover:text-white border border-red-900/30'
                      }
                    `}
                  >
                    <span className="text-base leading-none">{sport.icon}</span>
                    <span>{sport.name}</span>
                    {sport.id !== 'all' && leaguesBySport[sport.id] && (
                      <span className="ml-0.5 rounded bg-black/30 px-1.5 py-0.5 text-xs">
                        {leaguesBySport[sport.id]}
                      </span>
                    )}
                  </button>
                ))}
              </div>
            )}

            <button
              onClick={() => navigate('/add-league/search')}
              className="rounded-lg bg-red-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-red-700 md:px-6 md:py-3 md:text-base"
            >
              <span className="sm:hidden">+ Add</span>
              <span className="hidden sm:inline">+ Add League</span>
            </button>
          </div>
        </div>
      </div>

      {/* Search Bar */}
      <div className="mb-3 md:mb-4 max-w-2xl">
        <div className="relative">
          <div className="absolute inset-y-0 left-0 pl-3 md:pl-4 flex items-center pointer-events-none">
            <MagnifyingGlassIcon className="h-4 w-4 md:h-5 md:w-5 text-gray-400" />
          </div>
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search leagues..."
            className="w-full rounded-lg border border-red-900/30 bg-gray-900 py-2 pl-10 pr-4 text-sm text-white transition-all placeholder-gray-500 focus:border-red-600 focus:outline-none focus:ring-2 focus:ring-red-600/20 md:py-2.5 md:pl-12 md:text-base"
          />
        </div>
      </div>

      {/* Stats */}
      <div className="mb-3 md:mb-4 grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-5 md:gap-4">
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="text-gray-400 text-xs md:text-sm mb-1">Total Leagues</p>
          <p className="text-xl md:text-3xl font-bold text-white">{leagues?.length || 0}</p>
        </div>
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="text-gray-400 text-xs md:text-sm mb-1">Monitored</p>
          <p className="text-xl md:text-3xl font-bold text-white">
            {leagues?.filter(l => l.monitored).length || 0}
          </p>
        </div>
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="text-gray-400 text-xs md:text-sm mb-1">Total Events</p>
          <p className="text-xl md:text-3xl font-bold text-white">
            {leagues?.reduce((sum, league) => sum + (league.eventCount || 0), 0) || 0}
          </p>
        </div>
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="text-gray-400 text-xs md:text-sm mb-1">Monitored Events</p>
          <p className="text-xl md:text-3xl font-bold text-white">
            {leagues?.reduce((sum, league) => sum + (league.monitoredEventCount || 0), 0) || 0}
          </p>
        </div>
        <div className="col-span-2 rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4 sm:col-span-1">
          <p className="text-gray-400 text-xs md:text-sm mb-1">Downloaded</p>
          <p className="text-xl md:text-3xl font-bold text-white">
            {leagues?.reduce((sum, league) => sum + (league.fileCount || 0), 0) || 0}
          </p>
        </div>
      </div>

      {/* Leagues Grid or Table */}
      {compactView ? (
        renderCompactTable()
      ) : filteredLeagues.length === 0 ? (
        <div className="text-center py-12">
          <p className="text-gray-400 text-lg">
            {searchQuery ? 'No leagues found' : selectedSport === 'all' ? 'No leagues yet' : `No ${selectedSport} leagues`}
          </p>
          <p className="text-gray-500 text-sm mt-2">
            Click "Add League" to start tracking sports competitions
          </p>
        </div>
      ) : (
        <div className={`grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 md:gap-4 ${selectedLeagueIds.size > 0 ? 'pb-24' : ''}`}>
          {filteredLeagues.map((league) => {
            const isSelected = selectedLeagueIds.has(league.id);
            return (
              <div
                key={league.id}
                onClick={() => navigate(`/leagues/${league.id}`)}
                className={`bg-gray-900 border rounded-lg overflow-hidden hover:shadow-lg transition-all cursor-pointer group ${
                  isSelected
                    ? 'border-red-500 ring-2 ring-red-500/50 shadow-red-900/30'
                    : 'border-red-900/30 hover:border-red-600/50 hover:shadow-red-900/20'
                }`}
              >
                {/* Logo/Poster */}
                <div className="relative h-28 bg-gray-800 p-3 overflow-hidden md:h-32">
                  {league.logoUrl || league.bannerUrl || league.posterUrl ? (
                    <img
                      src={league.logoUrl || league.bannerUrl || league.posterUrl}
                      alt={league.name}
                      className="w-full h-full object-contain transition-transform duration-300 group-hover:scale-105"
                    />
                  ) : (
                    <div className="w-full h-full flex items-center justify-center">
                      <span className="text-6xl font-bold text-gray-700">
                        {league.name.charAt(0)}
                      </span>
                    </div>
                  )}

                  {/* Selection Checkbox */}
                  <div
                    className="absolute top-2 left-2 z-10"
                    onClick={(e) => toggleLeagueSelection(league.id, e)}
                  >
                    <div className={`w-6 h-6 rounded border-2 flex items-center justify-center transition-colors ${
                      isSelected
                        ? 'bg-red-600 border-red-600'
                        : 'bg-black/50 border-white/50 hover:border-white'
                    }`}>
                      {isSelected && (
                        <CheckIcon className="h-4 w-4 text-white" />
                      )}
                    </div>
                  </div>

                  {/* Sport Badge */}
                  <div className="absolute top-2 left-10">
                    <span className="inline-flex items-center gap-1 rounded bg-black/70 px-2 py-1 text-xs font-semibold text-white backdrop-blur-sm">
                      <span className="text-sm leading-none">{getSportIcon(league.sport || '')}</span>
                      {league.sport}
                    </span>
                  </div>

                  {/* Status Badges */}
                  <div className="absolute top-2 right-2 flex flex-col gap-2 items-end">
                    {league.monitored ? (
                      <span className="px-2 py-1 bg-green-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                        Monitored
                      </span>
                    ) : (
                      <span className="px-2 py-1 bg-gray-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                        Not Monitored
                      </span>
                    )}
                  </div>

                {/* Event Count Badge */}
                <div className="absolute bottom-3 left-2">
                  <span className="px-3 py-1 bg-black/70 backdrop-blur-sm text-white text-sm font-semibold rounded">
                    {league.eventCount || 0} {(league.eventCount || 0) === 1 ? 'Event' : 'Events'}
                  </span>
                </div>

                {/* Progress Bar */}
                <LeagueProgressLine
                  progressPercent={league.progressPercent || 0}
                  progressStatus={league.progressStatus || 'unmonitored'}
                />
              </div>

              {/* Info */}
                <div className="p-4">
                  <h3 className="text-white font-bold text-lg mb-2 truncate">{league.name}</h3>

                {league.country && (
                  <p className="text-gray-400 text-sm mb-3">{league.country}</p>
                )}

                {/* Stats Row */}
                  <div className="mb-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                    <div className="flex items-center gap-1 whitespace-nowrap">
                      <span className="w-2 h-2 rounded-full bg-blue-500 flex-shrink-0"></span>
                      <span className="text-gray-400">Monitored:</span>
                    <span className="text-white font-semibold">{league.monitoredEventCount || 0}</span>
                  </div>
                  <div className="flex items-center gap-1 whitespace-nowrap">
                    <span className="w-2 h-2 rounded-full bg-green-500 flex-shrink-0"></span>
                    <span className="text-gray-400">Have:</span>
                    <span className="text-white font-semibold">{league.downloadedMonitoredCount || 0}</span>
                  </div>
                  {(league.missingCount || 0) > 0 && (
                    <div className="flex items-center gap-1 whitespace-nowrap">
                      <span className="w-2 h-2 rounded-full bg-red-500 flex-shrink-0"></span>
                      <span className="text-gray-400">Missing:</span>
                      <span className="text-red-400 font-semibold">{league.missingCount}</span>
                    </div>
                  )}
                </div>

                {/* Quality Profile Badge */}
                {league.qualityProfileId && (
                  <span className={BADGE_GRAY}>
                    Quality Profile #{league.qualityProfileId}
                  </span>
                )}
              </div>
            </div>
            );
          })}
        </div>
      )}

      {/* Floating Action Bar (when items are selected) */}
      {selectedLeagueIds.size > 0 && (
        <div className="fixed bottom-0 left-0 right-0 z-50 border-t border-red-900/50 bg-gray-900 shadow-lg shadow-black/50">
          <div className="max-w-7xl mx-auto px-4 md:px-8 py-3 md:py-4 flex flex-col sm:flex-row items-center justify-between gap-3">
            <div className="flex flex-wrap items-center justify-center sm:justify-start gap-2 md:gap-4">
              <span className="text-white font-semibold text-sm md:text-base">
                {selectedLeagueIds.size} {selectedLeagueIds.size === 1 ? 'League' : 'Leagues'} Selected
              </span>
              <button
                onClick={selectAllFiltered}
                className="text-xs md:text-sm text-gray-400 hover:text-white transition-colors"
              >
                Select All ({filteredLeagues.length})
              </button>
              <button
                onClick={clearSelection}
                className="text-xs md:text-sm text-gray-400 hover:text-white transition-colors"
              >
                Clear
              </button>
            </div>
            <div className="flex items-center gap-3">
              <button
                onClick={handleOpenRenameDialog}
                className="px-3 md:px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 font-semibold transition-colors flex items-center gap-2 text-sm md:text-base"
              >
                <ArrowPathIcon className="h-4 w-4 md:h-5 md:w-5" />
                <span className="hidden sm:inline">Rename Files</span>
                <span className="sm:hidden">Rename</span>
              </button>
              <button
                onClick={() => setShowDeleteDialog(true)}
                className="px-3 md:px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 font-semibold transition-colors flex items-center gap-2 text-sm md:text-base"
              >
                <TrashIcon className="h-4 w-4 md:h-5 md:w-5" />
                <span className="hidden sm:inline">Delete Selected</span>
                <span className="sm:hidden">Delete</span>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Dialog */}
      {showDeleteDialog && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50">
          <div className="bg-gray-900 border border-red-900/50 rounded-lg p-6 max-w-lg w-full mx-4 shadow-2xl">
            <h2 className="text-xl font-bold text-white mb-4">Delete {selectedLeagueIds.size} {selectedLeagueIds.size === 1 ? 'League' : 'Leagues'}?</h2>

            <p className="text-gray-400 mb-4">
              The following {selectedLeagueIds.size === 1 ? 'league' : 'leagues'} and all associated events will be removed from Sportarr:
            </p>

            {/* List of leagues to be deleted */}
            <div className="bg-gray-800/50 rounded-lg p-3 mb-4 max-h-40 overflow-y-auto">
              {selectedLeagues.map(league => (
                <div key={league.id} className="flex items-center gap-2 py-1 text-sm text-white">
                  <span className="text-gray-400">{league.sport || 'League'}</span>
                  <span>{league.name}</span>
                  {(league.eventCount || 0) > 0 && (
                    <span className="text-gray-500">({league.eventCount} events)</span>
                  )}
                </div>
              ))}
            </div>

            {/* Delete folder checkbox */}
            <label className="flex items-start gap-3 mb-6 cursor-pointer group">
              <div className="relative flex items-center">
                <input
                  type="checkbox"
                  checked={deleteLeagueFolder}
                  onChange={(e) => setDeleteLeagueFolder(e.target.checked)}
                  className="sr-only"
                />
                <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-colors ${
                  deleteLeagueFolder
                    ? 'bg-red-600 border-red-600'
                    : 'border-gray-500 group-hover:border-gray-400'
                }`}>
                  {deleteLeagueFolder && (
                    <CheckIcon className="h-3 w-3 text-white" />
                  )}
                </div>
              </div>
              <div>
                <span className="text-white font-medium">Delete league folder(s)</span>
                <p className="text-gray-500 text-sm">This will permanently delete the league folders and all files from disk.</p>
              </div>
            </label>

            {/* Warning for delete files */}
            {deleteLeagueFolder && (
              <div className="bg-red-900/30 border border-red-600/50 rounded-lg p-3 mb-4">
                <p className="text-red-400 text-sm">
                  <strong>Warning:</strong> This action cannot be undone. All media files in the selected league folders will be permanently deleted.
                </p>
              </div>
            )}

            {/* Dialog buttons */}
            <div className="flex justify-end gap-3">
              <button
                onClick={() => {
                  setShowDeleteDialog(false);
                  setDeleteLeagueFolder(false);
                }}
                disabled={isDeleting}
                className="px-4 py-2 bg-gray-700 text-white rounded-lg hover:bg-gray-600 font-semibold transition-colors disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                onClick={handleDeleteSelected}
                disabled={isDeleting}
                className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 font-semibold transition-colors disabled:opacity-50 flex items-center gap-2"
              >
                {isDeleting ? (
                  <>
                    <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                    Deleting...
                  </>
                ) : (
                  <>
                    <TrashIcon className="h-5 w-5" />
                    Delete
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Rename Confirmation Dialog */}
      {showRenameDialog && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50">
          <div className="bg-gray-900 border border-red-900/50 rounded-lg p-6 max-w-4xl w-full mx-4 shadow-2xl max-h-[90vh] flex flex-col">
            <h2 className="text-xl font-bold text-white mb-4">
              Organize {selectedLeagueIds.size} Selected {selectedLeagueIds.size === 1 ? 'League' : 'Leagues'}
            </h2>

            <p className="text-gray-400 mb-4">
              The following files will be renamed according to your naming settings:
            </p>

            {/* List of leagues being organized */}
            <div className="bg-gray-800/50 rounded-lg p-3 mb-4">
              <p className="text-sm text-gray-400 mb-2">Selected Leagues:</p>
              <div className="flex flex-wrap gap-2">
                {selectedLeagues.map(league => (
                  <span key={league.id} className="px-2 py-1 bg-gray-700 text-white text-sm rounded">
                    {league.name}
                  </span>
                ))}
              </div>
            </div>

            {/* File rename preview */}
            <div className="flex-1 overflow-y-auto mb-4">
              {isLoadingPreview ? (
                <div className="flex items-center justify-center h-32">
                  <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-red-600"></div>
                </div>
              ) : renamePreview.length > 0 ? (
                <div className="space-y-2">
                  <div className="bg-blue-900/20 border border-blue-600/50 rounded-lg p-3">
                    <p className="text-blue-400 text-sm">
                      <strong>{renamePreview.length}</strong> file{renamePreview.length !== 1 ? 's' : ''} will be renamed
                    </p>
                  </div>
                  <div className={`space-y-2 ${SCROLLABLE_LIST}`}>
                    {renamePreview.map((preview, index) => (
                      <div key={index} className="bg-gray-800/50 rounded-lg p-3 border border-red-900/20">
                        <div className="flex items-center gap-2 mb-2">
                          <span className="text-xs text-gray-500">{preview.leagueName}</span>
                          {preview.folderChanged && (
                            <span className="px-1.5 py-0.5 bg-yellow-600/20 text-yellow-400 text-xs rounded">
                              Folder Change
                            </span>
                          )}
                        </div>
                        <div className="space-y-1">
                          <div>
                            <p className="text-gray-400 text-xs">Current Path:</p>
                            <p className="text-gray-300 font-mono text-xs break-all">{preview.existingPath}</p>
                          </div>
                          <div>
                            <p className="text-gray-400 text-xs">New Path:</p>
                            <p className="text-green-400 font-mono text-xs break-all">{preview.newPath}</p>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="bg-gray-800/50 rounded-lg p-6 text-center">
                  <ArrowPathIcon className="w-12 h-12 text-gray-600 mx-auto mb-3" />
                  <p className="text-gray-400">No files need renaming</p>
                  <p className="text-gray-500 text-sm mt-1">
                    All files are already using the correct naming format
                  </p>
                </div>
              )}
            </div>

            {/* Dialog buttons */}
            <div className="flex justify-end gap-3 pt-4 border-t border-gray-800">
              <button
                onClick={() => setShowRenameDialog(false)}
                disabled={isRenaming}
                className="px-4 py-2 bg-gray-700 text-white rounded-lg hover:bg-gray-600 font-semibold transition-colors disabled:opacity-50"
              >
                Cancel
              </button>
              {renamePreview.length > 0 && (
                <button
                  onClick={handleRenameSelected}
                  disabled={isRenaming}
                  className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 font-semibold transition-colors disabled:opacity-50 flex items-center gap-2"
                >
                  {isRenaming ? (
                    <>
                      <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                      Renaming...
                    </>
                  ) : (
                    <>
                      <ArrowPathIcon className="h-5 w-5" />
                      Organize
                    </>
                  )}
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
