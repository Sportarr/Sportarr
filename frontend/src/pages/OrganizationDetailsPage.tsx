import { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeftIcon, CheckCircleIcon, ChevronDownIcon, ChevronUpIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import type { Event, FightCard } from '../types';

export default function OrganizationDetailsPage() {
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [expandedEvents, setExpandedEvents] = useState<Set<number>>(new Set());
  const [updatingCardId, setUpdatingCardId] = useState<number | null>(null);
  const [updatingEventId, setUpdatingEventId] = useState<number | null>(null);

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
      alert('Failed to update fight card monitor status. Please try again.');
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
      alert('Failed to update event monitor status. Please try again.');
    } finally {
      setUpdatingEventId(null);
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
          onClick={() => navigate('/events')}
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
        onClick={() => navigate('/events')}
        className="flex items-center gap-2 text-gray-400 hover:text-white mb-6 transition-colors"
      >
        <ArrowLeftIcon className="w-5 h-5" />
        Back to Organizations
      </button>

      {/* Header */}
      <div className="mb-8">
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
                {/* Monitor Toggle */}
                <div className="flex items-center gap-3">
                  <span className="text-gray-400 text-sm">Monitor Event</span>
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

                            {/* Fight Card Monitor Toggle */}
                            <div className="flex items-center gap-3">
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
    </div>
  );
}
