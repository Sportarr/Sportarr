import { Fragment, useState } from 'react';
import { Dialog, Transition, Tab } from '@headlessui/react';
import {
  XMarkIcon,
  CheckCircleIcon,
  ClockIcon,
  MagnifyingGlassIcon,
  DocumentTextIcon,
  FolderIcon,
  ClockIcon as HistoryIcon,
  ArrowDownTrayIcon,
} from '@heroicons/react/24/outline';
import type { Event } from '../types';
import { apiPost } from '../utils/api';

interface EventDetailsModalProps {
  isOpen: boolean;
  onClose: () => void;
  event: Event;
}

interface ReleaseSearchResult {
  title: string;
  guid: string;
  downloadUrl: string;
  indexer: string;
  size: number;
  publishDate: string;
  seeders: number | null;
  leechers: number | null;
  quality: string | null;
  score: number;
}

export default function EventDetailsModal({ isOpen, onClose, event }: EventDetailsModalProps) {
  const [isSearching, setIsSearching] = useState(false);
  const [searchResults, setSearchResults] = useState<ReleaseSearchResult[]>([]);
  const [searchError, setSearchError] = useState<string | null>(null);

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  };

  const formatFileSize = (bytes?: number) => {
    if (!bytes) return 'N/A';
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    if (bytes === 0) return '0 Byte';
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return Math.round((bytes / Math.pow(1024, i)) * 100) / 100 + ' ' + sizes[i];
  };

  const handleManualSearch = async () => {
    setIsSearching(true);
    setSearchError(null);
    setSearchResults([]);

    try {
      const response = await apiPost(`/api/event/${event.id}/search`, {});
      const results = await response.json();
      setSearchResults(results || []);
    } catch (error) {
      console.error('Search failed:', error);
      setSearchError('Failed to search indexers. Please try again.');
    } finally {
      setIsSearching(false);
    }
  };

  const tabs = [
    { name: 'Details', icon: DocumentTextIcon },
    { name: 'Files', icon: FolderIcon },
    { name: 'Search', icon: MagnifyingGlassIcon },
    { name: 'History', icon: HistoryIcon },
  ];

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
          <div className="fixed inset-0 bg-black/80" />
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
              <Dialog.Panel className="w-full max-w-4xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header with Event Poster */}
                <div className="relative h-48 bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900">
                  {event.images?.[0] && (
                    <div className="absolute inset-0 opacity-20">
                      <img
                        src={event.images[0].remoteUrl}
                        alt={event.title}
                        className="w-full h-full object-cover blur-sm"
                      />
                    </div>
                  )}

                  <div className="absolute inset-0 flex items-end p-6">
                    <div className="flex items-start gap-4 w-full">
                      {/* Poster Thumbnail */}
                      {event.images?.[0] && (
                        <div className="w-32 h-40 bg-gray-950 rounded-lg overflow-hidden flex-shrink-0 border-2 border-red-900/40 shadow-xl">
                          <img
                            src={event.images[0].remoteUrl}
                            alt={event.title}
                            className="w-full h-full object-cover"
                          />
                        </div>
                      )}

                      {/* Event Info */}
                      <div className="flex-1 min-w-0">
                        <h2 className="text-2xl font-bold text-white mb-2 line-clamp-2">
                          {event.title}
                        </h2>
                        <p className="text-red-400 font-semibold text-lg mb-1">
                          {event.organization}
                        </p>
                        <p className="text-gray-400 text-sm">
                          {formatDate(event.eventDate)}
                        </p>
                        {event.venue && (
                          <p className="text-gray-500 text-sm mt-1">
                            {event.venue} {event.location && `• ${event.location}`}
                          </p>
                        )}
                      </div>

                      {/* Status Badge */}
                      <div className="flex flex-col gap-2">
                        {event.monitored && (
                          <span className="px-3 py-1 bg-red-600/90 text-white text-xs font-semibold rounded-full">
                            MONITORED
                          </span>
                        )}
                        {event.hasFile && (
                          <span className="px-3 py-1 bg-green-600/90 text-white text-xs font-semibold rounded-full flex items-center gap-1">
                            <CheckCircleIcon className="w-4 h-4" />
                            DOWNLOADED
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Close Button */}
                    <button
                      onClick={onClose}
                      className="absolute top-4 right-4 p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors"
                    >
                      <XMarkIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>
                </div>

                {/* Tabs */}
                <Tab.Group>
                  <Tab.List className="flex border-b border-red-900/30 bg-gray-900/50">
                    {tabs.map((tab) => (
                      <Tab
                        key={tab.name}
                        className={({ selected }) =>
                          `flex-1 px-4 py-3 text-sm font-medium focus:outline-none transition-colors ${
                            selected
                              ? 'text-white border-b-2 border-red-600 bg-red-950/20'
                              : 'text-gray-400 hover:text-white hover:bg-gray-800/30'
                          }`
                        }
                      >
                        <div className="flex items-center justify-center gap-2">
                          <tab.icon className="w-5 h-5" />
                          <span>{tab.name}</span>
                        </div>
                      </Tab>
                    ))}
                  </Tab.List>

                  <Tab.Panels className="p-6 max-h-[60vh] overflow-y-auto">
                    {/* Details Tab */}
                    <Tab.Panel>
                      <div className="space-y-6">
                        {/* Overview */}
                        <div>
                          <h3 className="text-lg font-semibold text-white mb-3">Overview</h3>
                          <div className="grid grid-cols-2 gap-4">
                            <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                              <p className="text-gray-400 text-sm mb-1">Status</p>
                              <p className="text-white font-medium">
                                {event.hasFile ? 'Downloaded' : event.monitored ? 'Monitored' : 'Unmonitored'}
                              </p>
                            </div>
                            <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                              <p className="text-gray-400 text-sm mb-1">Quality Profile</p>
                              <p className="text-white font-medium">{event.quality || 'Not Set'}</p>
                            </div>
                            <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                              <p className="text-gray-400 text-sm mb-1">Organization</p>
                              <p className="text-white font-medium">{event.organization}</p>
                            </div>
                            <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                              <p className="text-gray-400 text-sm mb-1">Event Date</p>
                              <p className="text-white font-medium">{formatDate(event.eventDate)}</p>
                            </div>
                          </div>
                        </div>

                        {/* Location Info */}
                        {(event.venue || event.location) && (
                          <div>
                            <h3 className="text-lg font-semibold text-white mb-3">Location</h3>
                            <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                              {event.venue && (
                                <p className="text-white mb-1">{event.venue}</p>
                              )}
                              {event.location && (
                                <p className="text-gray-400 text-sm">{event.location}</p>
                              )}
                            </div>
                          </div>
                        )}
                      </div>
                    </Tab.Panel>

                    {/* Files Tab */}
                    <Tab.Panel>
                      <div className="space-y-4">
                        <h3 className="text-lg font-semibold text-white mb-3">File Information</h3>

                        {event.hasFile ? (
                          <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                            <div className="flex items-start justify-between mb-4">
                              <div className="flex items-center gap-3">
                                <div className="w-12 h-12 bg-green-600/20 rounded-lg flex items-center justify-center">
                                  <CheckCircleIcon className="w-6 h-6 text-green-400" />
                                </div>
                                <div>
                                  <p className="text-white font-medium">File Downloaded</p>
                                  <p className="text-gray-400 text-sm">Quality: {event.quality || 'Unknown'}</p>
                                </div>
                              </div>
                              <span className="text-gray-400 text-sm">{formatFileSize(event.fileSize)}</span>
                            </div>

                            {event.filePath && (
                              <div className="mt-4 pt-4 border-t border-gray-700">
                                <p className="text-gray-400 text-xs mb-1">File Path:</p>
                                <p className="text-white font-mono text-sm break-all">{event.filePath}</p>
                              </div>
                            )}
                          </div>
                        ) : (
                          <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                            <FolderIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                            <p className="text-gray-400 mb-2">No file available</p>
                            <p className="text-gray-500 text-sm">
                              {event.monitored ? 'File will be downloaded automatically when available' : 'Enable monitoring to download this event'}
                            </p>
                          </div>
                        )}
                      </div>
                    </Tab.Panel>

                    {/* Search Tab */}
                    <Tab.Panel>
                      <div className="space-y-4">
                        <div className="flex items-center justify-between mb-4">
                          <h3 className="text-lg font-semibold text-white">Manual Search</h3>
                          <button
                            onClick={handleManualSearch}
                            disabled={isSearching}
                            className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded-lg transition-colors flex items-center gap-2"
                          >
                            {isSearching ? (
                              <>
                                <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                                <span>Searching...</span>
                              </>
                            ) : (
                              <>
                                <MagnifyingGlassIcon className="w-5 h-5" />
                                <span>Search for Releases</span>
                              </>
                            )}
                          </button>
                        </div>

                        {searchError && (
                          <div className="bg-red-900/20 border border-red-600/50 rounded-lg p-4 mb-4">
                            <p className="text-red-400 text-sm">{searchError}</p>
                          </div>
                        )}

                        {isSearching ? (
                          <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                            <p className="text-gray-400">Searching indexers for releases...</p>
                          </div>
                        ) : searchResults.length > 0 ? (
                          <div className="space-y-2">
                            <p className="text-gray-400 text-sm mb-3">Found {searchResults.length} releases</p>
                            {searchResults.map((result, index) => (
                              <div key={index} className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20 hover:border-red-600/50 transition-colors">
                                <div className="flex items-start justify-between gap-4">
                                  <div className="flex-1 min-w-0">
                                    <h4 className="text-white font-medium mb-1 truncate">{result.title}</h4>
                                    <div className="flex items-center gap-3 text-sm text-gray-400">
                                      <span className="px-2 py-0.5 bg-red-900/30 text-red-400 rounded text-xs">{result.indexer}</span>
                                      {result.quality && <span>{result.quality}</span>}
                                      <span>{formatFileSize(result.size)}</span>
                                      {result.seeders !== null && (
                                        <span className="text-green-400">↑ {result.seeders} seeds</span>
                                      )}
                                      {result.leechers !== null && (
                                        <span className="text-yellow-400">↓ {result.leechers} peers</span>
                                      )}
                                    </div>
                                  </div>
                                  <div className="flex items-center gap-2">
                                    <span className="text-xs text-gray-500">Score: {result.score}</span>
                                    <button className="px-3 py-1 bg-red-600 hover:bg-red-700 text-white text-sm rounded transition-colors">
                                      Download
                                    </button>
                                  </div>
                                </div>
                              </div>
                            ))}
                          </div>
                        ) : (
                          <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                            <MagnifyingGlassIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                            <p className="text-gray-400 mb-2">No releases found</p>
                            <p className="text-gray-500 text-sm">
                              Click "Search for Releases" to manually search indexers for this event
                            </p>
                          </div>
                        )}

                        <div className="mt-6">
                          <h4 className="text-md font-semibold text-white mb-3">Search Parameters</h4>
                          <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20 space-y-2">
                            <div className="flex justify-between">
                              <span className="text-gray-400 text-sm">Title:</span>
                              <span className="text-white text-sm">{event.title}</span>
                            </div>
                            <div className="flex justify-between">
                              <span className="text-gray-400 text-sm">Organization:</span>
                              <span className="text-white text-sm">{event.organization}</span>
                            </div>
                            <div className="flex justify-between">
                              <span className="text-gray-400 text-sm">Date:</span>
                              <span className="text-white text-sm">{new Date(event.eventDate).toLocaleDateString()}</span>
                            </div>
                          </div>
                        </div>
                      </div>
                    </Tab.Panel>

                    {/* History Tab */}
                    <Tab.Panel>
                      <div className="space-y-4">
                        <h3 className="text-lg font-semibold text-white mb-3">Download History</h3>

                        <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                          <HistoryIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                          <p className="text-gray-400 mb-2">No history available</p>
                          <p className="text-gray-500 text-sm">
                            Download and activity history will appear here
                          </p>
                        </div>
                      </div>
                    </Tab.Panel>
                  </Tab.Panels>
                </Tab.Group>

                {/* Footer Actions */}
                <div className="px-6 py-4 bg-gray-900/50 border-t border-red-900/30 flex justify-between items-center">
                  <div className="flex gap-2">
                    {!event.hasFile && (
                      <button className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors flex items-center gap-2">
                        <ArrowDownTrayIcon className="w-5 h-5" />
                        <span>Download</span>
                      </button>
                    )}
                  </div>

                  <button
                    onClick={onClose}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Close
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
