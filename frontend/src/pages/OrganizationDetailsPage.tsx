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

  // Handler functions for actions
  const handleSearchOrganization = async () => {
    if (!name) return;
    try {
      const response = await fetch(`/api/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: 'OrganizationSearch',
          organization: name
        }),
      });
      if (response.ok) {
        toast.success('Search Started', {
          description: `Searching all monitored events for ${name}.`,
        });
      }
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
      const response = await fetch(`/api/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: 'EventSearch',
          eventIds: [eventId]
        }),
      });
      if (response.ok) {
        toast.success('Search Started', {
          description: `Searching for ${eventTitle}.`,
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
    try {
      const response = await fetch(`/api/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: 'FightCardSearch',
          fightCardId: cardId
        }),
      });
      if (response.ok) {
        toast.success('Search Started', {
          description: `Searching for ${cardType}.`,
        });
      }
    } catch (error) {
      console.error('Search failed:', error);
      toast.error('Search Failed', {
        description: 'Failed to start search. Please try again.',
      });
    }
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

      {/* Events List */}
      <div className="space-y-4">
        <h2 className="text-2xl font-bold text-white mb-4">Events</h2>
        {events.map((event) => (
          <div
            key={event.id}
            className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden hover:border-red-900/50 transition-colors"
          >
            {/* Event Header */}
            <div
              className="flex items-center justify-between p-6 cursor-pointer"
              onClick={() => toggleEvent(event.id)}
            >
              <div className="flex-1">
                <div className="flex items-center gap-4 mb-2">
                  <h3 className="text-white font-bold text-xl">{event.title}</h3>
                  {event.hasFile && (
                    <span className="px-2 py-1 bg-green-600/20 text-green-400 text-xs font-semibold rounded border border-green-600/30">
                      Downloaded
                    </span>
                  )}
                  {event.monitored && (
                    <span className="px-2 py-1 bg-red-600/20 text-red-400 text-xs font-semibold rounded border border-red-600/30">
                      Monitored
                    </span>
                  )}
                </div>
                <div className="flex items-center gap-6 text-sm text-gray-400">
                  <div className="flex items-center gap-2">
                    <span className="text-gray-500">Date:</span>
                    <span>
                      {new Date(event.eventDate).toLocaleDateString('en-US', {
                        month: 'long',
                        day: 'numeric',
                        year: 'numeric',
                        hour: '2-digit',
                        minute: '2-digit'
                      })}
                    </span>
                  </div>
                  {event.location && (
                    <>
                      <span className="text-gray-700">•</span>
                      <div className="flex items-center gap-2">
                        <span className="text-gray-500">Location:</span>
                        <span>{event.location}</span>
                      </div>
                    </>
                  )}
                  {event.fightCards && event.fightCards.length > 0 && (
                    <>
                      <span className="text-gray-700">•</span>
                      <div className="flex items-center gap-2">
                        <span className="text-gray-500">Fight Cards:</span>
                        <span>{event.fightCards.length}</span>
                      </div>
                    </>
                  )}
                </div>
              </div>

              <div className="flex items-center gap-4">
                {/* Event Actions */}
                <div className="flex items-center gap-1">
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      handleSearchEvent(event.id, event.title);
                    }}
                    className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                    title="Search for this event"
                  >
                    <MagnifyingGlassIcon className="w-4 h-4 text-gray-400 group-hover:text-white" />
                  </button>
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      handleManualSearchEvent(event.id, event.title);
                    }}
                    className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                    title="Interactive search"
                  >
                    <UserIcon className="w-4 h-4 text-gray-400 group-hover:text-white" />
                  </button>
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      handlePreviewRenameEvent(event.id, event.title);
                    }}
                    className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                    title="Preview rename"
                  >
                    <ChartBarIcon className="w-4 h-4 text-gray-400 group-hover:text-white" />
                  </button>
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      handleEventHistory(event.id, event.title);
                    }}
                    className="p-2 hover:bg-gray-800 rounded-lg transition-colors group"
                    title="View history"
                  >
                    <ClockIcon className="w-4 h-4 text-gray-400 group-hover:text-white" />
                  </button>
                </div>

                {/* Monitor Toggle */}
                <div className="flex items-center gap-3">
                  <span className="text-gray-400 text-sm">Monitor</span>
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      handleToggleEventMonitor(event);
                    }}
                    disabled={updatingEventId === event.id}
                    className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                      event.monitored ? 'bg-red-600' : 'bg-gray-600'
                    } ${updatingEventId === event.id ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
                  >
                    <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                      event.monitored ? 'translate-x-6' : 'translate-x-1'
                    }`} />
                  </button>
                </div>

                {/* Expand Icon */}
                {expandedEvents.has(event.id) ? (
                  <ChevronUpIcon className="w-6 h-6 text-gray-400" />
                ) : (
                  <ChevronDownIcon className="w-6 h-6 text-gray-400" />
                )}
              </div>
            </div>

            {/* Expanded Content - Fight Cards */}
            {expandedEvents.has(event.id) && event.fightCards && event.fightCards.length > 0 && (
              <div className="border-t border-red-900/30 p-6 bg-gray-950/50">
                <h4 className="text-white font-semibold text-lg mb-4">Fight Cards</h4>
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
