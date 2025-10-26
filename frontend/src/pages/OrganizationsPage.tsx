import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import OrganizationDetailsModal from '../components/OrganizationDetailsModal';
import apiClient from '../api/client';

interface Organization {
  name: string;
  eventCount: number;
  monitoredCount: number;
  fileCount: number;
  nextEvent?: {
    title: string;
    eventDate: string;
  };
  latestEvent: {
    id: number;
    title: string;
    eventDate: string;
  };
  posterUrl?: string;
}

export default function OrganizationsPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedOrganization, setSelectedOrganization] = useState<string | null>(null);

  const { data: organizations, isLoading, error, refetch } = useQuery({
    queryKey: ['organizations'],
    queryFn: async () => {
      const response = await apiClient.get<Organization[]>('/organizations');
      return response.data;
    },
  });

  const filteredOrganizations = organizations?.filter(org =>
    org.name.toLowerCase().includes(searchQuery.toLowerCase())
  ) || [];

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
          <p className="text-red-500 text-xl mb-4">Failed to load organizations</p>
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
        <h1 className="text-4xl font-bold text-white mb-2">Organizations</h1>
        <p className="text-gray-400">
          Manage your monitored organizations and their events
        </p>
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
            placeholder="Search organizations..."
            className="w-full pl-12 pr-4 py-3 bg-gray-900 border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all"
          />
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Total Organizations</p>
          <p className="text-3xl font-bold text-white">{organizations?.length || 0}</p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Total Events</p>
          <p className="text-3xl font-bold text-white">
            {organizations?.reduce((sum, org) => sum + org.eventCount, 0) || 0}
          </p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Monitored Events</p>
          <p className="text-3xl font-bold text-white">
            {organizations?.reduce((sum, org) => sum + org.monitoredCount, 0) || 0}
          </p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Downloaded</p>
          <p className="text-3xl font-bold text-white">
            {organizations?.reduce((sum, org) => sum + org.fileCount, 0) || 0}
          </p>
        </div>
      </div>

      {/* Organizations Grid */}
      {filteredOrganizations.length === 0 ? (
        <div className="text-center py-12">
          <p className="text-gray-400 text-lg">
            {searchQuery ? 'No organizations found' : 'No organizations yet'}
          </p>
          <p className="text-gray-500 text-sm mt-2">
            Add events to start tracking organizations
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
          {filteredOrganizations.map((org) => (
            <div
              key={org.name}
              onClick={() => setSelectedOrganization(org.name)}
              className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden hover:border-red-600/50 hover:shadow-lg hover:shadow-red-900/20 transition-all cursor-pointer group"
            >
              {/* Poster */}
              <div className="relative aspect-[2/3] bg-gray-800 overflow-hidden">
                {org.posterUrl ? (
                  <img
                    src={org.posterUrl}
                    alt={org.name}
                    className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                  />
                ) : (
                  <div className="w-full h-full flex items-center justify-center">
                    <span className="text-6xl font-bold text-gray-700">{org.name.charAt(0)}</span>
                  </div>
                )}

                {/* Status Badges */}
                <div className="absolute top-2 right-2 flex gap-2">
                  {org.monitoredCount > 0 && (
                    <span className="px-2 py-1 bg-red-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                      {org.monitoredCount} Monitored
                    </span>
                  )}
                </div>

                {/* Event Count Badge */}
                <div className="absolute bottom-2 left-2">
                  <span className="px-3 py-1 bg-black/70 backdrop-blur-sm text-white text-sm font-semibold rounded">
                    {org.eventCount} {org.eventCount === 1 ? 'Event' : 'Events'}
                  </span>
                </div>
              </div>

              {/* Info */}
              <div className="p-4">
                <h3 className="text-white font-bold text-lg mb-2 truncate">{org.name}</h3>

                {/* Stats Row */}
                <div className="flex items-center gap-4 text-sm mb-3">
                  <div className="flex items-center gap-1">
                    <span className="text-gray-400">Downloaded:</span>
                    <span className="text-white font-semibold">{org.fileCount}</span>
                  </div>
                </div>

                {/* Next Event */}
                {org.nextEvent && (
                  <div className="text-sm">
                    <p className="text-gray-400 mb-1">Next Event:</p>
                    <p className="text-white font-medium truncate">{org.nextEvent.title}</p>
                    <p className="text-gray-500 text-xs mt-1">
                      {new Date(org.nextEvent.eventDate).toLocaleDateString('en-US', {
                        month: 'short',
                        day: 'numeric',
                        year: 'numeric'
                      })}
                    </p>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Organization Details Modal */}
      {selectedOrganization && (
        <OrganizationDetailsModal
          organizationName={selectedOrganization}
          onClose={() => {
            setSelectedOrganization(null);
            refetch();
          }}
        />
      )}
    </div>
  );
}
