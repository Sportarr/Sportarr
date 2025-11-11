import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowLeftIcon, MagnifyingGlassIcon, ChevronDownIcon, ChevronUpIcon, UserIcon } from '@heroicons/react/24/outline';
import { CheckCircleIcon, XCircleIcon } from '@heroicons/react/24/solid';
import { useState } from 'react';
import apiClient from '../api/client';
import { toast } from 'sonner';
import ManualSearchModal from '../components/ManualSearchModal';

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
  homeScore?: number;
  awayScore?: number;
  status?: string;
  fights: FightDetail[];
}

interface FightDetail {
  id: number;
  fighter1: string;
  fighter2: string;
  weightClass?: string;
  isMainEvent: boolean;
  isTitleFight: boolean;
  fightOrder: number;
  result?: string;
  winner?: string;
}

interface QualityProfile {
  id: number;
  name: string;
}

export default function LeagueDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [expandedEvents, setExpandedEvents] = useState<Set<number>>(new Set());
  const [manualSearchModal, setManualSearchModal] = useState<{ isOpen: boolean; eventId: number; eventTitle: string }>({
    isOpen: false,
    eventId: 0,
    eventTitle: '',
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
  });

  // Fetch quality profiles
  const { data: qualityProfiles = [] } = useQuery({
    queryKey: ['quality-profiles'],
    queryFn: async () => {
      const response = await apiClient.get<QualityProfile[]>('/qualityprofiles');
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

  const toggleEventExpanded = (eventId: number) => {
    setExpandedEvents(prev => {
      const newSet = new Set(prev);
      if (newSet.has(eventId)) {
        newSet.delete(eventId);
      } else {
        newSet.add(eventId);
      }
      return newSet;
    });
  };

  const handleManualSearch = (eventId: number, eventTitle: string) => {
    setManualSearchModal({
      isOpen: true,
      eventId,
      eventTitle,
    });
  };

  const handleAutomaticSearch = async (eventId: number, eventTitle: string, qualityProfileId?: number) => {
    try {
      toast.info('Starting automatic search...', {
        description: `Searching indexers for ${eventTitle}`,
      });

      const response = await apiClient.post(`/event/${eventId}/automatic-search`, { qualityProfileId });

      if (response.data.success) {
        toast.success('Automatic search started', {
          description: `Task queued for ${eventTitle}. The system will automatically select and download the best release based on your quality profile and custom format scores.`,
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

        {/* Events Section */}
        <div className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden">
          <div className="p-6 border-b border-red-900/30">
            <h2 className="text-2xl font-bold text-white">Events</h2>
            <p className="text-gray-400 text-sm mt-1">
              {events.length} event{events.length !== 1 ? 's' : ''} in this league
            </p>
          </div>

          {eventsLoading ? (
            <div className="p-12 text-center">
              <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto"></div>
            </div>
          ) : events.length === 0 ? (
            <div className="p-12 text-center">
              <p className="text-gray-400">No events found for this league</p>
            </div>
          ) : (
            <div className="divide-y divide-red-900/30">
              {events.map(event => {
                const isExpanded = expandedEvents.has(event.id);
                const isCombatSport = event.sport.toLowerCase() === 'fighting';
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

                              {/* Team/Fighter Names */}
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
                                    ? `Use League Default (${qualityProfiles.find(p => p.id === league.qualityProfileId)?.name || 'Unknown'})`
                                    : 'No Quality Profile'}
                                </option>
                                {qualityProfiles.map(profile => (
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
                              title="Automatic Search - Automatically find and download the best release based on quality and custom format scores"
                            >
                              <MagnifyingGlassIcon className="w-4 h-4" />
                              Auto Search
                            </button>

                            {/* Expand Fights Button (Combat Sports Only) */}
                            {isCombatSport && event.fights.length > 0 && (
                              <button
                                onClick={() => toggleEventExpanded(event.id)}
                                className="px-3 py-1.5 bg-gray-700 hover:bg-gray-600 text-gray-200 text-sm rounded transition-colors flex items-center gap-2"
                              >
                                {isExpanded ? (
                                  <>
                                    <ChevronUpIcon className="w-4 h-4" />
                                    Hide Fights
                                  </>
                                ) : (
                                  <>
                                    <ChevronDownIcon className="w-4 h-4" />
                                    Show Fights ({event.fights.length})
                                  </>
                                )}
                              </button>
                            )}
                          </div>
                        </div>
                      </div>

                      {/* Expanded Fight Card Details (Combat Sports Only) */}
                      {isExpanded && isCombatSport && event.fights.length > 0 && (
                        <div className="mt-6 ml-10 space-y-3">
                          <h4 className="text-sm font-semibold text-gray-400 uppercase tracking-wider">
                            Fight Card
                          </h4>
                          {event.fights
                            .sort((a, b) => b.fightOrder - a.fightOrder)
                            .map(fight => (
                              <div
                                key={fight.id}
                                className="bg-gray-800/50 border border-gray-700 rounded-lg p-4"
                              >
                                <div className="flex items-center justify-between">
                                  <div className="flex-1">
                                    <div className="flex items-center gap-3 mb-2">
                                      <span className="text-white font-medium">
                                        {fight.fighter1} vs {fight.fighter2}
                                      </span>
                                      {fight.isMainEvent && (
                                        <span className="px-2 py-0.5 bg-red-600 text-white text-xs font-bold rounded">
                                          MAIN EVENT
                                        </span>
                                      )}
                                      {fight.isTitleFight && (
                                        <span className="px-2 py-0.5 bg-yellow-600 text-white text-xs font-bold rounded">
                                          TITLE FIGHT
                                        </span>
                                      )}
                                    </div>
                                    <div className="flex items-center gap-4 text-sm text-gray-400">
                                      {fight.weightClass && (
                                        <span>{fight.weightClass}</span>
                                      )}
                                      {fight.result && (
                                        <span className="text-blue-400">
                                          {fight.winner} wins by {fight.result}
                                        </span>
                                      )}
                                    </div>
                                  </div>
                                </div>
                              </div>
                            ))}
                        </div>
                      )}
                    </div>
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
      />
    </div>
  );
}
