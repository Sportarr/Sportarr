import { useState, Fragment } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  CalendarIcon,
  MapPinIcon,
  GlobeAltIcon,
  CheckCircleIcon,
  ChevronDownIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import { useQualityProfiles } from '../api/hooks';
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

interface AddEventModalProps {
  isOpen: boolean;
  onClose: () => void;
  event: {
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
  };
  onSuccess: () => void;
}

export default function AddEventModal({ isOpen, onClose, event, onSuccess }: AddEventModalProps) {
  const [monitored, setMonitored] = useState(true);
  const [qualityProfileId, setQualityProfileId] = useState(1);
  const [searchOnAdd, setSearchOnAdd] = useState(true);
  const [isAdding, setIsAdding] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);
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

  const handleAdd = async () => {
    setIsAdding(true);
    try {
      const response = await apiClient.post('/events', {
        tapologyId: event.tapologyId,
        title: event.title,
        organization: event.organization,
        eventDate: event.eventDate,
        venue: event.venue,
        location: event.location,
        monitored,
        qualityProfileId,
      });

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
                          <div className="flex items-center text-gray-300">
                            <GlobeAltIcon className="w-5 h-5 mr-3 text-red-400" />
                            <span className="font-semibold text-red-400">{event.organization}</span>
                          </div>

                          <div className="flex items-center text-gray-300">
                            <CalendarIcon className="w-5 h-5 mr-3 text-gray-500" />
                            <span>{formatDate(event.eventDate)}</span>
                          </div>

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

                        {/* Quality Profile */}
                        <div>
                          <label htmlFor="qualityProfile" className="block text-white font-medium mb-2">
                            Quality Profile
                          </label>
                          <select
                            id="qualityProfile"
                            value={qualityProfileId}
                            onChange={(e) => setQualityProfileId(Number(e.target.value))}
                            className="w-full px-4 py-2.5 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 focus:ring-2 focus:ring-red-600/20 transition-all"
                          >
                            {qualityProfiles?.map((profile) => (
                              <option key={profile.id} value={profile.id}>
                                {profile.name}
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

                      {/* Fight Card Preview */}
                      {event.fights && event.fights.length > 0 && (
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
                    disabled={isAdding}
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
