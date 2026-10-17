import { useState, useEffect, useMemo } from 'react';
import { Menu } from '@headlessui/react';
import {
  PlayIcon,
  StopIcon,
  TrashIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClockIcon,
  FilmIcon,
  XMarkIcon,
  ArrowPathIcon,
  ExclamationTriangleIcon,
  VideoCameraIcon,
  PlusIcon,
  ArrowDownOnSquareIcon,
  Cog6ToothIcon,
  EllipsisVerticalIcon,
  WrenchScrewdriverIcon,
} from '@heroicons/react/24/outline';
import { Link } from 'react-router-dom';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import PageHeader from '../../components/PageHeader';
import PageShell from '../../components/PageShell';
import { useUISettings } from '../../hooks/useUISettings';
import { formatDateInTimezone, formatTimeInTimezone, localInputToUtcIso } from '../../utils/timezone';
import {
  getRecordingAttentionCount,
  getRecordingCompletedCount,
  getVisibleSelectedIds,
  recordingMatchesView,
  type RecordingView,
} from './dvrRecordingPresentation';


// DVR Recording Types
type RecordingStatus = 'Scheduled' | 'Recording' | 'Completed' | 'Failed' | 'Cancelled' | 'Importing' | 'Imported' | 'Ignored';

interface DvrRecording {
  id: number;
  eventId?: number;
  eventTitle: string;
  leagueName?: string;
  channelId: number;
  channelName: string;
  scheduledStart: string;
  scheduledEnd: string;
  actualStart?: string;
  actualEnd?: string;
  status: RecordingStatus;
  outputPath?: string;
  fileSize?: number;
  prePadding: number;
  postPadding: number;
  errorMessage?: string;
  createdAt: string;
  // Actual scores (for completed recordings)
  qualityScore?: number;
  customFormatScore?: number;
  quality?: string;
  resolution?: string;
  videoCodec?: string;
  audioCodec?: string;
  // "Live" records in real time; "Catchup" downloads the already-aired
  // window from the provider's timeshift archive after the event ends.
  method?: 'Live' | 'Catchup';
  // Expected scores (for scheduled recordings)
  expectedQualityScore?: number;
  expectedCustomFormatScore?: number;
  expectedTotalScore?: number;
  expectedQualityName?: string;
  expectedFormatDescription?: string;
  expectedMatchedFormats?: string[];
}

interface DvrStats {
  totalRecordings: number;
  scheduledCount: number;
  recordingCount: number;
  completedCount: number;
  importedCount: number;
  importingCount: number;
  failedCount: number;
  cancelledCount: number;
  ignoredCount: number;
  totalStorageUsed: number;
}

interface ScheduleFormData {
  eventTitle: string;
  channelId: number;
  scheduledStart: string;
  scheduledEnd: string;
  prePadding: number;
  postPadding: number;
  // Optional. A recording linked to an event is imported against it when the
  // recording finishes, instead of waiting in the manual import queue.
  eventId?: number;
  // Empty leaves the finished recording where it was recorded.
  importMode?: 'move' | 'copy' | 'hardlink' | '';
}

interface FailedCapture {
  id: number;
  title: string;
  outputPath: string;
  fileSize: number;
}

interface EventSearchResult {
  id: number;
  title: string;
  sport?: string | null;
  leagueName?: string | null;
  eventDate?: string | null;
}

interface IptvChannel {
  id: number;
  name: string;
  isEnabled: boolean;
  status: string;
}


const defaultFormData: ScheduleFormData = {
  eventTitle: '',
  channelId: 0,
  scheduledStart: '',
  scheduledEnd: '',
  prePadding: 5,
  postPadding: 15,
  eventId: undefined,
  importMode: '',
};

export default function DvrRecordingsSettings() {
  // Get user's configured timezone
  const { timezone } = useUISettings();

  // State
  const [recordings, setRecordings] = useState<DvrRecording[]>([]);
  const [stats, setStats] = useState<DvrStats | null>(null);
  const [channels, setChannels] = useState<IptvChannel[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filter state
  const [recordingView, setRecordingView] = useState<RecordingView>('upcoming');

  // Bulk selection state
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [manageRecordings, setManageRecordings] = useState(false);
  const visibleRecordings = useMemo(
    () => recordings.filter((recording) => recordingMatchesView(recording.status, recordingView)),
    [recordings, recordingView],
  );

  // Modal state
  const [showScheduleModal, setShowScheduleModal] = useState(false);
  const [formData, setFormData] = useState<ScheduleFormData>(defaultFormData);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [showBulkDeleteConfirm, setShowBulkDeleteConfirm] = useState(false);
  const [viewingRecording, setViewingRecording] = useState<DvrRecording | null>(null);

  // Channel search state for modal
  const [channelSearch, setChannelSearch] = useState('');
  const [showChannelDropdown, setShowChannelDropdown] = useState(false);

  // Event search state for the optional event link
  const [eventSearch, setEventSearch] = useState('');
  const [eventResults, setEventResults] = useState<EventSearchResult[]>([]);
  const [showEventDropdown, setShowEventDropdown] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState<EventSearchResult | null>(null);
  const [isSearchingEvents, setIsSearchingEvents] = useState(false);

  // Captures a failed recording left on disk. Hidden while they were being
  // written, so nothing showed that the space was still taken.
  const [failedCaptures, setFailedCaptures] = useState<FailedCapture[]>([]);

  // FFmpeg state
  const [ffmpegAvailable, setFfmpegAvailable] = useState<boolean | null>(null);


  // Load data on mount
  useEffect(() => {
    loadData();
    checkFfmpeg();
  }, []);

  useEffect(() => {
    setSelectedIds(new Set());
  }, [recordingView]);

  // Re-check while the answer is unknown, so a check that could not reach the
  // server settles on its own instead of leaving the page unsure and manual
  // recording unavailable for the rest of the session.
  useEffect(() => {
    if (ffmpegAvailable !== null) return;
    const interval = setInterval(checkFfmpeg, 30000);
    return () => clearInterval(interval);
  }, [ffmpegAvailable]);

  // Load on filter change and refresh every 30 seconds. The interval lives
  // in THIS effect so each registration closes over the current filter;
  // registered on mount it kept polling with the initial 'All' forever and
  // overwrote a filtered list with everything a few seconds after the user
  // picked a status.
  useEffect(() => {
    // Drop the selection when the filter changes. Rows selected under the
    // previous filter stayed selected while invisible, and a bulk delete then
    // removed recordings the user could no longer see.
    setSelectedIds(new Set());
    loadRecordings();
    const interval = setInterval(loadRecordings, 30000);
    return () => clearInterval(interval);
  }, []);

  const loadData = async () => {
    await Promise.all([loadRecordings(), loadStats(), loadChannels(), loadFailedCaptures()]);
  };

  const loadFailedCaptures = async () => {
    try {
      const { data } = await apiClient.get<FailedCapture[]>('/dvr/recordings/failed-captures');
      setFailedCaptures(data);
    } catch {
      setFailedCaptures([]);
    }
  };

  // Removing the recording takes its file with it, which is the point here:
  // the capture is the thing occupying the disk.
  const handleRemoveFailedCaptures = async (ids: number[]) => {
    const reclaimed = failedCaptures.filter(c => ids.includes(c.id)).reduce((sum, c) => sum + c.fileSize, 0);
    try {
      await Promise.all(ids.map(id => apiClient.delete(`/dvr/recordings/${id}?deleteFile=true`)));
      setFailedCaptures(prev => prev.filter(c => !ids.includes(c.id)));
      await Promise.all([loadRecordings(), loadStats()]);
      toast.success(ids.length === 1 ? 'Capture removed' : `${ids.length} captures removed`, { description: `Freed ${formatFileSize(reclaimed)}` });
    } catch (err: any) {
      toast.error('Could not remove the capture', { description: err?.message });
    }
  };

  const loadRecordings = async () => {
    try {
      setIsLoading(true);
      const { data } = await apiClient.get<DvrRecording[]>('/dvr/recordings');
      setRecordings(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load recordings');
    } finally {
      setIsLoading(false);
    }
  };

  const loadStats = async () => {
    try {
      const { data } = await apiClient.get<DvrStats>('/dvr/stats');
      setStats(data);
    } catch (err: any) {
      console.error('Failed to load DVR stats:', err);
    }
  };

  const loadChannels = async () => {
    try {
      const { data } = await apiClient.get<IptvChannel[]>('/iptv/channels', {
        params: { enabledOnly: true },
      });
      setChannels(data);
    } catch (err: any) {
      console.error('Failed to load channels:', err);
    }
  };

  const checkFfmpeg = async () => {
    try {
      // The backend route is /api/dvr/ffmpeg/status. This used to call a
      // nonexistent /dvr/ffmpeg/check, which the SPA fallback answered
      // with HTML, so the UI reported FFmpeg missing on every install
      // regardless of reality.
      const { data } = await apiClient.get<{ available: boolean; version?: string; path?: string }>('/dvr/ffmpeg/status');
      setFfmpegAvailable(data.available);
    } catch (err: any) {
      // A request that never got an answer says nothing about whether FFmpeg
      // is installed. Recording it as absent turned a momentary blip into a
      // page that refused manual recording for the rest of the session and
      // told the user FFmpeg was missing. Unknown stays unknown, and the next
      // check settles it.
      console.error('Could not check FFmpeg availability:', err);
      setFfmpegAvailable(null);
    }
  };

  const handleFormChange = (field: keyof ScheduleFormData, value: any) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const handleScheduleRecording = async () => {
    try {
      setError(null);
      // The datetime-local inputs hold wall-clock digits in the app's configured
      // timezone with no offset info - convert to real UTC instants before
      // sending, otherwise the backend stores the raw digits as if they were
      // already UTC (off by the timezone's offset, rolling to the previous day
      // for negative offsets on early times).
      const payload = {
        ...formData,
        scheduledStart: localInputToUtcIso(formData.scheduledStart, timezone),
        scheduledEnd: localInputToUtcIso(formData.scheduledEnd, timezone),
      };
      const response = await apiClient.post<DvrRecording>('/dvr/recordings', payload);
      setRecordings(prev => [response.data, ...prev]);
      setShowScheduleModal(false);
      setFormData(defaultFormData);
      await loadStats();
      toast.success('Recording Scheduled', { description: `${formData.eventTitle} has been scheduled` });
    } catch (err: any) {
      setError(err.message || 'Failed to schedule recording');
      toast.error('Failed to schedule recording', { description: err.message });
    }
  };

  const handleStartRecording = async (id: number) => {
    try {
      const response = await apiClient.post<{ success: boolean; error?: string }>(`/dvr/recordings/${id}/start`);
      if (response.data.success) {
        await loadRecordings();
        await loadStats();
        toast.success('Recording Started');
      } else {
        toast.error('Failed to start recording', { description: response.data.error });
      }
    } catch (err: any) {
      toast.error('Failed to start recording', { description: err.message });
    }
  };

  const handleStopRecording = async (id: number) => {
    try {
      const response = await apiClient.post<{ success: boolean; error?: string }>(`/dvr/recordings/${id}/stop`);
      if (response.data.success) {
        await loadRecordings();
        await loadStats();
        toast.success('Recording Stopped');
      } else {
        toast.error('Failed to stop recording', { description: response.data.error });
      }
    } catch (err: any) {
      toast.error('Failed to stop recording', { description: err.message });
    }
  };

  const handleCancelRecording = async (id: number) => {
    try {
      await apiClient.post(`/dvr/recordings/${id}/cancel`);
      await loadRecordings();
      await loadStats();
      toast.success('Recording Cancelled');
    } catch (err: any) {
      toast.error('Failed to cancel recording', { description: err.message });
    }
  };

  const handleDeleteRecording = async (id: number) => {
    try {
      await apiClient.delete(`/dvr/recordings/${id}`);
      setRecordings(prev => prev.filter(r => r.id !== id));
      setShowDeleteConfirm(null);
      await loadStats();
      toast.success('Recording Deleted');
    } catch (err: any) {
      toast.error('Failed to delete recording', { description: err.message });
    }
  };

  const handleBulkDelete = async () => {
    // Only what is actually on screen. A selection made before the list
    // changed under it must not take rows the user cannot see with it.
    const ids = getVisibleSelectedIds(selectedIds, visibleRecordings);
    if (ids.length === 0) return;

    try {
      // Delete recordings one by one (could be optimized with a bulk endpoint)
      let successCount = 0;
      let failCount = 0;

      for (const id of ids) {
        try {
          await apiClient.delete(`/dvr/recordings/${id}`);
          successCount++;
        } catch {
          failCount++;
        }
      }

      // Update state
      const deleted = new Set(ids);
      setRecordings(prev => prev.filter(r => !deleted.has(r.id)));
      setSelectedIds(new Set());
      await loadStats();

      if (failCount > 0) {
        toast.success(`Deleted ${successCount} recordings`, {
          description: `${failCount} failed to delete`,
        });
      } else {
        toast.success(`Deleted ${successCount} recordings`);
      }
    } catch (err: any) {
      toast.error('Failed to delete recordings', { description: err.message });
    }
  };

  // Selection handlers
  const handleToggleSelect = (id: number) => {
    setSelectedIds(prev => {
      const newSet = new Set(prev);
      if (newSet.has(id)) {
        newSet.delete(id);
      } else {
        newSet.add(id);
      }
      return newSet;
    });
  };

  const handleSelectAll = () => {
    // Only select recordings that can be deleted (Scheduled, Completed, Failed, Cancelled, Imported)
    const deletableRecordings = visibleRecordings.filter(
      r => r.status === 'Scheduled' || r.status === 'Completed' || r.status === 'Failed' || r.status === 'Cancelled' || r.status === 'Imported' || r.status === 'Ignored'
    );
    if (selectedIds.size === deletableRecordings.length && deletableRecordings.length > 0) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(deletableRecordings.map(r => r.id)));
    }
  };

  // Check if a recording can be selected for bulk operations
  const canSelectRecording = (recording: DvrRecording) => {
    return recording.status === 'Scheduled' || recording.status === 'Completed' || recording.status === 'Failed' || recording.status === 'Cancelled' || recording.status === 'Imported' || recording.status === 'Ignored';
  };

  // Get deletable recordings count
  const deletableRecordingsCount = visibleRecordings.filter(canSelectRecording).length;

  const handleImportRecording = async (id: number) => {
    try {
      const response = await apiClient.post<{ success: boolean; error?: string }>(`/dvr/recordings/${id}/import`);
      if (response.data.success) {
        await loadRecordings();
        await loadStats();
        toast.success('Recording Imported', { description: 'Recording has been added to your library' });
      } else {
        toast.error('Failed to import recording', { description: response.data.error });
      }
    } catch (err: any) {
      toast.error('Failed to import recording', { description: err.message });
    }
  };

  const formatDuration = (start: string, end: string): string => {
    const startDate = new Date(start);
    const endDate = new Date(end);
    const durationMs = endDate.getTime() - startDate.getTime();
    const hours = Math.floor(durationMs / (1000 * 60 * 60));
    const minutes = Math.floor((durationMs % (1000 * 60 * 60)) / (1000 * 60));
    if (hours > 0) {
      return `${hours}h ${minutes}m`;
    }
    return `${minutes}m`;
  };

  const formatFileSize = (bytes?: number): string => {
    if (!bytes) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let unitIndex = 0;
    let size = bytes;
    while (size >= 1024 && unitIndex < units.length - 1) {
      size /= 1024;
      unitIndex++;
    }
    return `${size.toFixed(1)} ${units[unitIndex]}`;
  };

  const getStatusIcon = (status: RecordingStatus) => {
    switch (status) {
      case 'Scheduled':
        return <ClockIcon className="w-5 h-5 text-blue-400" />;
      case 'Recording':
        return <VideoCameraIcon className="w-5 h-5 text-red-400 animate-pulse" />;
      case 'Completed':
        return <CheckCircleIcon className="w-5 h-5 text-green-400" />;
      case 'Imported':
        return <ArrowDownOnSquareIcon className="w-5 h-5 text-green-400" />;
      case 'Ignored':
        return <CheckCircleIcon className="w-5 h-5 text-gray-400" />;
      case 'Failed':
        return <XCircleIcon className="w-5 h-5 text-red-400" />;
      case 'Cancelled':
        return <XMarkIcon className="w-5 h-5 text-gray-400" />;
      default:
        return <ClockIcon className="w-5 h-5 text-gray-400" />;
    }
  };

  const getStatusColor = (status: RecordingStatus): string => {
    switch (status) {
      case 'Scheduled':
        return 'bg-blue-900/30 text-blue-400';
      case 'Recording':
        return 'bg-red-900/30 text-red-400';
      case 'Completed':
        return 'bg-green-900/30 text-green-400';
      case 'Imported':
        return 'bg-green-900/30 text-green-400';
      case 'Ignored':
        return 'bg-gray-800 text-gray-400';
      case 'Failed':
        return 'bg-red-900/30 text-red-400';
      case 'Cancelled':
        return 'bg-gray-800 text-gray-400';
      default:
        return 'bg-gray-800 text-gray-400';
    }
  };

  const isFormValid = () => {
    return formData.eventTitle.trim() !== '' &&
      formData.channelId > 0 &&
      formData.scheduledStart !== '' &&
      formData.scheduledEnd !== '';
  };

  // Filter channels based on search query
  const filteredChannels = channels.filter(channel =>
    channel.name.toLowerCase().includes(channelSearch.toLowerCase())
  );

  // Get selected channel name
  const selectedChannel = channels.find(c => c.id === formData.channelId);

  // Handle channel selection
  const handleChannelSelect = (channelId: number) => {
    handleFormChange('channelId', channelId);
    setChannelSearch('');
    setShowChannelDropdown(false);
  };

  // Search events for the optional link, debounced so a fast typist does not
  // fire a request per keystroke.
  useEffect(() => {
    if (!showScheduleModal) return;
    const term = eventSearch.trim();
    if (term.length < 2) {
      setEventResults([]);
      return;
    }
    let cancelled = false;
    setIsSearchingEvents(true);
    const timer = setTimeout(async () => {
      try {
        const { data } = await apiClient.get<EventSearchResult[]>('/events/search', {
          params: { q: term, limit: 25 },
        });
        if (!cancelled) setEventResults(data);
      } catch {
        if (!cancelled) setEventResults([]);
      } finally {
        if (!cancelled) setIsSearchingEvents(false);
      }
    }, 300);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [eventSearch, showScheduleModal]);

  const handleEventSelect = (evt: EventSearchResult) => {
    setSelectedEvent(evt);
    setEventSearch('');
    setShowEventDropdown(false);
    setFormData(prev => ({
      ...prev,
      eventId: evt.id,
      // Only fill a title the user has not written themselves.
      eventTitle: prev.eventTitle.trim() === '' ? evt.title : prev.eventTitle,
    }));
  };

  const handleEventClear = () => {
    setSelectedEvent(null);
    setEventSearch('');
    setShowEventDropdown(false);
    setFormData(prev => ({ ...prev, eventId: undefined }));
  };

  const closeScheduleModal = () => {
    setShowScheduleModal(false);
    setFormData(defaultFormData);
    setChannelSearch('');
    setShowChannelDropdown(false);
    handleEventClear();
  };

  return (
    <PageShell className="pb-8">
      <PageHeader
        title="Recordings"
        subtitle="Upcoming, active, and completed event recordings"
        actions={
          <Menu as="div" className="relative">
            <Menu.Button className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-200 transition-colors hover:bg-gray-700">
              <EllipsisVerticalIcon className="h-5 w-5" /> More
            </Menu.Button>
            <Menu.Items className="absolute right-0 z-30 mt-1 w-56 overflow-hidden rounded-lg border border-gray-700 bg-gray-900 py-1 shadow-xl shadow-black/50 focus:outline-none">
              <Menu.Item>{({ active }) => (
                <button
                  onClick={() => setShowScheduleModal(true)}
                  disabled={ffmpegAvailable === false}
                  className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm text-gray-200 disabled:opacity-50 ${active ? 'bg-gray-800' : ''}`}
                >
                  <PlusIcon className="h-4 w-4" /> Manual recording
                </button>
              )}</Menu.Item>
              <Menu.Item>{({ active }) => (
                <button
                  onClick={() => {
                    setManageRecordings((current) => {
                      if (current) setSelectedIds(new Set());
                      return !current;
                    });
                  }}
                  className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm text-gray-200 ${active ? 'bg-gray-800' : ''}`}
                >
                  <WrenchScrewdriverIcon className="h-4 w-4" /> {manageRecordings ? 'Finish managing' : 'Manage recordings'}
                </button>
              )}</Menu.Item>
              <Menu.Item>{({ active }) => (
                <Link to="/iptv/settings/recording" className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm text-gray-200 ${active ? 'bg-gray-800' : ''}`}>
                  <Cog6ToothIcon className="h-4 w-4" /> Recording settings
                </Link>
              )}</Menu.Item>
            </Menu.Items>
          </Menu>
        }
      />

      {/* FFmpeg Warning */}
      {ffmpegAvailable === false && (
        <div className="mb-6 bg-yellow-950/30 border border-yellow-900/50 rounded-lg p-4 flex items-start">
          <ExclamationTriangleIcon className="w-6 h-6 text-yellow-400 mr-3 flex-shrink-0 mt-0.5" />
          <div className="min-w-0 flex-1">
            <h3 className="text-lg font-semibold text-yellow-400 mb-1">FFmpeg Not Found</h3>
            <p className="break-words text-sm text-gray-300">
              FFmpeg is required for DVR recordings. Please install FFmpeg and ensure it's available in your system PATH.
            </p>
          </div>
        </div>
      )}

      {/* Error Alert */}
      {error && (
        <div className="mb-6 bg-red-950/30 border border-red-900/50 rounded-lg p-4 flex items-start">
          <XCircleIcon className="w-6 h-6 text-red-400 mr-3 flex-shrink-0 mt-0.5" />
          <div className="flex-1">
            <h3 className="text-lg font-semibold text-red-400 mb-1">Error</h3>
            <p className="text-sm text-gray-300">{error}</p>
          </div>
          <button
            onClick={() => setError(null)}
            className="text-gray-400 hover:text-white ml-4"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>
      )}

        {/* Recordings List */}
        <div className="mb-8 rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black p-4 sm:p-6">
          <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-center gap-1 rounded-lg bg-gray-900 p-1">
              {([
                ['upcoming', `Upcoming${stats ? ` (${stats.scheduledCount})` : ''}`],
                ['recording', `Recording${stats ? ` (${stats.recordingCount})` : ''}`],
                ['completed', `Completed${stats ? ` (${getRecordingCompletedCount(stats)})` : ''}`],
                ['attention', `Needs attention${stats && getRecordingAttentionCount(stats) > 0 ? ` (${getRecordingAttentionCount(stats)})` : ''}`],
                ['all', 'All'],
              ] as const).map(([key, label]) => (
                <button
                  key={key}
                  onClick={() => setRecordingView(key)}
                  className={`min-h-10 rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                    recordingView === key ? 'bg-red-600 text-white' : 'text-gray-400 hover:bg-gray-800 hover:text-white'
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>
            <div className="flex items-center space-x-2">
              {stats && <span className="mr-2 text-sm text-gray-500">{formatFileSize(stats.totalStorageUsed)} used</span>}
              <button
                onClick={loadRecordings}
                className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-lg text-gray-400 transition-colors hover:bg-gray-800 hover:text-white"
                title="Refresh"
              >
                <ArrowPathIcon className="w-5 h-5" />
              </button>
            </div>
          </div>

          {/* Bulk Selection Controls */}
          {manageRecordings && deletableRecordingsCount > 0 && (
            <div className="flex items-center justify-between mb-4 pb-4 border-b border-gray-800">
              <div className="flex items-center space-x-4">
                <label className="flex items-center space-x-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={selectedIds.size === deletableRecordingsCount && deletableRecordingsCount > 0}
                    onChange={handleSelectAll}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                  />
                  <span className="text-sm text-gray-300">Select All ({deletableRecordingsCount})</span>
                </label>
                {selectedIds.size > 0 && (
                  <span className="text-sm text-gray-400">{selectedIds.size} selected</span>
                )}
              </div>
              {selectedIds.size > 0 && (
                <div className="flex items-center space-x-2">
                  <button
                    onClick={() => setShowBulkDeleteConfirm(true)}
                    className="flex items-center px-3 py-1.5 bg-red-900/30 hover:bg-red-900/50 text-red-400 rounded text-sm transition-colors"
                  >
                    <TrashIcon className="w-4 h-4 mr-1" />
                    Delete Selected ({selectedIds.size})
                  </button>
                  <button
                    onClick={() => setSelectedIds(new Set())}
                    className="px-3 py-1.5 text-gray-400 hover:text-white text-sm"
                  >
                    Clear Selection
                  </button>
                </div>
              )}
            </div>
          )}

          {/* A failed recording keeps whatever it managed to write. The file is
              hidden while it is being written, so without this nothing says the
              space is still taken. */}
          {failedCaptures.length > 0 && (
            <div className="mb-4 p-4 bg-red-950/30 border border-red-900/50 rounded-lg flex items-center justify-between gap-4">
              <div className="flex items-center gap-3 min-w-0">
                <ExclamationTriangleIcon className="w-5 h-5 text-red-400 flex-shrink-0" />
                <div className="min-w-0">
                  <div className="text-red-300 font-medium">
                    {failedCaptures.length} failed recording
                    {failedCaptures.length !== 1 ? 's' : ''} still holding{' '}
                    {formatFileSize(failedCaptures.reduce((sum, c) => sum + c.fileSize, 0))}
                  </div>
                  <div className="text-xs text-red-300/70">
                    These never finished, so their files are not in your library. Removing a
                    recording deletes its file.
                  </div>
                </div>
              </div>
              <button
                onClick={() => handleRemoveFailedCaptures(failedCaptures.map(c => c.id))}
                className="px-3 py-1.5 bg-red-900/50 hover:bg-red-900 text-red-200 rounded text-sm transition-colors flex-shrink-0"
              >
                Remove All
              </button>
            </div>
          )}

          <div className="space-y-3">
            {visibleRecordings.map((recording) => (
              <div
                key={recording.id}
                className={`group bg-black/30 border rounded-lg p-4 transition-all ${
                  selectedIds.has(recording.id) ? 'border-red-600 bg-red-950/20' : 'border-gray-800 hover:border-red-900/50'
                }`}
              >
                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div className="flex min-w-0 flex-1 items-start space-x-4">
                    {/* Selection Checkbox */}
                    {manageRecordings && canSelectRecording(recording) && (
                      <div className="mt-1">
                        <input
                          type="checkbox"
                          checked={selectedIds.has(recording.id)}
                          onChange={() => handleToggleSelect(recording.id)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                      </div>
                    )}
                    {/* Status Icon */}
                    <div className="mt-1">
                      {getStatusIcon(recording.status)}
                    </div>

                    {/* Recording Info */}
                    <div className="min-w-0 flex-1">
                      <div className="mb-2 flex flex-wrap items-center gap-2">
                        <h4 className="min-w-0 break-words text-lg font-semibold text-white">{recording.eventTitle}</h4>
                        <span className={`px-2 py-0.5 text-xs rounded ${getStatusColor(recording.status)}`}>
                          {recording.status}
                        </span>
                        {/* Catchup recordings download from the provider
                            archive after the event airs, not live */}
                        {recording.method === 'Catchup' && (
                          <span
                            className="px-2 py-0.5 bg-indigo-900/30 text-indigo-400 text-xs rounded"
                            title="Downloads from the provider's catchup archive after the event finishes"
                          >
                            Catchup
                          </span>
                        )}
                        {recording.leagueName && (
                          <span className="px-2 py-0.5 bg-purple-900/30 text-purple-400 text-xs rounded">
                            {recording.leagueName}
                          </span>
                        )}
                      </div>

                      <div className="grid grid-cols-1 md:grid-cols-2 gap-2 text-sm text-gray-400">
                        <p>
                          <span className="text-gray-500">Channel:</span>{' '}
                          <span className="text-white">{recording.channelName}</span>
                        </p>
                        <p>
                          <span className="text-gray-500">Duration:</span>{' '}
                          <span className="text-white">
                            {formatDuration(recording.scheduledStart, recording.scheduledEnd)}
                          </span>
                        </p>
                        <p>
                          <span className="text-gray-500">Start:</span>{' '}
                          <span className="text-white">
                            {formatDateInTimezone(recording.scheduledStart, timezone, { month: '2-digit', day: '2-digit', year: 'numeric' })}, {formatTimeInTimezone(recording.scheduledStart, timezone, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                          </span>
                        </p>
                        <p>
                          <span className="text-gray-500">End:</span>{' '}
                          <span className="text-white">
                            {formatDateInTimezone(recording.scheduledEnd, timezone, { month: '2-digit', day: '2-digit', year: 'numeric' })}, {formatTimeInTimezone(recording.scheduledEnd, timezone, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                          </span>
                        </p>
                        {recording.fileSize && recording.fileSize > 0 && (
                          <p>
                            <span className="text-gray-500">File Size:</span>{' '}
                            <span className="text-white">{formatFileSize(recording.fileSize)}</span>
                          </p>
                        )}
                        {/* Expected scores for scheduled recordings */}
                        {recording.status === 'Scheduled' && recording.expectedQualityName && (
                          <p>
                            <span className="text-gray-500">Expected Quality:</span>{' '}
                            <span className="text-amber-400">{recording.expectedQualityName}</span>
                          </p>
                        )}
                        {recording.status === 'Scheduled' && recording.expectedTotalScore !== undefined && (
                          <p>
                            <span className="text-gray-500">Expected Score:</span>{' '}
                            <span className={`font-semibold ${
                              recording.expectedTotalScore >= 0 ? 'text-green-400' : 'text-red-400'
                            }`}>
                              {recording.expectedTotalScore >= 0 ? '+' : ''}{recording.expectedTotalScore}
                            </span>
                            <span className="text-gray-600 text-xs ml-1">
                              (Q:{recording.expectedQualityScore ?? 0} + CF:{recording.expectedCustomFormatScore ?? 0})
                            </span>
                          </p>
                        )}
                        {recording.status === 'Scheduled' && recording.expectedMatchedFormats && recording.expectedMatchedFormats.length > 0 && (
                          <div className="col-span-2">
                            <div className="flex items-center gap-2 mb-1">
                              <span className="text-gray-500">Expected Formats:</span>
                              <span className="text-gray-600 text-xs">({recording.expectedMatchedFormats.length})</span>
                            </div>
                            <div className="max-h-24 overflow-y-auto bg-black/30 rounded p-2">
                              <div className="flex flex-wrap gap-1">
                                {recording.expectedMatchedFormats
                                  .slice()
                                  .sort((a, b) => {
                                    // Extract score from format string like "x264 (+1000)" or "LQ (-10000)"
                                    const extractScore = (str: string) => {
                                      const match = str.match(/\(([+-]?\d+)\)$/);
                                      return match ? parseInt(match[1]) : 0;
                                    };
                                    const scoreA = extractScore(a);
                                    const scoreB = extractScore(b);
                                    // Sort by score descending (highest first)
                                    if (scoreB !== scoreA) return scoreB - scoreA;
                                    // Then alphabetically
                                    return a.localeCompare(b);
                                  })
                                  .map((format, idx) => {
                                    // Parse score from format string for coloring
                                    const scoreMatch = format.match(/\(([+-]?\d+)\)$/);
                                    const score = scoreMatch ? parseInt(scoreMatch[1]) : 0;
                                    const formatName = scoreMatch ? format.replace(/\s*\([+-]?\d+\)$/, '') : format;
                                    const scoreStr = scoreMatch ? scoreMatch[1] : null;
                                    return (
                                      <span
                                        key={idx}
                                        className={`text-xs px-1.5 py-0.5 rounded whitespace-nowrap ${
                                          score > 0
                                            ? 'bg-green-900/30 text-green-400'
                                            : score < 0
                                              ? 'bg-red-900/30 text-red-400'
                                              : 'bg-gray-700/50 text-gray-300'
                                        }`}
                                      >
                                        {formatName}
                                        {scoreStr && (
                                          <span className="ml-1 opacity-75">
                                            ({score > 0 ? '+' : ''}{score})
                                          </span>
                                        )}
                                      </span>
                                    );
                                  })}
                              </div>
                            </div>
                          </div>
                        )}
                        {/* Actual quality for completed/imported recordings */}
                        {(recording.status === 'Completed' || recording.status === 'Imported' || recording.status === 'Ignored') && recording.quality && (
                          <p>
                            <span className="text-gray-500">Quality:</span>{' '}
                            <span className="text-green-400">{recording.quality}</span>
                          </p>
                        )}
                        {(recording.status === 'Completed' || recording.status === 'Imported' || recording.status === 'Ignored') && recording.qualityScore !== undefined && (
                          <p>
                            <span className="text-gray-500">Score:</span>{' '}
                            <span className={`font-semibold ${
                              (recording.qualityScore + (recording.customFormatScore ?? 0)) >= 0 ? 'text-green-400' : 'text-red-400'
                            }`}>
                              {(recording.qualityScore + (recording.customFormatScore ?? 0)) >= 0 ? '+' : ''}
                              {recording.qualityScore + (recording.customFormatScore ?? 0)}
                            </span>
                            <span className="text-gray-600 text-xs ml-1">
                              (Q:{recording.qualityScore} + CF:{recording.customFormatScore ?? 0})
                            </span>
                          </p>
                        )}
                        {recording.errorMessage && (
                          <p className="col-span-2 text-red-400">
                            <span className="text-gray-500">Error:</span> {recording.errorMessage}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>

                  {/* Actions */}
                  <div className="flex shrink-0 items-center justify-end space-x-2 self-end sm:ml-4 sm:self-start">
                    {recording.status === 'Scheduled' && (
                      <>
                        {/* A catchup recording downloads after the event, and
                            the backend refuses to start it live, so the button
                            only offered a guaranteed error toast. */}
                        {recording.method !== 'Catchup' && (
                          <button
                            onClick={() => handleStartRecording(recording.id)}
                            className="p-2 text-gray-400 hover:text-green-400 hover:bg-green-950/30 rounded transition-colors"
                            title="Start Now"
                          >
                            <PlayIcon className="w-5 h-5" />
                          </button>
                        )}
                        <button
                          onClick={() => setShowDeleteConfirm(recording.id)}
                          className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                          title="Delete"
                        >
                          <TrashIcon className="w-5 h-5" />
                        </button>
                      </>
                    )}
                    {recording.status === 'Recording' && (
                      <button
                        onClick={() => handleStopRecording(recording.id)}
                        className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                        title="Stop Recording"
                      >
                        <StopIcon className="w-5 h-5" />
                      </button>
                    )}
                    {recording.status === 'Completed' && recording.outputPath && (
                      <>
                        <button
                          onClick={() => setViewingRecording(recording)}
                          className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                          title="View Details"
                        >
                          <FilmIcon className="w-5 h-5" />
                        </button>
                        {recording.eventId && (
                          <button
                            onClick={() => handleImportRecording(recording.id)}
                            className="p-2 text-gray-400 hover:text-green-400 hover:bg-green-950/30 rounded transition-colors"
                            title="Import to Library"
                          >
                            <ArrowDownOnSquareIcon className="w-5 h-5" />
                          </button>
                        )}
                      </>
                    )}
                    {recording.status === 'Imported' && (
                      <span className="px-2 py-1 bg-green-900/30 text-green-400 text-xs rounded">
                        Imported
                      </span>
                    )}
                    {(recording.status === 'Completed' || recording.status === 'Failed' || recording.status === 'Cancelled' || recording.status === 'Ignored') && (
                      <button
                        onClick={() => setShowDeleteConfirm(recording.id)}
                        className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                        title="Delete"
                      >
                        <TrashIcon className="w-5 h-5" />
                      </button>
                    )}
                  </div>
                </div>
              </div>
            ))}
          </div>

          {isLoading && (
            <div className="text-center py-12">
              <div className="animate-spin rounded-full h-16 w-16 border-b-2 border-red-600 mx-auto mb-4"></div>
              <p className="text-gray-500">Loading recordings...</p>
            </div>
          )}

          {!isLoading && visibleRecordings.length === 0 && (
            <div className="text-center py-12">
              <VideoCameraIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
              <p className="text-gray-500 mb-2">No recordings found</p>
              <p className="text-sm text-gray-400">
                {recordingView !== 'all'
                  ? `No recordings in ${recordingView === 'attention' ? 'Needs attention' : recordingView}`
                  : 'Schedule a recording or add events with IPTV channel mappings'}
              </p>
            </div>
          )}
        </div>

        {/* Schedule Manual Recording Modal */}
        {showScheduleModal && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-8 max-w-3xl w-full my-8">
              <div className="flex items-center justify-between mb-6">
                <div>
                  <h3 className="text-2xl font-bold text-white">Schedule Manual Recording</h3>
                  <p className="text-sm text-gray-400 mt-1">
                    Record any channel at a specific time. For automatic event recording, map channels to leagues in IPTV Channels.
                  </p>
                </div>
                <button
                  onClick={closeScheduleModal}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <div className="space-y-6">
                {/* Event Title */}
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">Event Title *</label>
                  <input
                    type="text"
                    value={formData.eventTitle}
                    onChange={(e) => handleFormChange('eventTitle', e.target.value)}
                    className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="e.g., NFL: Patriots vs Cowboys"
                  />
                </div>

                {/* Optional link to an event in the library */}
                {/* The wrapper carries the stacking context, not just the
                    dropdown: a native select paints above a plain z-indexed
                    sibling, so the results panel lost to Import Mode below. */}
                <div className="relative z-40">
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    Link to Event <span className="text-gray-500 font-normal">(optional)</span>
                  </label>

                  {selectedEvent ? (
                    <div className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <div className="truncate">{selectedEvent.title}</div>
                        <div className="text-xs text-gray-500 truncate">
                          {[selectedEvent.leagueName, selectedEvent.sport]
                            .filter(Boolean)
                            .join(' · ')}
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={handleEventClear}
                        className="text-gray-400 hover:text-white flex-shrink-0"
                      >
                        <XMarkIcon className="w-5 h-5" />
                      </button>
                    </div>
                  ) : (
                    <input
                      type="text"
                      value={eventSearch}
                      onChange={(e) => {
                        setEventSearch(e.target.value);
                        setShowEventDropdown(true);
                      }}
                      onFocus={() => setShowEventDropdown(true)}
                      className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="Type to search your events..."
                    />
                  )}

                  <p className="text-xs text-gray-500 mt-1">
                    A linked recording is imported against that event when it finishes. Leave
                    it empty to keep the recording as a standalone file.
                  </p>

                  {showEventDropdown && !selectedEvent && (
                    <div className="absolute z-30 w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg shadow-xl max-h-64 overflow-y-auto">
                      {eventSearch.trim().length < 2 ? (
                        <div className="px-4 py-3 text-gray-500 text-center">
                          Type at least two characters
                        </div>
                      ) : isSearchingEvents ? (
                        <div className="px-4 py-3 text-gray-500 text-center">Searching...</div>
                      ) : eventResults.length === 0 ? (
                        <div className="px-4 py-3 text-gray-500 text-center">
                          No events match your search
                        </div>
                      ) : (
                        eventResults.map((evt) => (
                          <button
                            key={evt.id}
                            type="button"
                            onClick={() => handleEventSelect(evt)}
                            className="w-full px-4 py-3 text-left hover:bg-gray-700 transition-colors text-white"
                          >
                            <div className="truncate">{evt.title}</div>
                            <div className="text-xs text-gray-500 truncate">
                              {[
                                evt.leagueName,
                                evt.eventDate
                                  ? new Date(evt.eventDate).toLocaleDateString()
                                  : null,
                              ]
                                .filter(Boolean)
                                .join(' · ')}
                            </div>
                          </button>
                        ))
                      )}
                    </div>
                  )}

                  {showEventDropdown && !selectedEvent && (
                    <div
                      className="fixed inset-0 z-0"
                      onClick={() => setShowEventDropdown(false)}
                    />
                  )}
                </div>

                {/* Searchable Channel Selector */}
                <div className="relative">
                  <label className="block text-sm font-medium text-gray-300 mb-2">Channel *</label>

                  {/* Selected channel display or search input */}
                  {selectedChannel && !showChannelDropdown ? (
                    <div
                      onClick={() => setShowChannelDropdown(true)}
                      className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white cursor-pointer hover:border-gray-600 flex items-center justify-between"
                    >
                      <span>{selectedChannel.name}</span>
                      <span className="text-gray-500 text-sm">Click to change</span>
                    </div>
                  ) : (
                    <input
                      type="text"
                      value={channelSearch}
                      onChange={(e) => {
                        setChannelSearch(e.target.value);
                        setShowChannelDropdown(true);
                      }}
                      onFocus={() => setShowChannelDropdown(true)}
                      className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="Type to search channels..."
                    />
                  )}

                  {/* Channel dropdown */}
                  {showChannelDropdown && (
                    <div className="absolute z-10 w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg shadow-xl max-h-64 overflow-y-auto">
                      {filteredChannels.length === 0 ? (
                        <div className="px-4 py-3 text-gray-500 text-center">
                          {channels.length === 0
                            ? 'No enabled channels available. Enable channels in IPTV Channels settings.'
                            : 'No channels match your search'}
                        </div>
                      ) : (
                        <>
                          <div className="sticky top-0 bg-gray-800 px-4 py-2 border-b border-gray-700">
                            <span className="text-xs text-gray-500">
                              {filteredChannels.length} of {channels.length} channels
                            </span>
                          </div>
                          {filteredChannels.slice(0, 100).map((channel) => (
                            <button
                              key={channel.id}
                              onClick={() => handleChannelSelect(channel.id)}
                              className={`w-full px-4 py-3 text-left hover:bg-gray-700 transition-colors flex items-center justify-between ${
                                formData.channelId === channel.id ? 'bg-red-900/30 text-red-400' : 'text-white'
                              }`}
                            >
                              <span className="truncate">{channel.name}</span>
                              {formData.channelId === channel.id && (
                                <CheckCircleIcon className="w-5 h-5 text-red-400 flex-shrink-0 ml-2" />
                              )}
                            </button>
                          ))}
                          {filteredChannels.length > 100 && (
                            <div className="px-4 py-2 text-xs text-gray-500 text-center border-t border-gray-700">
                              Showing first 100 results. Type to narrow search.
                            </div>
                          )}
                        </>
                      )}
                    </div>
                  )}

                  {/* Click outside to close dropdown */}
                  {showChannelDropdown && (
                    <div
                      className="fixed inset-0 z-0"
                      onClick={() => setShowChannelDropdown(false)}
                    />
                  )}
                </div>

                {/* How the finished recording is placed in the library */}
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">Import Mode</label>
                  <select
                    value={formData.importMode ?? ''}
                    onChange={(e) => handleFormChange('importMode', e.target.value)}
                    className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  >
                    <option value="">Leave in place (default)</option>
                    <option value="hardlink">Hardlink into the library</option>
                    <option value="copy">Copy into the library</option>
                    <option value="move">Move into the library</option>
                  </select>
                  <p className="text-xs text-gray-500 mt-1">
                    Leave in place keeps the recording in your DVR folder and points the event at
                    it. The other three organise it into the library with your usual folder and
                    naming rules, and need a linked event.
                  </p>
                </div>

                {/* Date/Time Selection */}
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Start Time *</label>
                    <input
                      type="datetime-local"
                      value={formData.scheduledStart}
                      onChange={(e) => handleFormChange('scheduledStart', e.target.value)}
                      className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">End Time *</label>
                    <input
                      type="datetime-local"
                      value={formData.scheduledEnd}
                      onChange={(e) => handleFormChange('scheduledEnd', e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                  </div>
                </div>

                {/* Padding Settings */}
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Pre-padding (minutes)</label>
                    <input
                      type="number"
                      value={formData.prePadding}
                      onChange={(e) => handleFormChange('prePadding', parseInt(e.target.value) || 0)}
                      min="0"
                      max="60"
                      className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Start recording before scheduled time</p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Post-padding (minutes)</label>
                    <input
                      type="number"
                      value={formData.postPadding}
                      onChange={(e) => handleFormChange('postPadding', parseInt(e.target.value) || 0)}
                      min="0"
                      max="120"
                      className="w-full px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Continue recording after scheduled end</p>
                  </div>
                </div>
              </div>

              <div className="mt-8 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
                <button
                  onClick={closeScheduleModal}
                  className="px-6 py-3 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={() => {
                    handleScheduleRecording();
                    setChannelSearch('');
                    setShowChannelDropdown(false);
                  }}
                  disabled={!isFormValid()}
                  className={`px-6 py-3 rounded-lg transition-colors ${
                    isFormValid()
                      ? 'bg-red-600 hover:bg-red-700 text-white'
                      : 'bg-gray-700 text-gray-500 cursor-not-allowed'
                  }`}
                >
                  Schedule Recording
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Delete Confirmation Modal */}
        {showDeleteConfirm !== null && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
              <h3 className="text-2xl font-bold text-white mb-4">Delete Recording?</h3>
              <p className="text-gray-400 mb-6">
                Are you sure you want to delete this recording? This will also delete the recorded file if it exists. This action cannot be undone.
              </p>
              <div className="flex items-center justify-end space-x-3">
                <button
                  onClick={() => setShowDeleteConfirm(null)}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={() => handleDeleteRecording(showDeleteConfirm)}
                  className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Delete
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Bulk Delete Confirmation Modal */}
        {showBulkDeleteConfirm && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
              <h3 className="text-2xl font-bold text-white mb-4">Delete {selectedIds.size} Recordings?</h3>
              <p className="text-gray-400 mb-6">
                Are you sure you want to delete {selectedIds.size} recording{selectedIds.size !== 1 ? 's' : ''}?
                This will also delete the recorded files if they exist. This action cannot be undone.
              </p>
              <div className="flex items-center justify-end space-x-3">
                <button
                  onClick={() => setShowBulkDeleteConfirm(false)}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={() => {
                    handleBulkDelete();
                    setShowBulkDeleteConfirm(false);
                  }}
                  className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Delete All
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Recording Details Modal */}
        {viewingRecording && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full my-8">
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-2xl font-bold text-white">Recording Details</h3>
                <button
                  onClick={() => setViewingRecording(null)}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <div className="space-y-4">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div>
                    <span className="text-gray-500 text-sm">Title</span>
                    <p className="text-white font-medium">{viewingRecording.eventTitle}</p>
                  </div>
                  <div>
                    <span className="text-gray-500 text-sm">Status</span>
                    <p className={`font-medium ${
                      viewingRecording.status === 'Completed' ? 'text-green-400' : 'text-gray-400'
                    }`}>
                      {viewingRecording.status}
                    </p>
                  </div>
                  <div>
                    <span className="text-gray-500 text-sm">Channel</span>
                    <p className="text-white">{viewingRecording.channelName}</p>
                  </div>
                  {viewingRecording.leagueName && (
                    <div>
                      <span className="text-gray-500 text-sm">League</span>
                      <p className="text-white">{viewingRecording.leagueName}</p>
                    </div>
                  )}
                  <div>
                    <span className="text-gray-500 text-sm">Scheduled Start</span>
                    <p className="text-white">{formatDateInTimezone(viewingRecording.scheduledStart, timezone, { month: '2-digit', day: '2-digit', year: 'numeric' })}, {formatTimeInTimezone(viewingRecording.scheduledStart, timezone, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</p>
                  </div>
                  <div>
                    <span className="text-gray-500 text-sm">Scheduled End</span>
                    <p className="text-white">{formatDateInTimezone(viewingRecording.scheduledEnd, timezone, { month: '2-digit', day: '2-digit', year: 'numeric' })}, {formatTimeInTimezone(viewingRecording.scheduledEnd, timezone, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</p>
                  </div>
                  {viewingRecording.actualStart && (
                    <div>
                      <span className="text-gray-500 text-sm">Actual Start</span>
                      <p className="text-white">{formatDateInTimezone(viewingRecording.actualStart, timezone, { month: '2-digit', day: '2-digit', year: 'numeric' })}, {formatTimeInTimezone(viewingRecording.actualStart, timezone, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</p>
                    </div>
                  )}
                  {viewingRecording.actualEnd && (
                    <div>
                      <span className="text-gray-500 text-sm">Actual End</span>
                      <p className="text-white">{formatDateInTimezone(viewingRecording.actualEnd, timezone, { month: '2-digit', day: '2-digit', year: 'numeric' })}, {formatTimeInTimezone(viewingRecording.actualEnd, timezone, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</p>
                    </div>
                  )}
                  <div>
                    <span className="text-gray-500 text-sm">Duration</span>
                    <p className="text-white">
                      {formatDuration(
                        viewingRecording.actualStart || viewingRecording.scheduledStart,
                        viewingRecording.actualEnd || viewingRecording.scheduledEnd
                      )}
                    </p>
                  </div>
                  {viewingRecording.fileSize && viewingRecording.fileSize > 0 && (
                    <div>
                      <span className="text-gray-500 text-sm">File Size</span>
                      <p className="text-white">{formatFileSize(viewingRecording.fileSize)}</p>
                    </div>
                  )}
                </div>

                {viewingRecording.outputPath && (
                  <div>
                    <span className="text-gray-500 text-sm">File Path</span>
                    <p className="text-white font-mono text-sm bg-black/50 p-2 rounded mt-1 break-all">
                      {viewingRecording.outputPath}
                    </p>
                  </div>
                )}

                {viewingRecording.errorMessage && (
                  <div>
                    <span className="text-gray-500 text-sm">Error</span>
                    <p className="text-red-400 bg-red-950/30 p-2 rounded mt-1">
                      {viewingRecording.errorMessage}
                    </p>
                  </div>
                )}
              </div>

              <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end">
                <button
                  onClick={() => setViewingRecording(null)}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        )}
    </PageShell>
  );
}
