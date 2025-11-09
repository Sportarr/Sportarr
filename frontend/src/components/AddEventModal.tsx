import { useState, Fragment, useEffect } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  CalendarIcon,
  MapPinIcon,
  GlobeAltIcon,
  CheckCircleIcon,
  ChevronDownIcon,
  TrophyIcon,
  TvIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import { useQualityProfiles } from '../api/hooks';
import apiClient from '../api/client';
import type { League, Team } from '../types';

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

interface AddEventModalProps {
  isOpen: boolean;
  onClose: () => void;
  event: {
    // Universal fields from TheSportsDB
    externalId?: string; // TheSportsDB event ID
    title: string;
    sport?: string; // Sport type from TheSportsDB (e.g., "Soccer", "Fighting", "Basketball")
    eventDate: string;
    venue?: string;
    location?: string;
    posterUrl?: string;
    broadcast?: string; // TV schedule information
    status?: string; // Event status (Scheduled, Live, Completed, etc.)
    season?: string; // Season identifier
    round?: string; // Round/week identifier

    // Combat sports specific
    organization?: string; // Promotion/Organization name for combat sports
    fights?: {
      fighter1: Fighter | string;
      fighter2: Fighter | string;
      weightClass?: string;
      isMainEvent: boolean;
    }[];

    // Team sports specific
    league?: League;
    leagueId?: number;
    homeTeam?: Team;
    homeTeamId?: number;
    awayTeam?: Team;
    awayTeamId?: number;
  };
  onSuccess: () => void;
}

export default function AddEventModal({ isOpen, onClose, event, onSuccess }: AddEventModalProps) {
  const [monitored, setMonitored] = useState(true);
  const [qualityProfileId, setQualityProfileId] = useState<number | null>(null);
  const [searchOnAdd, setSearchOnAdd] = useState(true);
  const [isAdding, setIsAdding] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);

  // Get sport type from TheSportsDB data
  // All events from TheSportsDB (via Fightarr-API) should include sport field
  const sport = event.sport || event.league?.sport || 'Unknown';

  // If sport is Unknown, this indicates an API issue
  if (sport === 'Unknown') {
    console.error('🚨 TheSportsDB API Error - Missing sport field:', event);
  }

  const isCombatSport = sport === 'Fighting' || sport === 'Boxing' || sport === 'MMA';
  const isTeamSport = !isCombatSport && sport !== 'Unknown';

  // Fight card type monitoring (1=EarlyPrelims, 2=Prelims, 3=MainCard, 4=FullEvent)
  // Only applicable to combat sports
  const [monitoredCardTypes, setMonitoredCardTypes] = useState({
    earlyPrelims: true,
    prelims: true,
    mainCard: true,
    fullEvent: false
  });

  const { data: qualityProfiles } = useQualityProfiles();

  const getFighterName = (fighter: Fighter | string): string => {
    return typeof fighter === 'string' ? fighter : fighter.name;
  };

  const formatDate = (dateString: string) => {
    try {
      const date = new Date(dateString);
      return date.toLocaleDateString('en-US', {
        weekday: 'long',
        year: 'numeric',
        month: 'long',
        day: 'numeric',
      });
    } catch {
      return dateString;
    }
  };

  const toggleCardType = (cardType: keyof typeof monitoredCardTypes) => {
    setMonitoredCardTypes(prev => ({
      ...prev,
      [cardType]: !prev[cardType]
    }));
  };

  const handleAdd = async () => {
    // VALIDATION: Check sport type
    // This should never happen if Fightarr-API is working correctly
    if (sport === 'Unknown') {
      console.error('🚨 Fightarr-API Integration Error - Event missing sport field:', event);
      toast.error('API Integration Error', {
        description: 'This event is missing sport classification from TheSportsDB. This indicates an issue with the Fightarr-API integration.',
      });
      return;
    }

    // VALIDATION: Check if quality profile is selected
    if (qualityProfileId === null) {
      toast.error('Quality Profile Required', {
        description: 'Please select a quality profile before adding this event.',
      });
      return;
    }

    // VALIDATION: Check if a default quality profile exists
    const hasDefaultProfile = qualityProfiles?.some((p: any) => p.isDefault);
    if (!hasDefaultProfile) {
      toast.error('Default Quality Profile Required', {
        description: 'Please set a default quality profile in Settings → Profiles before adding events.',
      });
      return;
    }

    setIsAdding(true);
    try {
      // Build request payload based on sport type
      const payload: any = {
        title: event.title,
        sport: sport,
        eventDate: event.eventDate,
        venue: event.venue,
        location: event.location,
        monitored,
        qualityProfileId,
        externalId: event.externalId, // TheSportsDB event ID
        broadcast: event.broadcast,
        status: event.status,
        season: event.season,
        round: event.round,
      };

      // Combat sports specific fields
      if (isCombatSport) {
        // Build array of monitored card type IDs (based on FightCardType enum)
        const monitoredCardTypeIds: number[] = [];
        if (monitoredCardTypes.earlyPrelims) monitoredCardTypeIds.push(1); // EarlyPrelims = 1
        if (monitoredCardTypes.prelims) monitoredCardTypeIds.push(2); // Prelims = 2
        if (monitoredCardTypes.mainCard) monitoredCardTypeIds.push(3); // MainCard = 3
        if (monitoredCardTypes.fullEvent) monitoredCardTypeIds.push(4); // FullEvent = 4

        payload.organization = event.organization;
        payload.monitoredCardTypes = monitoredCardTypeIds;
      }

      // Team sports specific fields
      if (isTeamSport) {
        payload.leagueId = event.leagueId || event.league?.id;
        payload.homeTeamId = event.homeTeamId || event.homeTeam?.id;
        payload.awayTeamId = event.awayTeamId || event.awayTeam?.id;
      }

      const response = await apiClient.post('/events', payload);

      // Check if event was already added
      if (response.data.alreadyAdded) {
        toast.info('Event Already Added', {
          description: `This event is already in your library.\nMonitored: ${response.data.monitored ? 'Yes' : 'No'}\n\nYou can change monitoring from the Organizations page.`,
        });
        onClose();
        return;
      }

      // Show success toast
      toast.success('Event Added Successfully', {
        description: `${event.title} has been added to your library.`,
      });

      // Call onSuccess to trigger refetch
      onSuccess();

      // Wait a moment for the refetch to start, then close modal
      setTimeout(() => {
        onClose();
      }, 100);
    } catch (error) {
      console.error('Failed to add event:', error);
      toast.error('Failed to Add Event', {
        description: error instanceof Error ? error.message : 'An unexpected error occurred. Please try again.',
      });
    } finally {
      setIsAdding(false);
    }
  };

  const isUpcoming = new Date(event.eventDate) > new Date();

  return (
    <Transition appear show={isOpen} as={Fragment}>
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4 text-center">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-4xl transform overflow-hidden rounded-2xl bg-gradient-to-br from-gray-900 to-black border border-red-900/30 text-left align-middle shadow-xl transition-all">
                {/* Header */}
                <div className="flex items-center justify-between px-6 py-4 border-b border-red-900/30">
                  <Dialog.Title as="h3" className="text-2xl font-bold text-white flex items-center">
                    <CheckCircleIcon className="w-7 h-7 text-red-600 mr-2" />
                    Add Event
                  </Dialog.Title>
                  <button
                    type="button"
                    className="text-gray-400 hover:text-white transition-colors"
                    onClick={onClose}
                  >
                    <XMarkIcon className="h-6 w-6" />
                  </button>
                </div>

                {/* Content */}
                <div className="p-6">
                  <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                    {/* Poster Column */}
                    <div className="md:col-span-1">
                      <div className="relative aspect-[2/3] bg-gray-950 rounded-lg overflow-hidden border border-red-900/30">
                        {event.posterUrl ? (
                          <img
                            src={event.posterUrl}
                            alt={event.title}
                            className="w-full h-full object-cover"
                          />
                        ) : (
                          <div className="w-full h-full flex items-center justify-center">
                            <svg
                              className="w-24 h-24 text-gray-700"
                              fill="none"
                              stroke="currentColor"
                              viewBox="0 0 24 24"
                            >
                              <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                strokeWidth={1}
                                d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z"
                              />
                            </svg>
                          </div>
                        )}
                        {isUpcoming && (
                          <div className="absolute top-2 left-2">
                            <span className="px-3 py-1 bg-green-600 text-white text-sm font-semibold rounded-full">
                              UPCOMING
                            </span>
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Details & Configuration Column */}
                    <div className="md:col-span-2 space-y-6">
                      {/* Event Info */}
                      <div>
                        <h4 className="text-2xl font-bold text-white mb-4">{event.title}</h4>

                        <div className="space-y-3">
                          {/* Sport Badge */}
                          <div className="flex items-center gap-2">
                            <TrophyIcon className="w-5 h-5 text-red-400" />
                            <span className={`px-3 py-1 font-semibold rounded-lg ${
                              sport === 'Unknown'
                                ? 'bg-yellow-900/30 text-yellow-400 border border-yellow-600'
                                : 'bg-red-900/30 text-red-400'
                            }`}>
                              {sport}
                            </span>
                          </div>

                          {/* Warning if sport is Unknown */}
                          {sport === 'Unknown' && (
                            <div className="bg-yellow-900/20 border border-yellow-600/50 rounded-lg p-3">
                              <p className="text-yellow-400 text-sm font-semibold mb-1">🚨 Fightarr-API Integration Error</p>
                              <p className="text-yellow-300/80 text-xs">
                                This event is missing sport classification from TheSportsDB. Check Fightarr-API response or network connection.
                              </p>
                            </div>
                          )}

                          {/* Combat Sports: Organization */}
                          {isCombatSport && event.organization && (
                            <div className="flex items-center text-gray-300">
                              <GlobeAltIcon className="w-5 h-5 mr-3 text-gray-500" />
                              <span className="font-semibold">{event.organization}</span>
                            </div>
                          )}

                          {/* Team Sports: League */}
                          {isTeamSport && (event.league || event.leagueId) && (
                            <div className="flex items-center text-gray-300">
                              <TrophyIcon className="w-5 h-5 mr-3 text-gray-500" />
                              <span className="font-semibold">
                                {event.league?.name || `League #${event.leagueId}`}
                              </span>
                            </div>
                          )}

                          {/* Team Sports: Teams */}
                          {isTeamSport && (event.homeTeam || event.awayTeam) && (
                            <div className="pl-8 space-y-2 border-l-2 border-red-900/30">
                              {event.homeTeam && (
                                <div className="flex items-center text-gray-300">
                                  <span className="text-gray-500 text-sm mr-2">Home:</span>
                                  <span className="font-semibold">{event.homeTeam.name}</span>
                                </div>
                              )}
                              {event.awayTeam && (
                                <div className="flex items-center text-gray-300">
                                  <span className="text-gray-500 text-sm mr-2">Away:</span>
                                  <span className="font-semibold">{event.awayTeam.name}</span>
                                </div>
                              )}
                            </div>
                          )}

                          {/* Season/Round Info */}
                          {(event.season || event.round) && (
                            <div className="flex items-center gap-3 text-sm text-gray-400">
                              {event.season && <span>Season: {event.season}</span>}
                              {event.round && <span>Round: {event.round}</span>}
                            </div>
                          )}

                          {/* Event Date */}
                          <div className="flex items-center text-gray-300">
                            <CalendarIcon className="w-5 h-5 mr-3 text-gray-500" />
                            <span>{formatDate(event.eventDate)}</span>
                          </div>

                          {/* Venue/Location */}
                          {(event.venue || event.location) && (
                            <div className="flex items-center text-gray-300">
                              <MapPinIcon className="w-5 h-5 mr-3 text-gray-500" />
                              <span>
                                {event.venue && event.location
                                  ? `${event.venue}, ${event.location}`
                                  : event.venue || event.location}
                              </span>
                            </div>
                          )}

                          {/* TV Broadcast Info */}
                          {event.broadcast && (
                            <div className="flex items-center text-gray-300">
                              <TvIcon className="w-5 h-5 mr-3 text-green-500" />
                              <span className="text-green-400 font-semibold">{event.broadcast}</span>
                            </div>
                          )}

                          {/* Event Status */}
                          {event.status && (
                            <div className="flex items-center gap-2">
                              <span className="px-3 py-1 bg-gray-800 text-gray-300 text-sm rounded">
                                Status: {event.status}
                              </span>
                            </div>
                          )}
                        </div>
                      </div>

                      {/* Configuration Form */}
                      <div className="space-y-4 bg-black/30 rounded-lg p-4 border border-red-900/20">
                        <h5 className="text-lg font-semibold text-white mb-3">Configuration</h5>

                        {/* Monitor Toggle */}
                        <div className="flex items-start space-x-3">
                          <input
                            type="checkbox"
                            id="monitored"
                            checked={monitored}
                            onChange={(e) => setMonitored(e.target.checked)}
                            className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                          />
                          <div className="flex-1">
                            <label htmlFor="monitored" className="text-white font-medium cursor-pointer">
                              Monitor Event
                            </label>
                            <p className="text-sm text-gray-400 mt-1">
                              Automatically search for and download this event when available
                            </p>
                          </div>
                        </div>

                        {/* Fight Card Type Selection - ONLY for Combat Sports */}
                        {monitored && isCombatSport && (
                          <div className="pl-8 space-y-3 border-l-2 border-red-900/30">
                            <p className="text-sm text-gray-400 mb-2">Select which fight card types to monitor:</p>

                            <div className="flex items-center space-x-3">
                              <input
                                type="checkbox"
                                id="earlyPrelims"
                                checked={monitoredCardTypes.earlyPrelims}
                                onChange={() => toggleCardType('earlyPrelims')}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                              />
                              <label htmlFor="earlyPrelims" className="text-gray-300 cursor-pointer">
                                Early Prelims
                              </label>
                            </div>

                            <div className="flex items-center space-x-3">
                              <input
                                type="checkbox"
                                id="prelims"
                                checked={monitoredCardTypes.prelims}
                                onChange={() => toggleCardType('prelims')}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                              />
                              <label htmlFor="prelims" className="text-gray-300 cursor-pointer">
                                Prelims
                              </label>
                            </div>

                            <div className="flex items-center space-x-3">
                              <input
                                type="checkbox"
                                id="mainCard"
                                checked={monitoredCardTypes.mainCard}
                                onChange={() => toggleCardType('mainCard')}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                              />
                              <label htmlFor="mainCard" className="text-gray-300 cursor-pointer">
                                Main Card
                              </label>
                            </div>

                            <div className="flex items-center space-x-3">
                              <input
                                type="checkbox"
                                id="fullEvent"
                                checked={monitoredCardTypes.fullEvent}
                                onChange={() => toggleCardType('fullEvent')}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                              />
                              <label htmlFor="fullEvent" className="text-gray-300 cursor-pointer">
                                Full Event
                              </label>
                            </div>
                          </div>
                        )}

                        {/* Team Sports Info - Show helpful message */}
                        {monitored && isTeamSport && (
                          <div className="pl-8 text-sm text-gray-400 border-l-2 border-red-900/30">
                            Team sports events will be downloaded as complete games
                          </div>
                        )}

                        {/* Quality Profile */}
                        <div>
                          <label htmlFor="qualityProfile" className="block text-white font-medium mb-2">
                            Quality Profile
                          </label>
                          <select
                            id="qualityProfile"
                            value={qualityProfileId ?? ''}
                            onChange={(e) => setQualityProfileId(e.target.value ? Number(e.target.value) : null)}
                            className={`w-full px-4 py-2.5 bg-gray-800 border rounded-lg focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all ${
                              qualityProfileId === null ? 'border-yellow-600 text-yellow-400' : 'border-gray-700 text-white'
                            }`}
                          >
                            <option value="">Select a Quality Profile *</option>
                            {qualityProfiles?.map((profile) => (
                              <option key={profile.id} value={profile.id}>
                                {profile.name}{profile.isDefault ? ' (Default)' : ''}
                              </option>
                            ))}
                          </select>
                          <p className="text-sm text-gray-400 mt-1">
                            Select the quality settings for downloads
                          </p>
                        </div>

                        {/* Advanced Options Toggle */}
                        <button
                          type="button"
                          onClick={() => setShowAdvanced(!showAdvanced)}
                          className="flex items-center text-red-400 hover:text-red-300 text-sm font-medium transition-colors"
                        >
                          <ChevronDownIcon
                            className={`w-4 h-4 mr-1 transition-transform ${
                              showAdvanced ? 'rotate-180' : ''
                            }`}
                          />
                          {showAdvanced ? 'Hide' : 'Show'} Advanced Options
                        </button>

                        {/* Advanced Options */}
                        {showAdvanced && (
                          <div className="pt-2 space-y-4 border-t border-red-900/20">
                            <div className="flex items-start space-x-3">
                              <input
                                type="checkbox"
                                id="searchOnAdd"
                                checked={searchOnAdd}
                                onChange={(e) => setSearchOnAdd(e.target.checked)}
                                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                              />
                              <div className="flex-1">
                                <label
                                  htmlFor="searchOnAdd"
                                  className="text-white font-medium cursor-pointer"
                                >
                                  Start search immediately
                                </label>
                                <p className="text-sm text-gray-400 mt-1">
                                  Begin searching for this event as soon as it's added
                                </p>
                              </div>
                            </div>
                          </div>
                        )}
                      </div>

                      {/* Fight Card Preview - ONLY for Combat Sports */}
                      {isCombatSport && event.fights && event.fights.length > 0 && (
                        <div className="bg-black/30 rounded-lg p-4 border border-red-900/20">
                          <h5 className="text-white font-semibold mb-3">
                            Fight Card ({event.fights.length} {event.fights.length === 1 ? 'fight' : 'fights'})
                          </h5>
                          <div className="space-y-2 max-h-40 overflow-y-auto">
                            {event.fights.slice(0, 8).map((fight, idx) => (
                              <div
                                key={idx}
                                className="flex items-center justify-between text-sm p-2 bg-gray-950/50 rounded"
                              >
                                <span className="text-white">
                                  {getFighterName(fight.fighter1)} vs {getFighterName(fight.fighter2)}
                                </span>
                                <div className="flex items-center gap-2">
                                  {fight.weightClass && (
                                    <span className="text-gray-400 text-xs">{fight.weightClass}</span>
                                  )}
                                  {fight.isMainEvent && (
                                    <span className="px-2 py-0.5 bg-red-600 text-white text-xs rounded-full">
                                      MAIN
                                    </span>
                                  )}
                                </div>
                              </div>
                            ))}
                            {event.fights.length > 8 && (
                              <p className="text-gray-500 text-xs text-center pt-2">
                                + {event.fights.length - 8} more fights
                              </p>
                            )}
                          </div>
                        </div>
                      )}

                      {/* Team Sports: Matchup Preview */}
                      {isTeamSport && (event.homeTeam || event.awayTeam) && (
                        <div className="bg-black/30 rounded-lg p-4 border border-red-900/20">
                          <h5 className="text-white font-semibold mb-3">Matchup</h5>
                          <div className="flex items-center justify-center gap-4 text-lg">
                            <div className="flex-1 text-center">
                              <div className="font-bold text-white mb-1">
                                {event.homeTeam?.name || 'TBD'}
                              </div>
                              {event.homeTeam?.shortName && (
                                <div className="text-sm text-gray-400">{event.homeTeam.shortName}</div>
                              )}
                            </div>
                            <div className="text-2xl font-bold text-red-500">VS</div>
                            <div className="flex-1 text-center">
                              <div className="font-bold text-white mb-1">
                                {event.awayTeam?.name || 'TBD'}
                              </div>
                              {event.awayTeam?.shortName && (
                                <div className="text-sm text-gray-400">{event.awayTeam.shortName}</div>
                              )}
                            </div>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                </div>

                {/* Footer Actions */}
                <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-red-900/30 bg-black/30">
                  <button
                    type="button"
                    onClick={onClose}
                    disabled={isAdding}
                    className="px-6 py-2.5 bg-gray-800 hover:bg-gray-700 text-white font-medium rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={handleAdd}
                    disabled={isAdding || sport === 'Unknown'}
                    className="px-6 py-2.5 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed disabled:transform-none flex items-center"
                  >
                    {isAdding ? (
                      <>
                        <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                        Adding...
                      </>
                    ) : (
                      <>
                        <CheckCircleIcon className="w-5 h-5 mr-2" />
                        Add Event
                      </>
                    )}
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
