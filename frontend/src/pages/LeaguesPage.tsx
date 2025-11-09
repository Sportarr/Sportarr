import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import type { League } from '../types';

// Sport categories for filtering
const SPORT_FILTERS = [
  { id: 'all', name: 'All Sports', icon: '🌐' },
  { id: 'Fighting', name: 'Fighting', icon: '🥊' },
  { id: 'Soccer', name: 'Soccer', icon: '⚽' },
  { id: 'Basketball', name: 'Basketball', icon: '🏀' },
  { id: 'Baseball', name: 'Baseball', icon: '⚾' },
  { id: 'Football', name: 'Football', icon: '🏈' },
  { id: 'Hockey', name: 'Hockey', icon: '🏒' },
  { id: 'Tennis', name: 'Tennis', icon: '🎾' },
  { id: 'Golf', name: 'Golf', icon: '⛳' },
  { id: 'Racing', name: 'Racing', icon: '🏎️' },
];

export default function LeaguesPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const navigate = useNavigate();

  const { data: leagues, isLoading, error, refetch } = useQuery({
    queryKey: ['leagues', selectedSport],
    queryFn: async () => {
      const params = selectedSport !== 'all' ? `?sport=${selectedSport}` : '';
      const response = await apiClient.get<League[]>(`/leagues${params}`);
      return response.data;
    },
  });

  const filteredLeagues = leagues?.filter(league =>
    league.name.toLowerCase().includes(searchQuery.toLowerCase())
  ) || [];

  // Group leagues by sport for statistics
  const leaguesBySport = leagues?.reduce((acc, league) => {
    acc[league.sport] = (acc[league.sport] || 0) + 1;
    return acc;
  }, {} as Record<string, number>) || {};

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
      <div className="mb-8">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-4xl font-bold text-white mb-2">Leagues</h1>
            <p className="text-gray-400">
              Manage your monitored leagues and competitions across all sports
            </p>
          </div>
          <button
            onClick={() => navigate('/add-league')}
            className="px-6 py-3 bg-red-600 text-white rounded-lg hover:bg-red-700 font-semibold transition-colors"
          >
            + Add League
          </button>
        </div>
      </div>

      {/* Sport Filter Tabs */}
      <div className="mb-8">
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
              {sport.id !== 'all' && leaguesBySport[sport.id] && (
                <span className="ml-1 px-2 py-0.5 bg-black/30 rounded text-xs">
                  {leaguesBySport[sport.id]}
                </span>
              )}
            </button>
          ))}
        </div>
      </div>

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
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
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
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
          {filteredLeagues.map((league) => (
            <div
              key={league.id}
              onClick={() => navigate(`/leagues/${league.id}`)}
              className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden hover:border-red-600/50 hover:shadow-lg hover:shadow-red-900/20 transition-all cursor-pointer group"
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

                {/* Sport Badge */}
                <div className="absolute top-2 left-2">
                  <span className="px-2 py-1 bg-black/70 backdrop-blur-sm text-white text-xs font-semibold rounded">
                    {SPORT_FILTERS.find(s => s.id === league.sport)?.icon || '🌐'} {league.sport}
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
                <div className="absolute bottom-2 left-2">
                  <span className="px-3 py-1 bg-black/70 backdrop-blur-sm text-white text-sm font-semibold rounded">
                    {league.eventCount || 0} {(league.eventCount || 0) === 1 ? 'Event' : 'Events'}
                  </span>
                </div>
              </div>

              {/* Info */}
              <div className="p-4">
                <h3 className="text-white font-bold text-lg mb-2 truncate">{league.name}</h3>

                {league.country && (
                  <p className="text-gray-400 text-sm mb-3">{league.country}</p>
                )}

                {/* Stats Row */}
                <div className="flex items-center gap-4 text-sm mb-3">
                  <div className="flex items-center gap-1">
                    <span className="text-gray-400">Monitored:</span>
                    <span className="text-white font-semibold">{league.monitoredEventCount || 0}</span>
                  </div>
                  <div className="flex items-center gap-1">
                    <span className="text-gray-400">Downloaded:</span>
                    <span className="text-white font-semibold">{league.fileCount || 0}</span>
                  </div>
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
          ))}
        </div>
      )}
    </div>
  );
}
