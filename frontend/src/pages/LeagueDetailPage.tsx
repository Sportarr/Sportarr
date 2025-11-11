import { useParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeftIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';

interface LeagueDetail {
  id: number;
  externalId?: string;
  name: string;
  sport: string;
  country?: string;
  description?: string;
  monitored: boolean;
  qualityProfileId?: number;
  logoUrl?: string;
  bannerUrl?: string;
  posterUrl?: string;
  website?: string;
  formedYear?: number;
  added: string;
  lastUpdate?: string;
  eventCount: number;
  monitoredEventCount: number;
  fileCount: number;
}

export default function LeagueDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: league, isLoading, error } = useQuery({
    queryKey: ['league', id],
    queryFn: async () => {
      const response = await apiClient.get<LeagueDetail>(`/leagues/${id}`);
      return response.data;
    },
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
      </div>
    );
  }

  if (error || !league) {
    return (
      <div className="p-8">
        <div className="max-w-4xl mx-auto">
          <button
            onClick={() => navigate('/leagues')}
            className="flex items-center gap-2 text-gray-400 hover:text-white mb-4 transition-colors"
          >
            <ArrowLeftIcon className="w-5 h-5" />
            Back to Leagues
          </button>
          <div className="text-center py-12">
            <p className="text-red-500 text-xl mb-4">League not found</p>
            <button
              onClick={() => navigate('/leagues')}
              className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
            >
              Go to Leagues
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8">
      <div className="max-w-6xl mx-auto">
        {/* Back Button */}
        <button
          onClick={() => navigate('/leagues')}
          className="flex items-center gap-2 text-gray-400 hover:text-white mb-6 transition-colors"
        >
          <ArrowLeftIcon className="w-5 h-5" />
          Back to Leagues
        </button>

        {/* League Header */}
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden mb-8">
          {/* Banner/Logo */}
          {(league.bannerUrl || league.logoUrl || league.posterUrl) && (
            <div className="relative h-64 bg-gray-800">
              <img
                src={league.bannerUrl || league.logoUrl || league.posterUrl}
                alt={league.name}
                className="w-full h-full object-cover"
              />
              <div className="absolute inset-0 bg-gradient-to-t from-black via-black/50 to-transparent"></div>
            </div>
          )}

          <div className="p-8">
            <div className="flex items-start justify-between">
              <div>
                <h1 className="text-4xl font-bold text-white mb-2">{league.name}</h1>
                <div className="flex items-center gap-4 text-gray-400">
                  <span className="px-3 py-1 bg-red-600/20 text-red-400 text-sm rounded font-medium">
                    {league.sport}
                  </span>
                  {league.country && (
                    <span className="text-sm">{league.country}</span>
                  )}
                  {league.formedYear && (
                    <span className="text-sm">Est. {league.formedYear}</span>
                  )}
                </div>
              </div>

              <div className="flex gap-3">
                {league.monitored ? (
                  <span className="px-4 py-2 bg-green-600 text-white text-sm font-semibold rounded-lg">
                    Monitored
                  </span>
                ) : (
                  <span className="px-4 py-2 bg-gray-600 text-white text-sm font-semibold rounded-lg">
                    Not Monitored
                  </span>
                )}
              </div>
            </div>

            {league.description && (
              <p className="text-gray-400 mt-4 leading-relaxed">
                {league.description}
              </p>
            )}

            {league.website && (
              <a
                href={league.website}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-2 mt-4 text-red-400 hover:text-red-300 transition-colors"
              >
                Visit Official Website →
              </a>
            )}
          </div>
        </div>

        {/* Stats */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
          <div className="bg-gray-900 border border-red-900/30 rounded-lg p-6">
            <div className="text-gray-400 text-sm mb-1">Total Events</div>
            <div className="text-3xl font-bold text-white">{league.eventCount}</div>
          </div>
          <div className="bg-gray-900 border border-red-900/30 rounded-lg p-6">
            <div className="text-gray-400 text-sm mb-1">Monitored Events</div>
            <div className="text-3xl font-bold text-green-400">{league.monitoredEventCount}</div>
          </div>
          <div className="bg-gray-900 border border-red-900/30 rounded-lg p-6">
            <div className="text-gray-400 text-sm mb-1">Downloaded Files</div>
            <div className="text-3xl font-bold text-blue-400">{league.fileCount}</div>
          </div>
        </div>

        {/* Events Section - Coming Soon */}
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-8 text-center">
          <h2 className="text-2xl font-bold text-white mb-4">Events</h2>
          <p className="text-gray-400">
            Event management for leagues is coming soon. For now, events are managed at the league level.
          </p>
        </div>
      </div>
    </div>
  );
}
