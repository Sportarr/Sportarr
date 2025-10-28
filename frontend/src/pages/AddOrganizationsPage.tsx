import { useState, useCallback, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { MagnifyingGlassIcon, GlobeAltIcon, XMarkIcon, InformationCircleIcon } from '@heroicons/react/24/outline';
import AddEventSearchResult from '../components/AddEventSearchResult';
import AddEventModal from '../components/AddEventModal';
import AddOrganizationModal from '../components/AddOrganizationModal';
import apiClient from '../api/client';

interface Fighter {
  id: number;
  name: string;
  slug: string;
  nickname?: string;
  weightClass?: string;
  nationality?: string;
  wins: number;
  losses: number;
  draws: number;
  noContests: number;
  birthDate?: string;
  height?: string;
  reach?: string;
  imageUrl?: string;
  bio?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

interface SearchResult {
  tapologyId: string;
  title: string;
  organization: string;
  eventDate: string;
  venue?: string;
  location?: string;
  posterUrl?: string;
  fights?: {
    fighter1: Fighter | string;
    fighter2: Fighter | string;
    weightClass?: string;
    isMainEvent: boolean;
  }[];
}

interface Organization {
  id: number;
  name: string;
  slug: string;
  type?: string;
  country?: string;
  description?: string;
  logoUrl?: string;
  website?: string;
  isActive: boolean;
}

export default function AddOrganizationsPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedOrganization, setSelectedOrganization] = useState<Organization | null>(null);

  // Event search state
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState<SearchResult | null>(null);
  const [hasSearched, setHasSearched] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { data: organizations, isLoading, error: orgError, refetch } = useQuery({
    queryKey: ['metadata-organizations'],
    queryFn: async () => {
      const response = await apiClient.get<Organization[]>('/metadata/organizations');
      return response.data;
    },
  });

  // Debounced event search function
  const searchEvents = useCallback(async (query: string) => {
    if (!query || query.length < 1) {
      setSearchResults([]);
      setHasSearched(false);
      setError(null);
      return;
    }

    setIsSearching(true);
    setError(null);
    try {
      const response = await apiClient.get<SearchResult[]>('/search/events', {
        params: { q: query },
      });
      const results = Array.isArray(response.data) ? response.data : [];
      setSearchResults(results);
      setHasSearched(true);
    } catch (err) {
      console.error('Search failed:', err);
      setError('Failed to search events. Please try again.');
      setSearchResults([]);
      setHasSearched(true);
    } finally {
      setIsSearching(false);
    }
  }, []);

  // Handle search input with debounce
  useEffect(() => {
    const timeoutId = setTimeout(() => {
      searchEvents(searchQuery);
    }, 500);

    return () => clearTimeout(timeoutId);
  }, [searchQuery, searchEvents]);

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchQuery(e.target.value);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
    setSearchResults([]);
    setHasSearched(false);
    setError(null);
  };

  const handleSelectEvent = (event: SearchResult) => {
    setSelectedEvent(event);
  };

  const handleSelectOrganization = (org: Organization) => {
    setSelectedOrganization(org);
  };

  const handleCloseEventModal = () => {
    setSelectedEvent(null);
  };

  const handleCloseOrgModal = () => {
    setSelectedOrganization(null);
  };

  const handleAddEventSuccess = () => {
    setSearchQuery('');
    setSearchResults([]);
    setHasSearched(false);
  };

  // Filter organizations (no filter during event search, only show organizations when relevant)
  const filteredOrganizations = organizations || [];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
          <p className="text-gray-400">Loading organizations...</p>
        </div>
      </div>
    );
  }

  if (orgError) {
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
    <div className="p-8 max-w-7xl mx-auto">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-4xl font-bold text-white mb-2">Add New</h1>
        <p className="text-gray-400">
          Search for individual events or import entire organizations with all their events
        </p>
      </div>

      {/* Search Bar */}
      <div className="mb-8">
        <div className="relative max-w-3xl">
          <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none">
            <MagnifyingGlassIcon className="h-6 w-6 text-gray-400" />
          </div>
          <input
            type="text"
            value={searchQuery}
            onChange={handleSearchChange}
            placeholder="Search by event name, organization (e.g., UFC, Bellator), or fighter..."
            className="w-full pl-14 pr-12 py-4 bg-gray-900 border border-red-900/30 rounded-lg text-white text-lg placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all"
            autoFocus
          />
          {searchQuery && (
            <button
              onClick={handleClearSearch}
              className="absolute inset-y-0 right-0 pr-4 flex items-center text-gray-400 hover:text-white transition-colors"
            >
              <XMarkIcon className="h-6 w-6" />
            </button>
          )}
        </div>

        {/* Search Status */}
        <div className="mt-3 flex items-center justify-between max-w-3xl">
          {isSearching && (
            <p className="text-sm text-gray-400 flex items-center">
              <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-red-600 mr-2"></div>
              Searching Fightarr-API...
            </p>
          )}
          {!isSearching && hasSearched && searchResults.length > 0 && (
            <p className="text-sm text-gray-400">
              Found {searchResults.length} {searchResults.length === 1 ? 'event' : 'events'}
            </p>
          )}
        </div>
      </div>

      {/* Help Text - Show when no search */}
      {!hasSearched && !searchQuery && (
        <div className="max-w-3xl mb-8">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-blue-900/30 rounded-lg p-6">
            <div className="flex items-start">
              <InformationCircleIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
              <div>
                <h3 className="text-lg font-semibold text-white mb-2">How to Add Content</h3>
                <ul className="space-y-2 text-gray-300 text-sm">
                  <li className="flex items-start">
                    <span className="text-red-400 mr-2">•</span>
                    <span>Search for individual events by name, organization, or fighters</span>
                  </li>
                  <li className="flex items-start">
                    <span className="text-red-400 mr-2">•</span>
                    <span>Or browse organizations below to import all their events at once</span>
                  </li>
                  <li className="flex items-start">
                    <span className="text-red-400 mr-2">•</span>
                    <span>Click on an organization to configure monitoring settings (Main Cards, Prelims, Quality, etc.)</span>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Error Message */}
      {error && (
        <div className="mb-8 max-w-3xl">
          <div className="bg-red-950/30 border border-red-900/50 rounded-lg p-4">
            <p className="text-red-400">{error}</p>
          </div>
        </div>
      )}

      {/* Event Search Results */}
      {searchResults.length > 0 && (
        <div className="space-y-4 mb-12">
          <h2 className="text-2xl font-bold text-white mb-4">Event Search Results</h2>
          {searchResults.map((event) => (
            <AddEventSearchResult
              key={event.tapologyId}
              event={event}
              onSelect={() => handleSelectEvent(event)}
            />
          ))}
        </div>
      )}

      {/* No Event Results Message */}
      {hasSearched && !isSearching && searchResults.length === 0 && !error && searchQuery.length >= 1 && (
        <div className="text-center py-8 max-w-3xl mx-auto mb-12">
          <div className="inline-block p-6 bg-red-950/20 rounded-full border-2 border-red-900/30 mb-4">
            <MagnifyingGlassIcon className="w-12 h-12 text-red-600/50" />
          </div>
          <h3 className="text-xl font-bold text-white mb-2">No Events Found</h3>
          <p className="text-gray-400 mb-4">
            We couldn't find any events matching "{searchQuery}"
          </p>
          <p className="text-sm text-gray-500">
            Try browsing organizations below to import all their events
          </p>
        </div>
      )}

      {/* Organizations Section - Show when not actively searching for events or when no event results */}
      {(!hasSearched || searchResults.length === 0) && (
        <>
          <div className="mb-6">
            <h2 className="text-2xl font-bold text-white mb-2">Browse Organizations</h2>
            <p className="text-gray-400 text-sm">
              Click on an organization to import all their events and configure monitoring settings
            </p>
          </div>

          {organizations && organizations.length === 0 ? (
            <div className="text-center py-12">
              <p className="text-gray-400 text-lg">No organizations available</p>
            </div>
          ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
          {filteredOrganizations.map((org) => (
            <div
              key={org.id}
              onClick={() => handleSelectOrganization(org)}
              className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden hover:border-red-600/50 hover:shadow-lg hover:shadow-red-900/20 transition-all cursor-pointer group"
            >
              {/* Logo/Image */}
              <div className="relative aspect-[2/3] bg-gray-800 overflow-hidden">
                {org.logoUrl ? (
                  <img
                    src={org.logoUrl}
                    alt={org.name}
                    className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                  />
                ) : (
                  <div className="w-full h-full flex items-center justify-center">
                    <GlobeAltIcon className="w-24 h-24 text-gray-700" />
                  </div>
                )}

                {/* Active Badge */}
                {org.isActive && (
                  <div className="absolute top-2 right-2">
                    <span className="px-2 py-1 bg-green-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                      Active
                    </span>
                  </div>
                )}

                {/* Country Badge */}
                {org.country && (
                  <div className="absolute bottom-2 left-2">
                    <span className="px-3 py-1 bg-black/70 backdrop-blur-sm text-white text-sm font-semibold rounded">
                      {org.country}
                    </span>
                  </div>
                )}
              </div>

              {/* Info */}
              <div className="p-4">
                <h3 className="text-white font-bold text-lg mb-1 truncate">{org.name}</h3>
                {org.type && (
                  <p className="text-gray-400 text-sm mb-2">{org.type}</p>
                )}
                {org.description && (
                  <p className="text-gray-500 text-xs line-clamp-2">{org.description}</p>
                )}
              </div>
            </div>
          ))}
        </div>
          )}
        </>
      )}

      {/* Add Event Modal */}
      {selectedEvent && (
        <AddEventModal
          isOpen={!!selectedEvent}
          onClose={handleCloseEventModal}
          event={selectedEvent}
          onSuccess={handleAddEventSuccess}
        />
      )}

      {/* Add Organization Modal */}
      {selectedOrganization && (
        <AddOrganizationModal
          isOpen={!!selectedOrganization}
          onClose={handleCloseOrgModal}
          organizationName={selectedOrganization.name}
          onSuccess={() => {
            handleCloseOrgModal();
          }}
        />
      )}
    </div>
  );
}
