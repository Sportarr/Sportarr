import { Fragment, useState, useEffect } from 'react';
import { toast } from 'sonner';
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
  VideoCameraIcon,
  SignalIcon,
  PlayIcon,
  StopIcon,
  ExclamationTriangleIcon,
} from '@heroicons/react/24/outline';
import type { Event, Image, PartStatus } from '../types';
import { apiPost } from '../utils/api';

// Helper function to get image URL from either Image object or string
const getImageUrl = (images: Image[] | string[] | undefined): string | undefined => {
  if (!images || images.length === 0) return undefined;
  const first = images[0];
  return typeof first === 'string' ? first : first.remoteUrl;
};

interface EventDetailsModalProps {
  isOpen: boolean;
  onClose: () => void;
  event: Event;
}

interface QualityProfile {
  id: number;
  name: string;
}

interface MatchedFormat {
  name: string;
  score: number;
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
  approved: boolean;
  rejections: string[];
  matchedFormats: MatchedFormat[];
  qualityScore: number;
  customFormatScore: number;
}

// DVR Status interface
interface DvrStatus {
  hasChannelMapping: boolean;
  mappedChannelName?: string;
  canScheduleRecording: boolean;
  recordings: Array<{
    id: number;
    status: string;
    channelName: string;
    scheduledStart: string;
    scheduledEnd: string;
    actualStart?: string;
    actualEnd?: string;
    outputPath?: string;
    fileSize?: number;
    errorMessage?: string;
  }>;
}

export default function EventDetailsModal({ isOpen, onClose, event }: EventDetailsModalProps) {
  const [isSearching, setIsSearching] = useState(false);
  const [searchResults, setSearchResults] = useState<ReleaseSearchResult[]>([]);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [downloadingIndex, setDownloadingIndex] = useState<number | null>(null);
  const [isMonitored, setIsMonitored] = useState(event.monitored);
  const [isUpdatingMonitor, setIsUpdatingMonitor] = useState(false);
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [selectedProfileId, setSelectedProfileId] = useState<number | undefined>(event.qualityProfileId);
  const [isUpdatingProfile, setIsUpdatingProfile] = useState(false);
  const [partStatuses, setPartStatuses] = useState<PartStatus[]>(event.partStatuses || []);
  const [updatingPartName, setUpdatingPartName] = useState<string | null>(null);

  // DVR state
  const [dvrStatus, setDvrStatus] = useState<DvrStatus | null>(null);
  const [isDvrLoading, setIsDvrLoading] = useState(false);
  const [isDvrActionLoading, setIsDvrActionLoading] = useState(false);

  // Fetch quality profiles on mount
  useEffect(() => {
    const fetchProfiles = async () => {
      try {
        const response = await fetch('/api/qualityprofile');
        if (response.ok) {
          const profiles = await response.json();
          setQualityProfiles(profiles);
        }
      } catch (error) {
        console.error('Failed to fetch quality profiles:', error);
      }
    };
    fetchProfiles();
  }, []);

  // Fetch DVR status
  const fetchDvrStatus = async () => {
    setIsDvrLoading(true);
    try {
      const response = await fetch(`/api/events/${event.id}/dvr`);
      if (response.ok) {
        const status = await response.json();
        setDvrStatus(status);
      }
    } catch (error) {
      console.error('Failed to fetch DVR status:', error);
    } finally {
      setIsDvrLoading(false);
    }
  };

  useEffect(() => {
    if (isOpen) {
      fetchDvrStatus();
    }
  }, [isOpen, event.id]);

  // DVR actions
  const handleScheduleDvr = async () => {
    setIsDvrActionLoading(true);
    try {
      const response = await apiPost(`/api/events/${event.id}/dvr/schedule`, {});
      if (response.ok) {
        toast.success('DVR Recording Scheduled', {
          description: 'Recording will start automatically at the scheduled time',
        });
        await fetchDvrStatus();
      } else {
        const error = await response.json();
        toast.error('Failed to schedule DVR', {
          description: error.error || 'Unknown error',
        });
      }
    } catch (error) {
      console.error('Failed to schedule DVR:', error);
      toast.error('Failed to schedule DVR');
    } finally {
      setIsDvrActionLoading(false);
    }
  };

  const handleCancelDvr = async () => {
    setIsDvrActionLoading(true);
    try {
      const response = await apiPost(`/api/events/${event.id}/dvr/cancel`, {});
      if (response.ok) {
        toast.success('DVR Recordings Cancelled');
        await fetchDvrStatus();
      } else {
        toast.error('Failed to cancel DVR');
      }
    } catch (error) {
      console.error('Failed to cancel DVR:', error);
      toast.error('Failed to cancel DVR');
    } finally {
      setIsDvrActionLoading(false);
    }
  };

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

  const handleDownload = async (release: ReleaseSearchResult, index: number) => {
    setDownloadingIndex(index);
    setSearchError(null);

    try {
      const response = await apiPost('/api/release/grab', {
        ...release,
        eventId: event.id
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || 'Download failed');
      }

      const result = await response.json();
      console.log('Download started:', result);
      // Status shown in sidebar FooterStatusBar and EventStatusBadge - no need for toast here
    } catch (error) {
      console.error('Download failed:', error);
      const errorMessage = error instanceof Error ? error.message : 'Failed to start download. Please try again.';
      setSearchError(errorMessage);
    } finally {
      setDownloadingIndex(null);
    }
  };

  const handleToggleMonitor = async () => {
    setIsUpdatingMonitor(true);
    try {
      const response = await fetch(`/api/events/${event.id}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          ...event,
          monitored: !isMonitored,
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to update monitor status');
      }

      const updatedEvent = await response.json();
      setIsMonitored(updatedEvent.monitored);
    } catch (error) {
      console.error('Failed to toggle monitor:', error);
      toast.error('Update Failed', {
        description: 'Failed to update monitor status. Please try again.',
      });
    } finally {
      setIsUpdatingMonitor(false);
    }
  };

  const handleTogglePartMonitor = async (partName: string, currentStatus: boolean) => {
    setUpdatingPartName(partName);
    try {
      // Build the new monitoredParts string
      // Current monitored parts
      const currentMonitored = event.monitoredParts
        ? event.monitoredParts.split(',').map(p => p.trim()).filter(p => p)
        : partStatuses.map(p => p.partName); // Default all monitored if null

      let newMonitored: string[];
      if (currentStatus) {
        // Unmonitor this part
        newMonitored = currentMonitored.filter(p => p !== partName);
      } else {
        // Monitor this part
        newMonitored = [...currentMonitored, partName];
      }

      // Update the event with new monitored parts
      const response = await fetch(`/api/events/${event.id}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          ...event,
          monitoredParts: newMonitored.join(','),
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to update part monitor status');
      }

      // Update local state
      setPartStatuses(prevParts =>
        prevParts.map(part =>
          part.partName === partName ? { ...part, monitored: !currentStatus } : part
        )
      );
    } catch (error) {
      console.error('Failed to toggle part monitor:', error);
      toast.error('Update Failed', {
        description: 'Failed to update part monitor status. Please try again.',
      });
    } finally {
      setUpdatingPartName(null);
    }
  };

  const handleProfileChange = async (profileId: number) => {
    setIsUpdatingProfile(true);
    try {
      const response = await fetch(`/api/events/${event.id}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          ...event,
          qualityProfileId: profileId,
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to update quality profile');
      }

      const updatedEvent = await response.json();
      setSelectedProfileId(updatedEvent.qualityProfileId);
    } catch (error) {
      console.error('Failed to update quality profile:', error);
      toast.error('Update Failed', {
        description: 'Failed to update quality profile. Please try again.',
      });
    } finally {
      setIsUpdatingProfile(false);
    }
  };

  const tabs = [
    { name: 'Details', icon: DocumentTextIcon },
    { name: 'Files', icon: FolderIcon },
    { name: 'DVR', icon: VideoCameraIcon },
    { name: 'Search', icon: MagnifyingGlassIcon },
    { name: 'History', icon: HistoryIcon },
  ];

  return (
    <Transition
      appear
      show={isOpen}
      as={Fragment}
      unmount={true}
      afterLeave={() => {
        // Force cleanup: remove any lingering inert attributes that might block navigation
        document.querySelectorAll('[inert]').forEach((el) => {
          el.removeAttribute('inert');
        });
      }}
    >
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
                  {getImageUrl(event.images) && (
                    <div className="absolute inset-0 opacity-20">
                      <img
                        src={getImageUrl(event.images)}
                        alt={event.title}
                        className="w-full h-full object-cover blur-sm"
                      />
                    </div>
                  )}

                  <div className="absolute inset-0 flex items-end p-6">
                    <div className="flex items-start gap-4 w-full">
                      {/* Poster Thumbnail */}
                      {getImageUrl(event.images) && (
                        <div className="w-32 h-40 bg-gray-950 rounded-lg overflow-hidden flex-shrink-0 border-2 border-red-900/40 shadow-xl">
                          <img
                            src={getImageUrl(event.images)}
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
                        {isMonitored && (
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
                              <div className="flex items-center justify-between mb-2">
                                <p className="text-gray-400 text-sm">Monitor Status</p>
                                <button
                                  onClick={handleToggleMonitor}
                                  disabled={isUpdatingMonitor}
                                  className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                                    isMonitored ? 'bg-red-600' : 'bg-gray-600'
                                  } ${isUpdatingMonitor ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
                                >
                                  <span
                                    className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                                      isMonitored ? 'translate-x-6' : 'translate-x-1'
                                    }`}
                                  />
                                </button>
                              </div>
                              <p className="text-white font-medium">
                                {event.hasFile ? 'Downloaded' : isMonitored ? 'Monitored' : 'Unmonitored'}
                              </p>
                            </div>
                            <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                              <p className="text-gray-400 text-sm mb-2">Quality Profile</p>
                              <select
                                value={selectedProfileId || ''}
                                onChange={(e) => handleProfileChange(Number(e.target.value))}
                                disabled={isUpdatingProfile || qualityProfiles.length === 0}
                                className="w-full bg-gray-700 text-white rounded px-3 py-1.5 text-sm border border-gray-600 focus:border-red-500 focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed"
                              >
                                <option value="">None</option>
                                {qualityProfiles.map((profile) => (
                                  <option key={profile.id} value={profile.id}>
                                    {profile.name}
                                  </option>
                                ))}
                              </select>
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

                        {/* Event Parts (Similar to Sonarr Episodes) */}
                        {partStatuses.length > 0 && (
                          <div>
                            <h3 className="text-lg font-semibold text-white mb-3">Event Parts</h3>
                            <p className="text-gray-400 text-sm mb-4">
                              Monitor individual portions of the event (similar to episodes in Sonarr)
                            </p>
                            <div className="space-y-2">
                              {partStatuses
                                .sort((a, b) => a.partNumber - b.partNumber)
                                .map((part) => (
                                  <div
                                    key={part.partName}
                                    className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20 hover:border-red-900/40 transition-colors"
                                  >
                                    <div className="flex items-center justify-between">
                                      <div className="flex items-center gap-4 flex-1">
                                        {/* Part Name */}
                                        <div className="flex-1">
                                          <p className="text-white font-medium">{part.partName}</p>
                                          {part.downloaded && (
                                            <div className="flex items-center gap-2 mt-1">
                                              <CheckCircleIcon className="w-4 h-4 text-green-400" />
                                              <span className="text-green-400 text-sm">
                                                Downloaded{part.file?.quality && ` • ${part.file.quality}`}
                                              </span>
                                            </div>
                                          )}
                                        </div>

                                        {/* Monitor Toggle */}
                                        <div className="flex items-center gap-2">
                                          <span className="text-gray-400 text-sm">Monitor</span>
                                          <button
                                            onClick={() => handleTogglePartMonitor(part.partName, part.monitored)}
                                            disabled={updatingPartName === part.partName}
                                            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                                              part.monitored ? 'bg-red-600' : 'bg-gray-600'
                                            } ${updatingPartName === part.partName ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
                                          >
                                            <span
                                              className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                                                part.monitored ? 'translate-x-6' : 'translate-x-1'
                                              }`}
                                            />
                                          </button>
                                        </div>
                                      </div>
                                    </div>
                                  </div>
                                ))}
                            </div>
                            <p className="text-gray-500 text-xs mt-3">
                              Tip: Unmonitor parts you don't want to download (e.g., only download Main Card)
                            </p>
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
                              {isMonitored ? 'Sportarr will search for this event when available' : 'Enable monitoring to search for this event'}
                            </p>
                          </div>
                        )}
                      </div>
                    </Tab.Panel>

                    {/* DVR Tab */}
                    <Tab.Panel>
                      <div className="space-y-4">
                        <h3 className="text-lg font-semibold text-white mb-3">DVR Recording</h3>

                        {isDvrLoading ? (
                          <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                            <p className="text-gray-400">Loading DVR status...</p>
                          </div>
                        ) : dvrStatus ? (
                          <div className="space-y-4">
                            {/* Channel Mapping Status */}
                            <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                              <div className="flex items-center gap-3 mb-2">
                                <SignalIcon className={`w-6 h-6 ${dvrStatus.hasChannelMapping ? 'text-green-400' : 'text-gray-500'}`} />
                                <div>
                                  <p className="text-white font-medium">
                                    {dvrStatus.hasChannelMapping ? 'Channel Mapped' : 'No Channel Mapped'}
                                  </p>
                                  {dvrStatus.mappedChannelName && (
                                    <p className="text-gray-400 text-sm">{dvrStatus.mappedChannelName}</p>
                                  )}
                                </div>
                              </div>
                              {!dvrStatus.hasChannelMapping && (
                                <p className="text-gray-500 text-sm mt-2">
                                  Map an IPTV channel to this event's league in Settings → IPTV Channels to enable DVR recording.
                                </p>
                              )}
                            </div>

                            {/* Recording Actions */}
                            {dvrStatus.hasChannelMapping && (
                              <div className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20">
                                <div className="flex items-center justify-between">
                                  <div>
                                    <p className="text-white font-medium">Schedule Recording</p>
                                    <p className="text-gray-400 text-sm">
                                      {dvrStatus.canScheduleRecording
                                        ? 'Record this event from IPTV'
                                        : new Date(event.eventDate) <= new Date()
                                          ? 'Event is in the past'
                                          : !isMonitored
                                            ? 'Enable monitoring first'
                                            : 'Recording already scheduled'}
                                    </p>
                                  </div>
                                  {dvrStatus.recordings.length === 0 && dvrStatus.canScheduleRecording && (
                                    <button
                                      onClick={handleScheduleDvr}
                                      disabled={isDvrActionLoading}
                                      className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded-lg transition-colors flex items-center gap-2"
                                    >
                                      {isDvrActionLoading ? (
                                        <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                                      ) : (
                                        <VideoCameraIcon className="w-5 h-5" />
                                      )}
                                      <span>Schedule DVR</span>
                                    </button>
                                  )}
                                </div>
                              </div>
                            )}

                            {/* Existing Recordings */}
                            {dvrStatus.recordings.length > 0 && (
                              <div>
                                <h4 className="text-white font-medium mb-3">Recordings</h4>
                                <div className="space-y-3">
                                  {dvrStatus.recordings.map((recording) => (
                                    <div
                                      key={recording.id}
                                      className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20"
                                    >
                                      <div className="flex items-center justify-between mb-2">
                                        <div className="flex items-center gap-2">
                                          {recording.status === 'Recording' && (
                                            <VideoCameraIcon className="w-5 h-5 text-red-400 animate-pulse" />
                                          )}
                                          {recording.status === 'Scheduled' && (
                                            <ClockIcon className="w-5 h-5 text-blue-400" />
                                          )}
                                          {recording.status === 'Completed' && (
                                            <CheckCircleIcon className="w-5 h-5 text-green-400" />
                                          )}
                                          {recording.status === 'Imported' && (
                                            <CheckCircleIcon className="w-5 h-5 text-green-400" />
                                          )}
                                          {recording.status === 'Failed' && (
                                            <ExclamationTriangleIcon className="w-5 h-5 text-red-400" />
                                          )}
                                          <span className={`px-2 py-0.5 text-xs rounded ${
                                            recording.status === 'Recording' ? 'bg-red-900/30 text-red-400' :
                                            recording.status === 'Scheduled' ? 'bg-blue-900/30 text-blue-400' :
                                            recording.status === 'Completed' || recording.status === 'Imported' ? 'bg-green-900/30 text-green-400' :
                                            'bg-red-900/30 text-red-400'
                                          }`}>
                                            {recording.status}
                                          </span>
                                        </div>
                                        {recording.status === 'Scheduled' && (
                                          <button
                                            onClick={handleCancelDvr}
                                            disabled={isDvrActionLoading}
                                            className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors"
                                          >
                                            Cancel
                                          </button>
                                        )}
                                      </div>
                                      <div className="grid grid-cols-2 gap-2 text-sm">
                                        <div>
                                          <span className="text-gray-500">Channel:</span>{' '}
                                          <span className="text-white">{recording.channelName}</span>
                                        </div>
                                        <div>
                                          <span className="text-gray-500">Start:</span>{' '}
                                          <span className="text-white">
                                            {new Date(recording.scheduledStart).toLocaleString()}
                                          </span>
                                        </div>
                                        {recording.fileSize && (
                                          <div>
                                            <span className="text-gray-500">Size:</span>{' '}
                                            <span className="text-white">{formatFileSize(recording.fileSize)}</span>
                                          </div>
                                        )}
                                        {recording.errorMessage && (
                                          <div className="col-span-2 text-red-400">
                                            Error: {recording.errorMessage}
                                          </div>
                                        )}
                                      </div>
                                    </div>
                                  ))}
                                </div>
                              </div>
                            )}
                          </div>
                        ) : (
                          <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                            <VideoCameraIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                            <p className="text-gray-400 mb-2">DVR status unavailable</p>
                            <button
                              onClick={fetchDvrStatus}
                              className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                            >
                              Retry
                            </button>
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
                              <div key={index} className={`bg-gray-800/50 rounded-lg p-4 border ${!result.approved ? 'border-yellow-600/30 opacity-60' : 'border-red-900/20'} hover:border-red-600/50 transition-colors`}>
                                <div className="flex items-start justify-between gap-4">
                                  <div className="flex-1 min-w-0">
                                    <div className="flex items-center gap-2 mb-1">
                                      <h4 className="text-white font-medium truncate">{result.title}</h4>
                                      {!result.approved && (
                                        <span className="px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded flex-shrink-0">REJECTED</span>
                                      )}
                                    </div>
                                    <div className="flex items-center gap-3 text-sm text-gray-400 mb-2">
                                      <span className="px-2 py-0.5 bg-red-900/30 text-red-400 rounded text-xs">{result.indexer}</span>
                                      {result.quality && <span className="font-semibold text-blue-400">{result.quality}</span>}
                                      <span>{formatFileSize(result.size)}</span>
                                      {result.seeders !== null && (
                                        <span className="text-green-400">↑ {result.seeders} seeds</span>
                                      )}
                                      {result.leechers !== null && (
                                        <span className="text-yellow-400">↓ {result.leechers} peers</span>
                                      )}
                                    </div>
                                    {/* Custom Format Scores */}
                                    {result.matchedFormats && result.matchedFormats.length > 0 && (
                                      <div className="flex flex-wrap gap-2 mb-2">
                                        {result.matchedFormats.map((format, fIdx) => (
                                          <span key={fIdx} className={`px-2 py-0.5 text-xs rounded ${format.score > 0 ? 'bg-green-900/30 text-green-400' : 'bg-red-900/30 text-red-400'}`}>
                                            {format.name} ({format.score > 0 ? '+' : ''}{format.score})
                                          </span>
                                        ))}
                                      </div>
                                    )}
                                    {/* Rejection Reasons */}
                                    {result.rejections && result.rejections.length > 0 && (
                                      <div className="mt-2 space-y-1">
                                        {result.rejections.map((rejection, rIdx) => (
                                          <p key={rIdx} className="text-xs text-yellow-500 flex items-start gap-1">
                                            <span>⚠</span>
                                            <span>{rejection}</span>
                                          </p>
                                        ))}
                                      </div>
                                    )}
                                  </div>
                                  <div className="flex flex-col items-end gap-2">
                                    <div className="text-right text-xs">
                                      <div className="mb-1">
                                        <span className="text-gray-400">Score: </span>
                                        <span className={`font-bold text-lg ${result.score >= 1000 ? 'text-green-400' : result.score >= 600 ? 'text-blue-400' : result.score >= 400 ? 'text-yellow-400' : 'text-gray-400'}`}>
                                          {result.score}
                                        </span>
                                      </div>
                                      {(result.qualityScore > 0 || result.customFormatScore !== 0) && (
                                        <div className="space-y-0.5 text-gray-500">
                                          <div>
                                            <span className="text-gray-400">Quality:</span> <span className="text-white">{result.qualityScore}</span>
                                          </div>
                                          {result.customFormatScore !== 0 && (
                                            <div>
                                              <span className="text-gray-400">Custom:</span> <span className={result.customFormatScore > 0 ? 'text-green-400' : 'text-red-400'}>{result.customFormatScore > 0 ? '+' : ''}{result.customFormatScore}</span>
                                            </div>
                                          )}
                                        </div>
                                      )}
                                    </div>
                                    <button
                                      onClick={() => handleDownload(result, index)}
                                      disabled={downloadingIndex !== null || !result.approved}
                                      className="px-3 py-1 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 disabled:cursor-not-allowed text-white text-sm rounded transition-colors flex items-center gap-1"
                                      title={!result.approved ? 'Release rejected by quality profile' : ''}
                                    >
                                      {downloadingIndex === index ? (
                                        <>
                                          <div className="animate-spin rounded-full h-3 w-3 border-b-2 border-white"></div>
                                          <span>Downloading...</span>
                                        </>
                                      ) : (
                                        <>
                                          <ArrowDownTrayIcon className="w-4 h-4" />
                                          <span>Download</span>
                                        </>
                                      )}
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
