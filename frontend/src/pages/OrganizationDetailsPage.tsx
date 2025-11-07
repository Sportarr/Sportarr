import { useState } from 'react';
import { toast } from 'sonner';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  ArrowLeftIcon,
  CheckCircleIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  MagnifyingGlassIcon,
  UserIcon,
  ChartBarIcon,
  ArrowPathIcon,
  ClockIcon
} from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import { useQualityProfiles } from '../api/hooks';
import type { Event, FightCard } from '../types';
import ManualSearchModal from '../components/ManualSearchModal';
import PreviewRenameModal from '../components/PreviewRenameModal';
import HistoryModal from '../components/HistoryModal';

export default function OrganizationDetailsPage() {
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [expandedEvents, setExpandedEvents] = useState<Set<number>>(new Set());
  const [updatingCardId, setUpdatingCardId] = useState<number | null>(null);
  const [updatingEventId, setUpdatingEventId] = useState<number | null>(null);
  const [isUpdatingOrganization, setIsUpdatingOrganization] = useState(false);

  // Load quality profiles
  const { data: qualityProfiles } = useQualityProfiles();

  // Modal states
  const [manualSearchModal, setManualSearchModal] = useState<{
    isOpen: boolean;
    type: 'organization' | 'event' | 'fightcard';
    title: string;
    params: { organizationName?: string; eventId?: number; fightCardId?: number };
  }>({
    isOpen: false,
    type: 'organization',
    title: '',
    params: {},
  });

  const [previewRenameModal, setPreviewRenameModal] = useState<{
    isOpen: boolean;
    type: 'organization' | 'event' | 'fightcard';
    title: string;
    params: { organizationName?: string; eventId?: number; fightCardId?: number };
  }>({
    isOpen: false,
    type: 'organization',
    title: '',
    params: {},
  });

  const [historyModal, setHistoryModal] = useState<{
    isOpen: boolean;
    type: 'organization' | 'event' | 'fightcard';
    title: string;
    params: { organizationName?: string; eventId?: number; fightCardId?: number };
  }>({
    isOpen: false,
    type: 'organization',
    title: '',
    params: {},
  });

  const { data: events, isLoading, refetch } = useQuery({
    queryKey: ['organization-events', name],
    queryFn: async () => {
      const response = await apiClient.get<Event[]>(`/organizations/${encodeURIComponent(name!)}/events`);
      return response.data;
    },
    enabled: !!name,
  });

  const toggleEvent = (eventId: number) => {
    const newExpanded = new Set(expandedEvents);
    if (newExpanded.has(eventId)) {
      newExpanded.delete(eventId);
    } else {
      newExpanded.add(eventId);
    }
    setExpandedEvents(newExpanded);
  };

  const handleToggleFightCardMonitor = async (cardId: number, currentStatus: boolean) => {
    setUpdatingCardId(cardId);
    try {
      const response = await fetch(`/api/fightcards/${cardId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          monitored: !currentStatus,
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to update fight card monitor status');
      }

      await refetch();
    } catch (error) {
      console.error('Failed to toggle fight card monitor:', error);
      toast.error('Update Failed', {
        description: 'Failed to update fight card monitor status. Please try again.',
      });
    } finally {
      setUpdatingCardId(null);
    }
  };

  const handleAddEventToLibrary = async (event: Event) => {
    // Check if a default quality profile exists
    const defaultProfile = qualityProfiles?.find((p: any) => p.isDefault);
    if (!defaultProfile) {
      toast.error('Default Quality Profile Required', {
        description: 'Please set a default quality profile in Settings → Profiles before adding events.',
      });
      return;
    }

    setUpdatingEventId(event.id);
    try {
      const response = await fetch('/api/events', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          title: event.title,
          organization: event.organization,
          eventDate: event.eventDate,
          venue: event.venue,
          location: event.location,
          monitored: true,
          qualityProfileId: defaultProfile.id,
          images: event.images,
          monitoredCardTypes: [1, 2, 3], // Monitor Early Prelims, Prelims, Main Card by default
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to add event to library');
      }

      await refetch();
      toast.success('Event Added', {
        description: `${event.title} has been added to your library and is now monitored.`,
      });
    } catch (error) {
      console.error('Failed to add event:', error);
      toast.error('Add Failed', {
        description: 'Failed to add event to library. Please try again.',
      });
    } finally {
      setUpdatingEventId(null);
    }
  };

  const handleToggleEventMonitor = async (event: Event) => {
    setUpdatingEventId(event.id);
    try {
      const response = await fetch(`/api/events/${event.id}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          ...event,
          monitored: !event.monitored,
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to update event monitor status');
      }

      await refetch();
    } catch (error) {
      console.error('Failed to toggle event monitor:', error);
      toast.error('Update Failed', {
        description: 'Failed to update event monitor status. Please try again.',
      });
    } finally {
      setUpdatingEventId(null);
    }
  };

  const handleUpdateEventQualityProfile = async (eventId: number, qualityProfileId: number | null) => {
    setUpdatingEventId(eventId);
    try {
      const response = await fetch(`/api/events/${eventId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          qualityProfileId,
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to update event quality profile');
      }

      await refetch();
      toast.success('Quality Profile Updated', {
        description: 'Event quality profile has been updated successfully.',
      });
    } catch (error) {
      console.error('Failed to update event quality profile:', error);
      toast.error('Update Failed', {
        description: 'Failed to update event quality profile. Please try again.',
      });
    } finally {
      setUpdatingEventId(null);
    }
  };

  const handleUpdateFightCardQualityProfile = async (cardId: number, qualityProfileId: number | null) => {
    setUpdatingCardId(cardId);
    try {
      const response = await fetch(`/api/fightcards/${cardId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          qualityProfileId,
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to update fight card quality profile');
      }

      await refetch();
      toast.success('Quality Profile Updated', {
        description: 'Fight card quality profile has been updated successfully.',
      });
    } catch (error) {
      console.error('Failed to update fight card quality profile:', error);
      toast.error('Update Failed', {
        description: 'Failed to update fight card quality profile. Please try again.',
      });
    } finally {
      setUpdatingCardId(null);
    }
  };

  const handleUpdateAllFightCardsQuality = async (eventId: number, qualityProfileId: number | null, event: Event) => {
    setUpdatingEventId(eventId);
    try {
      // Update all fight cards for this event
      const updatePromises = event.fightCards?.map(card =>
        fetch(`/api/fightcards/${card.id}`, {
          method: 'PUT',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            qualityProfileId,
          }),
        })
      ) || [];

      await Promise.all(updatePromises);
      await refetch();
      toast.success('Quality Profiles Updated', {
        description: 'All fight cards have been updated with the selected quality profile.',
      });
    } catch (error) {
      console.error('Failed to update fight cards quality profiles:', error);
      toast.error('Update Failed', {
        description: 'Failed to update fight cards quality profiles. Please try again.',
      });
    } finally {
      setUpdatingEventId(null);
    }
  };

  const handleUpdateAllEventsQuality = async (qualityProfileId: number | null) => {
    if (!events) return;

    setIsUpdatingOrganization(true);
    try {
      // Update all events in this organization
      const updatePromises = events.map(event =>
        fetch(`/api/events/${event.id}`, {
          method: 'PUT',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            qualityProfileId,
          }),
        })
      );

      await Promise.all(updatePromises);
      await refetch();
      toast.success('Quality Profiles Updated', {
        description: `All ${events.length} events have been updated with the selected quality profile.`,
      });
    } catch (error) {
      console.error('Failed to update events quality profiles:', error);
      toast.error('Update Failed', {
        description: 'Failed to update events quality profiles. Please try again.',
      });
    } finally {
      setIsUpdatingOrganization(false);
    }
  };

  // Handler functions for actions
  const handleSearchOrganization = async () => {
    if (!name || !events) return;
    try {
      // Queue searches for all monitored events in this organization
      const monitoredEvents = events.filter(e => e.monitored);

      for (const event of monitoredEvents) {
        await fetch(`/api/event/${event.id}/automatic-search`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
        });
      }

      toast.success('Search Queued', {
        description: `Queued ${monitoredEvents.length} searches for ${name}. Check the task queue at the bottom.`,
      });
    } catch (error) {
      console.error('Search failed:', error);
      toast.error('Search Failed', {
        description: 'Failed to start search. Please try again.',
      });
    }
  };

  const handleManualSearchOrganization = () => {
    if (!name) return;
    setManualSearchModal({
      isOpen: true,
      type: 'organization',
      title: name,
      params: { organizationName: name },
    });
  };

  const handlePreviewRenameOrganization = () => {
    if (!name) return;
    setPreviewRenameModal({
      isOpen: true,
      type: 'organization',
      title: name,
      params: { organizationName: name },
    });
  };

  const handleOrganizationHistory = () => {
    if (!name) return;
    setHistoryModal({
      isOpen: true,
      type: 'organization',
      title: name,
      params: { organizationName: name },
    });
  };

  const handleSearchEvent = async (eventId: number, eventTitle: string) => {
    try {
      const response = await fetch(`/api/event/${eventId}/automatic-search`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
      });
      if (response.ok) {
        toast.success('Search Queued', {
          description: `Search queued for ${eventTitle}. Check the task queue at the bottom.`,
        });
      }
    } catch (error) {
      console.error('Search failed:', error);
      toast.error('Search Failed', {
        description: 'Failed to start search. Please try again.',
      });
    }
  };

  const handleManualSearchEvent = (eventId: number, eventTitle: string) => {
    setManualSearchModal({
      isOpen: true,
      type: 'event',
      title: eventTitle,
      params: { eventId },
    });
  };

  const handlePreviewRenameEvent = (eventId: number, eventTitle: string) => {
    setPreviewRenameModal({
      isOpen: true,
      type: 'event',
      title: eventTitle,
      params: { eventId },
    });
  };

  const handleEventHistory = (eventId: number, eventTitle: string) => {
    setHistoryModal({
      isOpen: true,
      type: 'event',
      title: eventTitle,
      params: { eventId },
    });
  };

  const handleSearchFightCard = async (cardId: number, cardType: string) => {
    // Fight card-specific search not yet implemented
    // For now, users can search the entire event
    toast.info('Not Yet Implemented', {
      description: 'Fight card-specific search not yet implemented. Please search the entire event instead.',
    });
  };

  const handleManualSearchFightCard = (cardId: number, cardType: string, eventId: number) => {
    setManualSearchModal({
      isOpen: true,
      type: 'fightcard',
      title: cardType,
      params: { fightCardId: cardId, eventId: eventId },
    });
  };

  const handleToggleOrganizationMonitor = async () => {
    if (!events || events.length === 0) return;

    setIsUpdatingOrganization(true);
    try {
      // Toggle all events in the organization
      const newMonitoredState = stats.monitored < stats.total; // If not all monitored, monitor all. Otherwise unmonitor all.

      const promises = events.map(event =>
        fetch(`/api/events/${event.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            ...event,
            monitored: newMonitoredState,
          }),
        })
      );

      await Promise.all(promises);
      await refetch();
    } catch (error) {
      console.error('Failed to toggle organization monitoring:', error);
      toast.error('Update Failed', {
        description: 'Failed to update organization monitoring. Please try again.',
      });
    } finally {
      setIsUpdatingOrganization(false);
    }
  };

  const stats = events ? {
    total: events.length,
    monitored: events.filter(e => e.monitored).length,
    downloaded: events.filter(e => e.hasFile).length,
    upcoming: events.filter(e => new Date(e.eventDate) > new Date()).length,
  } : { total: 0, monitored: 0, downloaded: 0, upcoming: 0 };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
      </div>
    );
  }

  if (!events || events.length === 0) {
    return (
      <div className="p-8">
        <button
          onClick={() => navigate('/organizations')}
          className="flex items-center gap-2 text-gray-400 hover:text-white mb-6 transition-colors"
        >
          <ArrowLeftIcon className="w-5 h-5" />
          Back to Organizations
        </button>
        <div className="text-center py-12">
          <h1 className="text-3xl font-bold text-white mb-2">{name}</h1>
          <p className="text-gray-400">No events found for this organization</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-7xl mx-auto">
      {/* Back Button */}
      <button
        onClick={() => navigate('/organizations')}
        className="flex items-center gap-2 text-gray-400 hover:text-white mb-6 transition-colors"
      >
        <ArrowLeftIcon className="w-5 h-5" />
        Back to Organizations
      </button>

      {/* Header */}
      <div className="mb-8">
        <div className="flex items-start justify-between mb-4">
          <div>
            <h1 className="text-4xl font-bold text-white mb-2">{name}</h1>
            <div className="flex items-center gap-6 text-sm text-gray-400">
              <div className="flex items-center gap-2">
                <span className="font-semibold text-white">{stats.total}</span>
                <span>Events</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="font-semibold text-white">{stats.monitored}</span>
                <span>Monitored</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="font-semibold text-white">{stats.downloaded}</span>
                <span>Downloaded</span>
              </div>
              {stats.upcoming > 0 && (
                <div className="flex items-center gap-2">
                  <span className="font-semibold text-white">{stats.upcoming}</span>
                  <span>Upcoming</span>
                </div>
              )}
            </div>
          </div>

          {/* Organization Actions */}
          <div className="flex items-center gap-3">
            <div className="flex items-center gap-1">
              <button
                onClick={handleSearchOrganization}
                className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                title="Search all monitored events"
              >
                <MagnifyingGlassIcon className="w-5 h-5 text-gray-400 group-hover:text-white" />
              </button>
              <button
                onClick={handleManualSearchOrganization}
                className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                title="Interactive search"
              >
                <UserIcon className="w-5 h-5 text-gray-400 group-hover:text-white" />
              </button>
              <button
                onClick={handlePreviewRenameOrganization}
                className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                title="Preview rename"
              >
                <ChartBarIcon className="w-5 h-5 text-gray-400 group-hover:text-white" />
              </button>
              <button
                onClick={handleOrganizationHistory}
                className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                title="View history"
              >
                <ClockIcon className="w-5 h-5 text-gray-400 group-hover:text-white" />
              </button>
            </div>

            {/* Organization Quality Profile Selector */}
            <div className="flex items-center gap-2 border-l border-gray-700 pl-3">
              <span className="text-gray-400 text-sm">Set All Events Quality:</span>
              <select
                onChange={(e) => handleUpdateAllEventsQuality(
                  e.target.value ? Number(e.target.value) : null
                )}
                disabled={isUpdatingOrganization}
                className="bg-gray-700 border border-red-900/20 text-white text-sm rounded px-2 py-1 focus:ring-2 focus:ring-red-600 focus:border-transparent disabled:opacity-50"
                value=""
              >
                <option value="">-- Select to Apply All --</option>
                <option value="">No Quality Profile</option>
                {qualityProfiles?.map((profile: any) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.name}{profile.isDefault ? ' (Default)' : ''}
                  </option>
                ))}
              </select>
            </div>

            {/* Organization Monitor Toggle */}
            <div className="flex items-center gap-3 border-l border-gray-700 pl-3">
              <span className="text-gray-400 text-sm">Monitor Organization</span>
              <button
                onClick={handleToggleOrganizationMonitor}
                disabled={isUpdatingOrganization}
                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                  stats.monitored === stats.total ? 'bg-red-600' : 'bg-gray-600'
                } ${isUpdatingOrganization ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
                title={stats.monitored === stats.total ? 'Unmonitor all events' : 'Monitor all events'}
              >
                <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                  stats.monitored === stats.total ? 'translate-x-6' : 'translate-x-1'
                }`} />
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Total Events</p>
          <p className="text-3xl font-bold text-white">{stats.total}</p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Monitored</p>
          <p className="text-3xl font-bold text-white">{stats.monitored}</p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Downloaded</p>
          <p className="text-3xl font-bold text-white">{stats.downloaded}</p>
        </div>
        <div className="bg-gray-900 border border-red-900/30 rounded-lg p-4">
          <p className="text-gray-400 text-sm mb-1">Upcoming</p>
          <p className="text-3xl font-bold text-white">{stats.upcoming}</p>
        </div>
      </div>

      {/* Events List - Sonarr Style */}
      <div className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden">
        <h2 className="text-xl font-bold text-white px-4 py-3 border-b border-red-900/30 bg-gray-950/50">Events</h2>
        {events.map((event) => (
          <div
            key={event.id}
            className={`border-b border-red-900/20 last:border-b-0 ${!event.inLibrary ? 'bg-gray-950/30' : ''}`}
          >
            {/* Compact Event Row */}
            <div
              className="flex items-center justify-between px-4 py-2 cursor-pointer hover:bg-gray-800/50 transition-colors"
              onClick={() => event.inLibrary && toggleEvent(event.id)}
            >
              <div className="flex items-center gap-3 flex-1 min-w-0">
                {/* Expand Icon */}
                <div className="flex-shrink-0 w-6">
                  {event.inLibrary && event.fightCards && event.fightCards.length > 0 && (
                    <>
                      {expandedEvents.has(event.id) ? (
                        <ChevronUpIcon className="w-5 h-5 text-gray-400" />
                      ) : (
                        <ChevronDownIcon className="w-5 h-5 text-gray-400" />
                      )}
                    </>
                  )}
                </div>

                {/* Event Title and Date */}
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <h3 className="text-white font-medium text-sm truncate">{event.title}</h3>
                    {!event.inLibrary && (
                      <span className="px-2 py-0.5 bg-gray-700 text-gray-300 text-xs rounded">Not in Library</span>
                    )}
                    {event.hasFile && (
                      <CheckCircleIcon className="w-4 h-4 text-green-400 flex-shrink-0" />
                    )}
                  </div>
                  <div className="text-xs text-gray-400 mt-0.5">
                    {new Date(event.eventDate).toLocaleDateString('en-US', {
                      month: 'short',
                      day: 'numeric',
                      year: 'numeric'
                    })}
                    {event.location && <span className="ml-2">• {event.location}</span>}
                    {event.fightCards && event.fightCards.length > 0 && <span className="ml-2">• {event.fightCards.length} cards</span>}
                  </div>
                </div>

                {/* Actions and Status */}
                <div className="flex items-center gap-2">
                  {event.inLibrary ? (
                    <>
                      {/* Quality Profile (compact) */}
                      <select
                        value={event.qualityProfileId ?? ''}
                        onClick={(e) => e.stopPropagation()}
                        onChange={(e) => {
                          e.stopPropagation();
                          handleUpdateEventQualityProfile(
                            event.id,
                            e.target.value ? Number(e.target.value) : null
                          );
                        }}
                        disabled={updatingEventId === event.id}
                        className="bg-gray-700 border border-red-900/20 text-white text-xs rounded px-2 py-1 focus:ring-1 focus:ring-red-600 disabled:opacity-50"
                      >
                        <option value="">Select Quality</option>
                        {qualityProfiles?.map((profile: any) => (
                          <option key={profile.id} value={profile.id}>
                            {profile.name}
                          </option>
                        ))}
                      </select>

                      {/* Monitor Toggle (compact) */}
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleToggleEventMonitor(event);
                        }}
                        disabled={updatingEventId === event.id}
                        className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors ${
                          event.monitored ? 'bg-red-600' : 'bg-gray-600'
                        } ${updatingEventId === event.id ? 'opacity-50' : ''}`}
                        title={event.monitored ? 'Monitored' : 'Not Monitored'}
                      >
                        <span className={`inline-block h-3 w-3 transform rounded-full bg-white transition-transform ${
                          event.monitored ? 'translate-x-5' : 'translate-x-1'
                        }`} />
                      </button>

                      {/* Action Buttons */}
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleSearchEvent(event.id, event.title);
                        }}
                        className="p-1.5 hover:bg-gray-700 rounded transition-colors"
                        title="Search"
                      >
                        <MagnifyingGlassIcon className="w-4 h-4 text-gray-400" />
                      </button>
                    </>
                  ) : (
                    <>
                      {/* Add to Library Button */}
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleAddEventToLibrary(event);
                        }}
                        disabled={updatingEventId === event.id}
                        className="px-3 py-1 bg-red-600 hover:bg-red-700 text-white text-xs font-medium rounded transition-colors disabled:opacity-50"
                      >
                        {updatingEventId === event.id ? 'Adding...' : '+ Add'}
                      </button>
                    </>
                  )}
                </div>
              </div>
            </div>

            {/* Expanded Content - Fight Cards */}
            {expandedEvents.has(event.id) && event.fightCards && event.fightCards.length > 0 && (
              <div className="border-t border-red-900/30 p-6 bg-gray-950/50">
                <div className="flex items-center justify-between mb-4">
                  <h4 className="text-white font-semibold text-lg">Fight Cards</h4>
                  {/* Set All Quality Profile */}
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 text-sm">Set All Quality:</span>
                    <select
                      onChange={(e) => handleUpdateAllFightCardsQuality(
                        event.id,
                        e.target.value ? Number(e.target.value) : null,
                        event
                      )}
                      disabled={updatingEventId === event.id}
                      className="bg-gray-700 border border-red-900/20 text-white text-sm rounded px-2 py-1 focus:ring-2 focus:ring-red-600 focus:border-transparent disabled:opacity-50"
                      value=""
                    >
                      <option value="">-- Select to Apply All --</option>
                      <option value="">Inherit from Event</option>
                      {qualityProfiles?.map((profile: any) => (
                        <option key={profile.id} value={profile.id}>
                          {profile.name}{profile.isDefault ? ' (Default)' : ''}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                <div className="space-y-3">
                  {event.fightCards
                    .sort((a, b) => a.cardNumber - b.cardNumber)
                    .map((card) => (
                      <div
                        key={card.id}
                        className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20 hover:border-red-900/40 transition-colors"
                      >
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-4 flex-1">
                            <div className="flex-1">
                              <div className="flex items-center gap-3 mb-1">
                                <span className="text-white font-semibold text-lg">{card.cardType}</span>
                                {card.hasFile && (
                                  <CheckCircleIcon className="w-5 h-5 text-green-400" />
                                )}
                              </div>
                              {card.hasFile && (
                                <div className="flex items-center gap-2 text-sm">
                                  <span className="text-green-400">
                                    Downloaded{card.quality && ` • ${card.quality}`}
                                  </span>
                                </div>
                              )}
                              {card.airDate && (
                                <p className="text-gray-500 text-sm mt-1">
                                  Air Date: {new Date(card.airDate).toLocaleString('en-US', {
                                    month: 'short',
                                    day: 'numeric',
                                    year: 'numeric',
                                    hour: '2-digit',
                                    minute: '2-digit'
                                  })}
                                </p>
                              )}
                            </div>

                            {/* Fight Card Actions & Monitor Toggle */}
                            <div className="flex items-center gap-3">
                              {/* Fight Card Actions */}
                              <div className="flex items-center gap-1">
                                <button
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    handleSearchFightCard(card.id, card.cardType);
                                  }}
                                  className="p-2 hover:bg-gray-700 rounded-lg transition-colors group"
                                  title="Search for this fight card"
                                >
                                  <MagnifyingGlassIcon className="w-4 h-4 text-gray-400 group-hover:text-white" />
                                </button>
                                <button
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    handleManualSearchFightCard(card.id, card.cardType, card.eventId);
                                  }}
                                  className="p-2 hover:bg-gray-700 rounded-lg transition-colors group"
                                  title="Interactive search"
                                >
                                  <UserIcon className="w-4 h-4 text-gray-400 group-hover:text-white" />
                                </button>
                              </div>

                              {/* Quality Profile Selector */}
                              <div className="flex items-center gap-2">
                                <span className="text-gray-400 text-sm">Quality:</span>
                                <select
                                  value={card.qualityProfileId ?? event.qualityProfileId ?? ''}
                                  onChange={(e) => handleUpdateFightCardQualityProfile(
                                    card.id,
                                    e.target.value ? Number(e.target.value) : null
                                  )}
                                  disabled={updatingCardId === card.id}
                                  className="bg-gray-700 border border-red-900/20 text-white text-sm rounded px-2 py-1 focus:ring-2 focus:ring-red-600 focus:border-transparent disabled:opacity-50"
                                >
                                  <option value="">Inherit from Event</option>
                                  {qualityProfiles?.map((profile: any) => (
                                    <option key={profile.id} value={profile.id}>
                                      {profile.name}{profile.isDefault ? ' (Default)' : ''}
                                    </option>
                                  ))}
                                </select>
                              </div>

                              <span className="text-gray-400 text-sm">Monitor</span>
                              <button
                                onClick={() => handleToggleFightCardMonitor(card.id, card.monitored)}
                                disabled={updatingCardId === card.id}
                                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                                  card.monitored ? 'bg-red-600' : 'bg-gray-600'
                                } ${updatingCardId === card.id ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
                              >
                                <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                                  card.monitored ? 'translate-x-6' : 'translate-x-1'
                                }`} />
                              </button>
                            </div>
                          </div>
                        </div>
                      </div>
                    ))}
                </div>
                <p className="text-gray-500 text-sm mt-4">
                  Tip: Monitor individual fight cards to download only specific portions of the event
                </p>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* Modals */}
      <ManualSearchModal
        isOpen={manualSearchModal.isOpen}
        onClose={() => setManualSearchModal({ ...manualSearchModal, isOpen: false })}
        searchType={manualSearchModal.type}
        title={manualSearchModal.title}
        searchParams={manualSearchModal.params}
      />

      <PreviewRenameModal
        isOpen={previewRenameModal.isOpen}
        onClose={() => setPreviewRenameModal({ ...previewRenameModal, isOpen: false })}
        renameType={previewRenameModal.type}
        title={previewRenameModal.title}
        renameParams={previewRenameModal.params}
      />

      <HistoryModal
        isOpen={historyModal.isOpen}
        onClose={() => setHistoryModal({ ...historyModal, isOpen: false })}
        historyType={historyModal.type}
        title={historyModal.title}
        historyParams={historyModal.params}
      />
    </div>
  );
}
