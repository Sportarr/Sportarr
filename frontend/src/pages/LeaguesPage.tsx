import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, CheckIcon, XMarkIcon, TrashIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import type { League } from '../types';
import { LeagueProgressLine } from '../components/LeagueProgressBar';

// Icon mapping for sports (complete list from TheSportsDB)
const SPORT_ICONS: Record<string, string> = {
  'American Football': '🏈',
  'Athletics': '🏃',
  'Australian Football': '🏉',
  'Badminton': '🏸',
  'Baseball': '⚾',
  'Basketball': '🏀',
  'Climbing': '🧗',
  'Cricket': '🏏',
  'Cycling': '🚴',
  'Darts': '🎯',
  'Esports': '🎮',
  'Equestrian': '🏇',
  'Extreme Sports': '🪂',
  'Field Hockey': '🏑',
  'Fighting': '🥊',
  'Gaelic': '🏐',
  'Gambling': '🎰',
  'Golf': '⛳',
  'Gymnastics': '🤸',
  'Handball': '🤾',
  'Ice Hockey': '🏒',
  'Lacrosse': '🥍',
  'Motorsport': '🏎️',
  'Multi Sports': '🏅',
  'Netball': '🏀',
  'Rugby': '🏉',
  'Shooting': '🎯',
  'Skating': '⛸️',
  'Skiing': '⛷️',
  'Snooker': '🎱',
  'Soccer': '⚽',
  'Table Tennis': '🏓',
  'Tennis': '🎾',
  'Volleyball': '🏐',
  'Watersports': '🏄',
  'Weightlifting': '🏋️',
  'Wintersports': '🎿',
};

export default function LeaguesPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [isSelectionMode, setIsSelectionMode] = useState(false);
  const [selectedLeagueIds, setSelectedLeagueIds] = useState<Set<number>>(new Set());
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [deleteLeagueFolder, setDeleteLeagueFolder] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data: leagues, isLoading, error, refetch } = useQuery({
    queryKey: ['leagues'],
    queryFn: async () => {
      const response = await apiClient.get<League[]>('/leagues');
      return response.data;
    },
    staleTime: 2 * 60 * 1000, // 2 minutes - library data changes less frequently
    refetchOnWindowFocus: false, // Don't refetch on tab focus
  });

  // Filter leagues by selected sport and search query
  const filteredLeagues = leagues?.filter(league => {
    const matchesSport = selectedSport === 'all' || league.sport === selectedSport;
    const matchesSearch = league.name?.toLowerCase().includes(searchQuery.toLowerCase());
    return matchesSport && matchesSearch;
  }) || [];

  // Group leagues by sport for statistics
  const leaguesBySport = leagues?.reduce((acc, league) => {
    if (league.sport) {
      acc[league.sport] = (acc[league.sport] || 0) + 1;
    }
    return acc;
  }, {} as Record<string, number>) || {};

  // Dynamically generate sport filters based on user's leagues
  const sportFilters = useMemo(() => {
    const filters = [{ id: 'all', name: 'All Sports', icon: '🌐' }];

    // Get unique sports from user's leagues
    const uniqueSports = Array.from(new Set(leagues?.map(l => l.sport).filter(Boolean) || []));

    // Add sport filters for each unique sport the user has
    uniqueSports.forEach(sport => {
      filters.push({
        id: sport,
        name: sport,
        icon: SPORT_ICONS[sport] || '🌍',
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

  const exitSelectionMode = () => {
    setIsSelectionMode(false);
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
      setIsSelectionMode(false);
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch (error) {
      console.error('Failed to delete leagues:', error);
    } finally {
      setIsDeleting(false);
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

  return (
    <div className="p-8">
      {/* Header */}
      <div className="mb-6">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold text-white mb-2">Leagues</h1>
            <p className="text-gray-400">
              Manage your monitored leagues and competitions across all sports
            </p>
          </div>
          <div className="flex items-center gap-3">
            {isSelectionMode ? (
              <button
                onClick={exitSelectionMode}
                className="px-6 py-3 bg-gray-700 text-white rounded-lg hover:bg-gray-600 font-semibold transition-colors flex items-center gap-2"
              >
                <XMarkIcon className="h-5 w-5" />
                Cancel
              </button>
            ) : (
              <button
                onClick={() => setIsSelectionMode(true)}
                className="px-6 py-3 bg-gray-800 text-white rounded-lg hover:bg-gray-700 font-semibold transition-colors border border-red-900/30 flex items-center gap-2"
              >
                <CheckIcon className="h-5 w-5" />
                Select
              </button>
            )}
            <button
              onClick={() => navigate('/add-league/search')}
              className="px-6 py-3 bg-red-600 text-white rounded-lg hover:bg-red-700 font-semibold transition-colors"
            >
              + Add League
            </button>
          </div>
        </div>
      </div>

      {/* Sport Filter Tabs - Only show if user has leagues */}
      {sportFilters.length > 1 && (
        <div className="mb-8">
          <div className="flex gap-2 overflow-x-auto pb-2">
            {sportFilters.map(sport => (
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
                {sport.id !== 'all' && leaguesBySport[sport.id] && (
                  <span className="ml-1 px-2 py-0.5 bg-black/30 rounded text-xs">
                    {leaguesBySport[sport.id]}
                  </span>
                )}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Search Bar */}
      <div className="mb-8 max-w-2xl">
        <div className="relative">
          <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none">
            <MagnifyingGlassIcon className="h-5 w-5 text-gray-400" />
          </div>
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search leagues..."
            className="w-full pl-12 pr-4 py-3 bg-gray-900 border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all"
          />
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-1 md:grid-cols-5 gap-4 mb-8">
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Total Leagues</p>
          <p className="text-3xl font-bold text-white">{leagues?.length || 0}</p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Monitored Leagues</p>
          <p className="text-3xl font-bold text-white">
            {leagues?.filter(l => l.monitored).length || 0}
          </p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Total Events</p>
          <p className="text-3xl font-bold text-white">
            {leagues?.reduce((sum, league) => sum + (league.eventCount || 0), 0) || 0}
          </p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Monitored Events</p>
          <p className="text-3xl font-bold text-white">
            {leagues?.reduce((sum, league) => sum + (league.monitoredEventCount || 0), 0) || 0}
          </p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Downloaded</p>
          <p className="text-3xl font-bold text-white">
            {leagues?.reduce((sum, league) => sum + (league.fileCount || 0), 0) || 0}
          </p>
        </div>
      </div>

      {/* Leagues Grid */}
      {filteredLeagues.length === 0 ? (
        <div className="text-center py-12">
          <p className="text-gray-400 text-lg">
            {searchQuery ? 'No leagues found' : selectedSport === 'all' ? 'No leagues yet' : `No ${selectedSport} leagues`}
          </p>
          <p className="text-gray-500 text-sm mt-2">
            Click "Add League" to start tracking sports competitions
          </p>
        </div>
      ) : (
        <div className={`grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6 ${isSelectionMode && selectedLeagueIds.size > 0 ? 'pb-24' : ''}`}>
          {filteredLeagues.map((league) => {
            const isSelected = selectedLeagueIds.has(league.id);
            return (
              <div
                key={league.id}
                onClick={() => {
                  if (isSelectionMode) {
                    // In selection mode, toggle selection on card click
                    setSelectedLeagueIds(prev => {
                      const next = new Set(prev);
                      if (next.has(league.id)) {
                        next.delete(league.id);
                      } else {
                        next.add(league.id);
                      }
                      return next;
                    });
                  } else {
                    navigate(`/leagues/${league.id}`);
                  }
                }}
                className={`bg-gray-900 border rounded-lg overflow-hidden hover:shadow-lg transition-all cursor-pointer group ${
                  isSelected
                    ? 'border-red-500 ring-2 ring-red-500/50 shadow-red-900/30'
                    : 'border-red-900/30 hover:border-red-600/50 hover:shadow-red-900/20'
                }`}
              >
                {/* Logo/Poster */}
                <div className="relative aspect-[16/9] bg-gray-800 overflow-hidden">
                  {league.logoUrl || league.bannerUrl || league.posterUrl ? (
                    <img
                      src={league.logoUrl || league.bannerUrl || league.posterUrl}
                      alt={league.name}
                      className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                    />
                  ) : (
                    <div className="w-full h-full flex items-center justify-center">
                      <span className="text-6xl font-bold text-gray-700">
                        {league.name.charAt(0)}
                      </span>
                    </div>
                  )}

                  {/* Selection Checkbox (only in selection mode) */}
                  {isSelectionMode && (
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
                  )}

                  {/* Sport Badge - shift right when checkbox is visible */}
                  <div className={`absolute top-2 ${isSelectionMode ? 'left-10' : 'left-2'} transition-all`}>
                    <span className="px-2 py-1 bg-black/70 backdrop-blur-sm text-white text-xs font-semibold rounded">
                      {SPORT_ICONS[league.sport] || '🌐'} {league.sport}
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
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm mb-3">
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
                  <div className="text-xs">
                    <span className="px-2 py-1 bg-gray-800 text-gray-300 rounded">
                      Quality Profile #{league.qualityProfileId}
                    </span>
                  </div>
                )}
              </div>
            </div>
            );
          })}
        </div>
      )}

      {/* Floating Action Bar (when items are selected) */}
      {isSelectionMode && selectedLeagueIds.size > 0 && (
        <div className="fixed bottom-0 left-0 right-0 bg-gray-900 border-t border-red-900/50 shadow-lg shadow-black/50 z-50">
          <div className="max-w-7xl mx-auto px-8 py-4 flex items-center justify-between">
            <div className="flex items-center gap-4">
              <span className="text-white font-semibold">
                {selectedLeagueIds.size} {selectedLeagueIds.size === 1 ? 'League' : 'Leagues'} Selected
              </span>
              <button
                onClick={selectAllFiltered}
                className="text-sm text-gray-400 hover:text-white transition-colors"
              >
                Select All ({filteredLeagues.length})
              </button>
              <button
                onClick={clearSelection}
                className="text-sm text-gray-400 hover:text-white transition-colors"
              >
                Clear Selection
              </button>
            </div>
            <div className="flex items-center gap-3">
              <button
                onClick={() => setShowDeleteDialog(true)}
                className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 font-semibold transition-colors flex items-center gap-2"
              >
                <TrashIcon className="h-5 w-5" />
                Delete Selected
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
                  <span>{SPORT_ICONS[league.sport] || '🌐'}</span>
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
    </div>
  );
}
