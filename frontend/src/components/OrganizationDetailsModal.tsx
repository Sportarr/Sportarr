import { useState, useEffect, Fragment } from 'react';
import { toast } from 'sonner';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon, CheckCircleIcon, ChevronDownIcon, ChevronUpIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import apiClient from '../api/client';
import type { Event, FightCard } from '../types';

interface OrganizationDetailsModalProps {
  organizationName: string;
  onClose: () => void;
}

export default function OrganizationDetailsModal({ organizationName, onClose }: OrganizationDetailsModalProps) {
  const [expandedEvents, setExpandedEvents] = useState<Set<number>>(new Set());
  const [updatingCardId, setUpdatingCardId] = useState<number | null>(null);

  const { data: events, isLoading, refetch } = useQuery({
    queryKey: ['organization-events', organizationName],
    queryFn: async () => {
      const response = await apiClient.get<Event[]>(`/organizations/${encodeURIComponent(organizationName)}/events`);
      return response.data;
    },
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
    }
  };

  const formatFileSize = (bytes?: number): string => {
    if (!bytes) return 'N/A';
    const gb = bytes / (1024 ** 3);
    return `${gb.toFixed(2)} GB`;
  };

  const stats = events ? {
    total: events.length,
    monitored: events.filter(e => e.monitored).length,
    downloaded: events.filter(e => e.hasFile).length,
    upcoming: events.filter(e => new Date(e.eventDate) > new Date()).length,
  } : { total: 0, monitored: 0, downloaded: 0, upcoming: 0 };

  return (
    <Transition appear show={true} as={Fragment}>
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
          <div className="fixed inset-0 bg-black/70 backdrop-blur-sm" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-6xl transform overflow-hidden rounded-2xl bg-gray-950 border border-red-900/30 text-left align-middle shadow-xl transition-all">
                {/* Header */}
                <div className="flex items-center justify-between p-6 border-b border-red-900/30 bg-gradient-to-r from-gray-900 to-gray-950">
                  <div>
                    <Dialog.Title as="h2" className="text-3xl font-bold text-white">
                      {organizationName}
                    </Dialog.Title>
                    <div className="flex items-center gap-4 mt-2 text-sm text-gray-400">
                      <span>{stats.total} Events</span>
                      <span>•</span>
                      <span>{stats.monitored} Monitored</span>
                      <span>•</span>
                      <span>{stats.downloaded} Downloaded</span>
                      {stats.upcoming > 0 && (
                        <>
                          <span>•</span>
                          <span>{stats.upcoming} Upcoming</span>
                        </>
                      )}
                    </div>
                  </div>
                  <button
                    onClick={onClose}
                    className="p-2 hover:bg-gray-800 rounded-lg transition-colors"
                  >
                    <XMarkIcon className="w-6 h-6 text-gray-400 hover:text-white" />
                  </button>
                </div>

                {/* Content */}
                <div className="p-6 max-h-[70vh] overflow-y-auto">
                  {isLoading ? (
                    <div className="flex items-center justify-center py-12">
                      <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
                    </div>
                  ) : !events || events.length === 0 ? (
                    <div className="text-center py-12">
                      <p className="text-gray-400">No events found for this organization</p>
                    </div>
                  ) : (
                    <div className="space-y-3">
                      {events.map((event) => (
                        <div
                          key={event.id}
                          className="bg-gray-900/50 border border-red-900/20 rounded-lg overflow-hidden hover:border-red-900/40 transition-colors"
                        >
                          {/* Event Header */}
                          <div
                            className="flex items-center justify-between p-4 cursor-pointer"
                            onClick={() => toggleEvent(event.id)}
                          >
                            <div className="flex-1">
                              <div className="flex items-center gap-3">
                                <h3 className="text-white font-semibold text-lg">{event.title}</h3>
                                {event.hasFile && (
                                  <CheckCircleIcon className="w-5 h-5 text-green-400" />
                                )}
                              </div>
                              <div className="flex items-center gap-4 mt-1 text-sm">
                                <span className="text-gray-400">
                                  {new Date(event.eventDate).toLocaleDateString('en-US', {
                                    month: 'long',
                                    day: 'numeric',
                                    year: 'numeric'
                                  })}
                                </span>
                                {event.location && (
                                  <>
                                    <span className="text-gray-600">•</span>
                                    <span className="text-gray-400">{event.location}</span>
                                  </>
                                )}
                                {event.fightCards && event.fightCards.length > 0 && (
                                  <>
                                    <span className="text-gray-600">•</span>
                                    <span className="text-gray-400">
                                      {event.fightCards.length} Fight {event.fightCards.length === 1 ? 'Card' : 'Cards'}
                                    </span>
                                  </>
                                )}
                              </div>
                            </div>

                            <div className="flex items-center gap-3">
                              {/* Monitor Toggle */}
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  handleToggleEventMonitor(event);
                                }}
                                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                                  event.monitored ? 'bg-red-600' : 'bg-gray-600'
                                }`}
                              >
                                <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                                  event.monitored ? 'translate-x-6' : 'translate-x-1'
                                }`} />
                              </button>

                              {/* Expand Icon */}
                              {expandedEvents.has(event.id) ? (
                                <ChevronUpIcon className="w-5 h-5 text-gray-400" />
                              ) : (
                                <ChevronDownIcon className="w-5 h-5 text-gray-400" />
                              )}
                            </div>
                          </div>

                          {/* Expanded Content - Fight Cards */}
                          {expandedEvents.has(event.id) && event.fightCards && event.fightCards.length > 0 && (
                            <div className="border-t border-red-900/20 p-4 bg-gray-950/50">
                              <h4 className="text-white font-semibold mb-3">Fight Cards</h4>
                              <div className="space-y-2">
                                {event.fightCards
                                  .sort((a, b) => a.cardNumber - b.cardNumber)
                                  .map((card) => (
                                    <div
                                      key={card.id}
                                      className="bg-gray-800/50 rounded-lg p-3 border border-red-900/10 hover:border-red-900/30 transition-colors"
                                    >
                                      <div className="flex items-center justify-between">
                                        <div className="flex items-center gap-3 flex-1">
                                          <span className="text-white font-medium">{card.cardType}</span>
                                          {card.hasFile && (
                                            <div className="flex items-center gap-2">
                                              <CheckCircleIcon className="w-4 h-4 text-green-400" />
                                              <span className="text-green-400 text-sm">
                                                Downloaded{card.quality && ` • ${card.quality}`}
                                              </span>
                                            </div>
                                          )}
                                        </div>

                                        {/* Fight Card Monitor Toggle */}
                                        <div className="flex items-center gap-2">
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
                                  ))}
                              </div>
                            </div>
                          )}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
