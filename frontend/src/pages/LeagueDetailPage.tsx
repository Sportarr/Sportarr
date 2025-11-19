import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowLeftIcon, MagnifyingGlassIcon, ChevronDownIcon, ChevronUpIcon, UserIcon, ArrowPathIcon, UsersIcon } from '@heroicons/react/24/outline';
import { CheckCircleIcon, XCircleIcon } from '@heroicons/react/24/solid';
import { useState } from 'react';
import apiClient from '../api/client';
import { toast } from 'sonner';
import ManualSearchModal from '../components/ManualSearchModal';
import AddLeagueModal from '../components/AddLeagueModal';
import ConfirmationModal from '../components/ConfirmationModal';

interface LeagueDetail {
  id: number;
  externalId?: string;
  name: string;
  sport: string;
  country?: string;
  description?: string;
  monitored: boolean;
  monitorType?: string;
  qualityProfileId?: number;
  searchForMissingEvents?: boolean;
  searchForCutoffUnmetEvents?: boolean;
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

interface EventDetail {
  id: number;
  externalId?: string;
  title: string;
  sport: string;
  leagueId?: number;
  leagueName?: string;
  homeTeamId?: number;
  homeTeamName?: string;
  awayTeamId?: number;
  awayTeamName?: string;
  season?: string;
  round?: string;
  eventDate: string;
  venue?: string;
  location?: string;
  broadcast?: string;
  monitored: boolean;
  hasFile: boolean;
  filePath?: string;
  fileSize?: number;
  quality?: string;
  qualityProfileId?: number;
  images: string[];
  added: string;
  lastUpdate?: string;
  homeScore?: string;
  awayScore?: string;
  status?: string;
}


interface QualityProfile {
  id: number;
  name: string;
}

export default function LeagueDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [manualSearchModal, setManualSearchModal] = useState<{ isOpen: boolean; eventId: number; eventTitle: string; part?: string }>({
    isOpen: false,
    eventId: 0,
    eventTitle: '',
  });
  const [isEditTeamsModalOpen, setIsEditTeamsModalOpen] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  // Track which seasons are expanded (default: current year)
  const currentYear = new Date().getFullYear().toString();
  const [expandedSeasons, setExpandedSeasons] = useState<Set<string>>(new Set([currentYear]));

  // Track which events have their multi-part sections expanded
  const [expandedEventParts, setExpandedEventParts] = useState<Set<number>>(new Set());

  // Fetch config to check if multi-part episodes are enabled
  const { data: config } = useQuery({
    queryKey: ['config'],
    queryFn: async () => {
      const response = await apiClient.get<{ enableMultiPartEpisodes: boolean }>('/config');
      return response.data;
    },
  });

  // Fetch league details
  const { data: league, isLoading, error } = useQuery({
    queryKey: ['league', id],
    queryFn: async () => {
      const response = await apiClient.get<LeagueDetail>(`/leagues/${id}`);
      return response.data;
    },
  });

  // Fetch events for this league
  const { data: events = [], isLoading: eventsLoading } = useQuery({
    queryKey: ['league-events', id],
    queryFn: async () => {
      const response = await apiClient.get<EventDetail[]>(`/leagues/${id}/events`);
      return response.data;
    },
    enabled: !!id,
    refetchInterval: (query) => {
      // Auto-refresh every 3 seconds if no events yet (sync in progress)
      // Stop polling once events appear
      const data = query.state.data;
      return (!data || data.length === 0) ? 3000 : false;
    },
  });

  // Fetch quality profiles
  const { data: qualityProfiles = [] } = useQuery({
    queryKey: ['quality-profiles'],
    queryFn: async () => {
      const response = await apiClient.get<QualityProfile[]>('/qualityprofile');
      return response.data;
    },
  });

  // Toggle event monitoring
  const toggleMonitorMutation = useMutation({
    mutationFn: async ({ eventId, monitored }: { eventId: number; monitored: boolean }) => {
      const response = await apiClient.put(`/events/${eventId}`, { monitored });
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['league-events', id] });
      queryClient.invalidateQueries({ queryKey: ['league', id] });
      queryClient.invalidateQueries({ queryKey: ['leagues'] }); // Update league stats
      toast.success('Event updated');
    },
    onError: () => {
      toast.error('Failed to update event');
    },
  });

  // Update event quality profile
  const updateQualityMutation = useMutation({
    mutationFn: async ({ eventId, qualityProfileId }: { eventId: number; qualityProfileId: number | null }) => {
      const response = await apiClient.put(`/events/${eventId}`, { qualityProfileId });
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['league-events', id] });
      toast.success('Quality profile updated');
    },
    onError: () => {
      toast.error('Failed to update quality profile');
    },
  });

  // Toggle league monitoring
  const toggleLeagueMonitorMutation = useMutation({
    mutationFn: async (monitored: boolean) => {
      const response = await apiClient.put(`/leagues/${id}`, { monitored });
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['league', id] });
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
      toast.success('League monitoring updated');
    },
    onError: () => {
      toast.error('Failed to update league monitoring');
    },
  });

  // Update league settings (monitor type, quality profile, search options)
  const updateLeagueSettingsMutation = useMutation({
    mutationFn: async (settings: {
      monitorType?: string;
      qualityProfileId?: number | null;
      searchForMissingEvents?: boolean;
      searchForCutoffUnmetEvents?: boolean;
    }) => {
      const response = await apiClient.put(`/leagues/${id}`, settings);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['league', id] });
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
      toast.success('League settings updated');
    },
    onError: () => {
      toast.error('Failed to update league settings');
    },
  });

  // Delete league
  const deleteLeagueMutation = useMutation({
    mutationFn: async () => {
      const response = await apiClient.delete(`/leagues/${id}`);
      return response.data;
    },
    onSuccess: async () => {
      toast.success('League deleted successfully');
      // Invalidate queries before navigating to ensure /leagues page is updated
      await queryClient.invalidateQueries({ queryKey: ['leagues'] });
      navigate('/leagues');
    },
    onError: (error: any) => {
      const errorMessage = error.response?.data?.error || 'Failed to delete league';
      toast.error(errorMessage);
    },
  });

  // Update monitored teams
  const updateTeamsMutation = useMutation({
    mutationFn: async (monitoredTeamIds: string[]) => {
      const response = await apiClient.put(`/leagues/${id}/teams`, {
        monitoredTeamIds: monitoredTeamIds.length > 0 ? monitoredTeamIds : null,
      });
      return response.data;
    },
    onSuccess: (data) => {
      const teamCount = data.teamCount || 0;
      const message = teamCount > 0
        ? `Updated monitored teams (${teamCount} team${teamCount !== 1 ? 's' : ''})`
        : 'League set to not monitored (no teams selected)';

      toast.success(message);
      setIsEditTeamsModalOpen(false);
      queryClient.invalidateQueries({ queryKey: ['league', id] });
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    },
    onError: (error: any) => {
      const errorMessage = error.response?.data?.error || 'Failed to update monitored teams';
      toast.error(errorMessage);
    },
  });


  const handleEditTeams = (league: any, monitoredTeamIds: string[]) => {
    updateTeamsMutation.mutate(monitoredTeamIds);
  };

  const handleManualSearch = (eventId: number, eventTitle: string, part?: string) => {
    setManualSearchModal({
      isOpen: true,
      eventId,
      eventTitle,
      part,
    });
  };

  const handleAutomaticSearch = async (eventId: number, eventTitle: string, qualityProfileId?: number, part?: string) => {
    try {
      const searchTarget = part ? `${eventTitle} (${part})` : eventTitle;
      toast.info('Starting automatic search...', {
        description: `Searching indexers for ${searchTarget}`,
      });

      const response = await apiClient.post(`/event/${eventId}/automatic-search`, { qualityProfileId, part });

      if (response.data.success) {
        toast.success('Automatic search started', {
          description: response.data.message || `Task queued for ${eventTitle}. Will download if missing or upgrade if better quality is available.`,
        });
      } else {
        toast.error('Automatic search failed', {
          description: response.data.message || 'Failed to queue automatic search',
        });
      }
    } catch (error) {
      console.error('Automatic search error:', error);
      toast.error('Automatic search failed', {
        description: 'Failed to start automatic search. Please try again.',
      });
    }
  };

  const handleLeagueAutomaticSearch = async () => {
    if (!id) return;

    try {
      toast.info('Starting league search...', {
        description: `Searching all monitored events in ${league?.name} for missing files and quality upgrades`,
      });

      const response = await apiClient.post(`/league/${id}/automatic-search`);

      if (response.data.success) {
        toast.success('League search started', {
          description: `${response.data.message}. Missing events will be downloaded and existing events will be upgraded if better quality is found.`,
        });
        // Refresh league data to update counts
        queryClient.invalidateQueries({ queryKey: ['league', id] });
      } else {
        toast.error('League search failed', {
          description: response.data.message || 'Failed to queue league search',
        });
      }
    } catch (error) {
      console.error('League search error:', error);
      toast.error('League search failed', {
        description: 'Failed to start league search. Please try again.',
      });
    }
  };

  const handleRefreshEvents = async () => {
    if (!id) return;

    try {
      toast.info('Refreshing events...', {
        description: `Fetching events from TheSportsDB for ${league?.name}`,
      });

      // Don't specify seasons - let the backend fetch all available seasons from TheSportsDB
      const response = await apiClient.post(`/leagues/${id}/refresh-events`, {});

      if (response.data.success) {
        toast.success('Events refreshed successfully', {
          description: `${response.data.newEvents} new events added, ${response.data.updatedEvents} updated, ${response.data.skippedEvents} skipped`,
        });
        // Refresh league data to show new events
        queryClient.invalidateQueries({ queryKey: ['league', id] });
        queryClient.invalidateQueries({ queryKey: ['league', id, 'events'] });
        queryClient.invalidateQueries({ queryKey: ['leagues'] }); // Update league stats
      } else {
        toast.error('Failed to refresh events', {
          description: response.data.message || 'Failed to fetch events from TheSportsDB',
        });
      }
    } catch (error) {
      console.error('Refresh events error:', error);
      toast.error('Failed to refresh events', {
        description: 'An error occurred while fetching events. Please try again.',
      });
    }
  };

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

  // Group events by season
  const groupedEvents = (events || []).reduce((acc, event) => {
    const season = event.season || 'Unknown';
    if (!acc[season]) {
      acc[season] = [];
    }
    acc[season].push(event);
    return acc;
  }, {} as Record<string, EventDetail[]>);

  // Sort seasons newest first
  const sortedSeasons = Object.keys(groupedEvents).sort((a, b) => {
    // Handle 'Unknown' season
    if (a === 'Unknown') return 1;
    if (b === 'Unknown') return -1;
    // Sort numerically for years
    return parseInt(b) - parseInt(a);
  });

  // Toggle season expansion
  const toggleSeason = (season: string) => {
    setExpandedSeasons(prev => {
      const newSet = new Set(prev);
      if (newSet.has(season)) {
        newSet.delete(season);
      } else {
        newSet.add(season);
      }
      return newSet;
    });
  };

  // Toggle event parts expansion (for multi-part episodes)
  const toggleEventParts = (eventId: number) => {
    setExpandedEventParts(prev => {
      const newSet = new Set(prev);
      if (newSet.has(eventId)) {
        newSet.delete(eventId);
      } else {
        newSet.add(eventId);
      }
      return newSet;
    });
  };

  // Helper to check if a sport is a fighting sport that supports multi-part episodes
  const isFightingSport = (sport: string) => {
    const fightingSports = ['Fighting', 'MMA', 'UFC', 'Boxing', 'Kickboxing', 'Wrestling'];
    return fightingSports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
  };

  // Multi-part episode segments for Fighting sports
  const fightCardParts = [
    { name: 'Early Prelims', label: 'Early Prelims' },
    { name: 'Prelims', label: 'Prelims' },
    { name: 'Main Card', label: 'Main Card' },
  ];

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

              <div className="flex flex-col gap-3">
                <button
                  onClick={() => toggleLeagueMonitorMutation.mutate(!league.monitored)}
                  disabled={toggleLeagueMonitorMutation.isPending}
                  className={`px-4 py-2 text-white text-sm font-semibold rounded-lg transition-colors ${
                    league.monitored
                      ? 'bg-green-600 hover:bg-green-700'
                      : 'bg-gray-600 hover:bg-gray-700'
                  } ${toggleLeagueMonitorMutation.isPending ? 'opacity-50 cursor-not-allowed' : ''}`}
                  title="Toggle league monitoring - When monitored, events will be tracked for downloads"
                >
                  {league.monitored ? 'Monitored' : 'Not Monitored'}
                </button>
                <button
                  onClick={() => setIsEditTeamsModalOpen(true)}
                  className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold rounded-lg transition-colors flex items-center justify-center gap-2"
                  title="Edit monitored teams and monitoring settings"
                >
                  <UsersIcon className="w-4 h-4" />
                  Edit
                </button>
                <button
                  onClick={() => setShowDeleteConfirm(true)}
                  disabled={deleteLeagueMutation.isPending}
                  className="px-4 py-2 bg-red-600/80 hover:bg-red-700 text-white text-sm font-semibold rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  title="Remove league from library"
                >
                  {deleteLeagueMutation.isPending ? 'Deleting...' : 'Delete League'}
                </button>
              </div>
            </div>

            {league.description && (
              <p className="text-gray-400 mt-4 leading-relaxed">
                {league.description}
              </p>
            )}

            {league.website && (
              <a
                href={league.website.startsWith('http://') || league.website.startsWith('https://')
                  ? league.website
                  : `https://${league.website}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-2 mt-4 text-red-400 hover:text-red-300 transition-colors"
              >
                Visit Official Website →
              </a>
            )}

            {/* League-Level Search Actions (Sonarr-style show/season search) */}
            <div className="mt-6 pt-6 border-t border-red-900/30">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">Search All Monitored Events</h3>
                  <p className="text-xs text-gray-400">
                    Search all monitored events for missing files and quality upgrades
                  </p>
                </div>
                <div className="flex gap-2">
                  <button
                    onClick={handleLeagueAutomaticSearch}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white text-sm font-medium rounded transition-colors flex items-center gap-2"
                    title="Search all monitored events - downloads missing files and upgrades existing files if better quality is available"
                  >
                    <MagnifyingGlassIcon className="w-4 h-4" />
                    Search League
                  </button>
                  <button
                    onClick={handleRefreshEvents}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded transition-colors flex items-center gap-2"
                    title="Refresh events from TheSportsDB API - fetches and adds new events to the league"
                  >
                    <ArrowPathIcon className="w-4 h-4" />
                    Refresh Events
                  </button>
                </div>
              </div>
            </div>

            {/* Monitoring Settings */}
            <div className="mt-6 pt-6 border-t border-red-900/30">
              <h3 className="text-sm font-semibold text-white mb-4">Monitoring Settings</h3>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* Monitor Type */}
                <div>
                  <label className="block text-xs font-medium text-gray-400 mb-2">
                    Monitor Events
                  </label>
                  <select
                    value={league?.monitorType || 'Future'}
                    onChange={(e) => updateLeagueSettingsMutation.mutate({ monitorType: e.target.value })}
                    disabled={updateLeagueSettingsMutation.isPending}
                    className="w-full px-3 py-2 bg-black border border-red-900/30 rounded text-white text-sm focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600 disabled:opacity-50"
                  >
                    <option value="All">All Events</option>
                    <option value="Future">Future Events</option>
                    <option value="CurrentSeason">Current Season</option>
                    <option value="LatestSeason">Latest Season</option>
                    <option value="NextSeason">Next Season</option>
                    <option value="Recent">Recent (30 days)</option>
                    <option value="None">None</option>
                  </select>
                </div>

                {/* Quality Profile */}
                <div>
                  <label className="block text-xs font-medium text-gray-400 mb-2">
                    Quality Profile
                  </label>
                  <select
                    value={league?.qualityProfileId || ''}
                    onChange={(e) => updateLeagueSettingsMutation.mutate({
                      qualityProfileId: e.target.value ? parseInt(e.target.value) : null
                    })}
                    disabled={updateLeagueSettingsMutation.isPending}
                    className="w-full px-3 py-2 bg-black border border-red-900/30 rounded text-white text-sm focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600 disabled:opacity-50"
                  >
                    <option value="">No Quality Profile</option>
                    {qualityProfiles.map(profile => (
                      <option key={profile.id} value={profile.id}>
                        {profile.name}
                      </option>
                    ))}
                  </select>
                </div>

                {/* Search for Missing Events */}
                <div>
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={league?.searchForMissingEvents || false}
                      onChange={(e) => updateLeagueSettingsMutation.mutate({
                        searchForMissingEvents: e.target.checked
                      })}
                      disabled={updateLeagueSettingsMutation.isPending}
                      className="w-4 h-4 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2 disabled:opacity-50"
                    />
                    <div>
                      <div className="text-xs font-medium text-white">Search on add/update</div>
                      <div className="text-xs text-gray-400">Search when league settings change</div>
                    </div>
                  </label>
                </div>

                {/* Search for Cutoff Unmet Events */}
                <div>
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={league?.searchForCutoffUnmetEvents || false}
                      onChange={(e) => updateLeagueSettingsMutation.mutate({
                        searchForCutoffUnmetEvents: e.target.checked
                      })}
                      disabled={updateLeagueSettingsMutation.isPending}
                      className="w-4 h-4 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2 disabled:opacity-50"
                    />
                    <div>
                      <div className="text-xs font-medium text-white">Search for upgrades on add/update</div>
                      <div className="text-xs text-gray-400">Search for quality upgrades when settings change</div>
                    </div>
                  </label>
                </div>
              </div>
            </div>
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

        {/* Events Section */}
        <div className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden">
          <div className="p-6 border-b border-red-900/30">
            <h2 className="text-2xl font-bold text-white">Events</h2>
            <p className="text-gray-400 text-sm mt-1">
              {Array.isArray(events) ? events.length : 0} event{Array.isArray(events) && events.length !== 1 ? 's' : ''} in this league
            </p>
          </div>

          {eventsLoading ? (
            <div className="p-12 text-center">
              <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto"></div>
            </div>
          ) : !Array.isArray(events) || events.length === 0 ? (
            <div className="p-12 text-center">
              <p className="text-gray-400">No events found for this league</p>
            </div>
          ) : (
            <div>
              {/* Season Groups */}
              {sortedSeasons.map(season => {
                const seasonEvents = groupedEvents[season];
                const isExpanded = expandedSeasons.has(season);
                const monitoredCount = seasonEvents.filter(e => e.monitored).length;
                const hasFileCount = seasonEvents.filter(e => e.hasFile).length;

                return (
                  <div key={season} className="border-b border-red-900/30 last:border-b-0">
                    {/* Season Header */}
                    <button
                      onClick={() => toggleSeason(season)}
                      className="w-full p-6 flex items-center justify-between hover:bg-gray-800/30 transition-colors text-left"
                    >
                      <div className="flex items-center gap-4">
                        {isExpanded ? (
                          <ChevronDownIcon className="w-5 h-5 text-gray-400" />
                        ) : (
                          <ChevronUpIcon className="w-5 h-5 text-gray-400 transform rotate-180" />
                        )}
                        <div>
                          <h3 className="text-xl font-bold text-white">
                            {season === 'Unknown' ? 'No Season Info' : `Season ${season}`}
                          </h3>
                          <p className="text-sm text-gray-400 mt-1">
                            {seasonEvents.length} event{seasonEvents.length !== 1 ? 's' : ''}
                            {monitoredCount > 0 && ` • ${monitoredCount} monitored`}
                            {hasFileCount > 0 && ` • ${hasFileCount} downloaded`}
                          </p>
                        </div>
                      </div>
                      <div className="text-sm text-gray-500">
                        {isExpanded ? 'Click to collapse' : 'Click to expand'}
                      </div>
                    </button>

                    {/* Season Events */}
                    {isExpanded && (
                      <div className="divide-y divide-red-900/30">
                        {seasonEvents.map(event => {
                const hasFile = event.hasFile;
                const eventDate = new Date(event.eventDate);
                const isPast = eventDate < new Date();

                return (
                  <div key={event.id} className="hover:bg-gray-800/50 transition-colors">
                    {/* Event Row */}
                    <div className="p-6">
                      <div className="flex items-start gap-4">
                        {/* Monitor Checkbox */}
                        <div className="flex-shrink-0 pt-1">
                          <button
                            onClick={() => toggleMonitorMutation.mutate({
                              eventId: event.id,
                              monitored: !event.monitored
                            })}
                            className="focus:outline-none focus:ring-2 focus:ring-red-500 rounded"
                            disabled={toggleMonitorMutation.isPending}
                          >
                            {event.monitored ? (
                              <CheckCircleIcon className="w-6 h-6 text-green-500" />
                            ) : (
                              <XCircleIcon className="w-6 h-6 text-gray-600" />
                            )}
                          </button>
                        </div>

                        {/* Event Info */}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-start justify-between gap-4">
                            <div className="flex-1">
                              <h3 className="text-lg font-semibold text-white mb-1">
                                {event.title}
                              </h3>

                              <div className="flex flex-wrap items-center gap-3 text-sm text-gray-400 mb-2">
                                <span>{eventDate.toLocaleDateString('en-US', {
                                  year: 'numeric',
                                  month: 'short',
                                  day: 'numeric'
                                })}</span>

                                {event.round && (
                                  <span className="px-2 py-0.5 bg-red-600/20 text-red-400 rounded">
                                    {event.round}
                                  </span>
                                )}

                                {event.status && (
                                  <span className={`px-2 py-0.5 rounded ${
                                    event.status.toLowerCase() === 'completed' ? 'bg-blue-600/20 text-blue-400' :
                                    event.status.toLowerCase() === 'live' ? 'bg-green-600/20 text-green-400' :
                                    'bg-gray-600/20 text-gray-400'
                                  }`}>
                                    {event.status}
                                  </span>
                                )}
                              </div>

                              {/* Team Names */}
                              {event.homeTeamName && event.awayTeamName && (
                                <div className="text-sm text-gray-300 mb-2">
                                  {event.homeTeamName} vs {event.awayTeamName}
                                  {event.homeScore !== undefined && event.awayScore !== undefined && (
                                    <span className="ml-2 text-gray-400">
                                      ({event.homeScore} - {event.awayScore})
                                    </span>
                                  )}
                                </div>
                              )}

                              {event.venue && (
                                <div className="text-sm text-gray-400">
                                  {event.venue}
                                  {event.location && `, ${event.location}`}
                                </div>
                              )}
                            </div>

                            {/* File Status Badge */}
                            {hasFile && (
                              <div className="flex-shrink-0">
                                <span className="px-3 py-1 bg-green-600 text-white text-xs font-semibold rounded">
                                  Downloaded
                                </span>
                              </div>
                            )}
                          </div>

                          {/* Quality Profile & Actions */}
                          <div className="flex items-center gap-3 mt-4">
                            {/* Quality Profile Dropdown */}
                            <div className="flex-1 max-w-xs">
                              <select
                                value={event.qualityProfileId || league?.qualityProfileId || ''}
                                onChange={(e) => updateQualityMutation.mutate({
                                  eventId: event.id,
                                  qualityProfileId: e.target.value ? Number(e.target.value) : null
                                })}
                                className="w-full px-3 py-1.5 bg-gray-800 border border-gray-700 text-gray-200 text-sm rounded focus:outline-none focus:ring-2 focus:ring-red-500"
                                disabled={updateQualityMutation.isPending}
                              >
                                <option value="">
                                  {league?.qualityProfileId
                                    ? `Use League Default (${Array.isArray(qualityProfiles) ? qualityProfiles.find(p => p.id === league.qualityProfileId)?.name || 'Unknown' : 'Unknown'})`
                                    : 'No Quality Profile'}
                                </option>
                                {Array.isArray(qualityProfiles) && qualityProfiles.map(profile => (
                                  <option key={profile.id} value={profile.id}>
                                    {profile.name}
                                    {event.qualityProfileId === profile.id && ' (Custom)'}
                                  </option>
                                ))}
                              </select>
                            </div>

                            {/* Search Buttons */}
                            <button
                              onClick={() => handleManualSearch(event.id, event.title)}
                              className="px-4 py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-sm font-medium rounded transition-colors flex items-center gap-2"
                              title="Manual Search - Browse and select from available releases"
                            >
                              <UserIcon className="w-4 h-4" />
                              Manual Search
                            </button>

                            <button
                              onClick={() => handleAutomaticSearch(event.id, event.title, event.qualityProfileId || league?.qualityProfileId)}
                              className="px-4 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm font-medium rounded transition-colors flex items-center gap-2"
                              title={config?.enableMultiPartEpisodes && isFightingSport(event.sport)
                                ? "Automatic Search - Automatically searches for all fight card parts (Early Prelims, Prelims, Main Card)"
                                : "Automatic Search - Downloads missing file or upgrades existing file if better quality is found"}
                            >
                              <MagnifyingGlassIcon className="w-4 h-4" />
                              Auto Search
                            </button>

                            {/* Multi-Part Toggle Button (only for Fighting sports when enabled) - For manual granular control */}
                            {config?.enableMultiPartEpisodes && isFightingSport(event.sport) && (
                              <button
                                onClick={() => toggleEventParts(event.id)}
                                className="px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded transition-colors flex items-center gap-2"
                                title="Manually search for specific fight card segments"
                              >
                                <ChevronDownIcon className={`w-4 h-4 transition-transform ${expandedEventParts.has(event.id) ? 'rotate-180' : ''}`} />
                                Parts
                              </button>
                            )}

                          </div>

                          {/* Multi-Part Episode Search Section - For manual granular control */}
                          {config?.enableMultiPartEpisodes && isFightingSport(event.sport) && expandedEventParts.has(event.id) && (
                            <div className="mt-4 p-4 bg-blue-950/20 border border-blue-900/30 rounded-lg">
                              <h4 className="text-sm font-semibold text-blue-300 mb-3">Manual Search by Segment</h4>
                              <p className="text-xs text-gray-400 mb-3">
                                Manually search for specific parts of this fight card. Note: The main "Auto Search" button above automatically searches all parts - use this section only if you need granular control over individual segments.
                              </p>
                              <div className="space-y-2">
                                {fightCardParts.map((part) => (
                                  <div key={part.name} className="flex items-center justify-between p-3 bg-gray-800/50 rounded border border-gray-700">
                                    <div className="flex-1">
                                      <span className="text-white font-medium">{part.label}</span>
                                      <p className="text-xs text-gray-400 mt-0.5">
                                        Manually search for {part.label} only
                                      </p>
                                    </div>
                                    <div className="flex gap-2">
                                      <button
                                        onClick={() => handleManualSearch(event.id, event.title, part.name)}
                                        className="px-3 py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-xs font-medium rounded transition-colors flex items-center gap-1.5"
                                        title={`Browse and select ${part.label} releases`}
                                      >
                                        <UserIcon className="w-3.5 h-3.5" />
                                        Manual
                                      </button>
                                      <button
                                        onClick={() => handleAutomaticSearch(event.id, event.title, event.qualityProfileId || league?.qualityProfileId, part.name)}
                                        className="px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white text-xs font-medium rounded transition-colors flex items-center gap-1.5"
                                        title={`Automatically download ${part.label} only`}
                                      >
                                        <MagnifyingGlassIcon className="w-3.5 h-3.5" />
                                        Auto
                                      </button>
                                    </div>
                                  </div>
                                ))}
                              </div>
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                );
              })}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Manual Search Modal */}
      <ManualSearchModal
        isOpen={manualSearchModal.isOpen}
        onClose={() => setManualSearchModal({ ...manualSearchModal, isOpen: false })}
        eventId={manualSearchModal.eventId}
        eventTitle={manualSearchModal.eventTitle}
        part={manualSearchModal.part}
      />

      {/* Edit Teams Modal */}
      {league && league.externalId && (
        <AddLeagueModal
          league={{
            idLeague: league.externalId,
            strLeague: league.name,
            strSport: league.sport,
            strCountry: league.country,
            strLeagueAlternate: undefined,
            strDescriptionEN: league.description,
            strBadge: league.logoUrl,
            strLogo: league.logoUrl,
            strBanner: league.bannerUrl,
            strPoster: league.posterUrl,
            strWebsite: league.website,
            intFormedYear: league.formedYear?.toString(),
          }}
          isOpen={isEditTeamsModalOpen}
          onClose={() => setIsEditTeamsModalOpen(false)}
          onAdd={handleEditTeams}
          isAdding={updateTeamsMutation.isPending}
          editMode={true}
          leagueId={league.id}
        />
      )}

      {/* Delete Confirmation Modal */}
      {league && (
        <ConfirmationModal
          isOpen={showDeleteConfirm}
          onClose={() => setShowDeleteConfirm(false)}
          onConfirm={() => {
            deleteLeagueMutation.mutate();
            setShowDeleteConfirm(false);
          }}
          title="Delete League"
          message={`Are you sure you want to delete "${league.name}"? This will remove the league${
            league.eventCount > 0 ? ` and all ${league.eventCount} event${league.eventCount !== 1 ? 's' : ''}` : ''
          } from your library.`}
          confirmText="Delete League"
          confirmButtonClass="bg-red-600 hover:bg-red-700"
          isLoading={deleteLeagueMutation.isPending}
        />
      )}
    </div>
  );
}
