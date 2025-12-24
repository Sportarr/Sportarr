import { useState, useEffect } from 'react';
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
  CalendarDaysIcon,
  PlusIcon,
  ArrowDownOnSquareIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import SettingsHeader from '../../components/SettingsHeader';

// DVR Recording Types
type RecordingStatus = 'Scheduled' | 'Recording' | 'Completed' | 'Failed' | 'Cancelled' | 'Importing' | 'Imported';

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
}

interface DvrStats {
  totalRecordings: number;
  scheduledCount: number;
  recordingCount: number;
  completedCount: number;
  failedCount: number;
  totalStorageUsed: number;
}

interface ScheduleFormData {
  eventTitle: string;
  channelId: number;
  scheduledStart: string;
  scheduledEnd: string;
  prePadding: number;
  postPadding: number;
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
};

export default function DvrRecordingsSettings() {
  // State
  const [recordings, setRecordings] = useState<DvrRecording[]>([]);
  const [stats, setStats] = useState<DvrStats | null>(null);
  const [channels, setChannels] = useState<IptvChannel[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filter state
  const [statusFilter, setStatusFilter] = useState<RecordingStatus | 'All'>('All');

  // Modal state
  const [showScheduleModal, setShowScheduleModal] = useState(false);
  const [formData, setFormData] = useState<ScheduleFormData>(defaultFormData);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [viewingRecording, setViewingRecording] = useState<DvrRecording | null>(null);

  // FFmpeg state
  const [ffmpegAvailable, setFfmpegAvailable] = useState<boolean | null>(null);

  // Load data on mount
  useEffect(() => {
    loadData();
    checkFfmpeg();
    // Refresh every 30 seconds to update recording statuses
    const interval = setInterval(loadRecordings, 30000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    loadRecordings();
  }, [statusFilter]);

  const loadData = async () => {
    await Promise.all([loadRecordings(), loadStats(), loadChannels()]);
  };

  const loadRecordings = async () => {
    try {
      setIsLoading(true);
      const params: Record<string, string> = {};
      if (statusFilter !== 'All') {
        params.status = statusFilter;
      }
      const { data } = await apiClient.get<DvrRecording[]>('/dvr/recordings', { params });
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
      const { data } = await apiClient.get<{ available: boolean; path?: string; error?: string }>('/dvr/ffmpeg/check');
      setFfmpegAvailable(data.available);
    } catch (err: any) {
      setFfmpegAvailable(false);
    }
  };

  const handleFormChange = (field: keyof ScheduleFormData, value: any) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const handleScheduleRecording = async () => {
    try {
      setError(null);
      const response = await apiClient.post<DvrRecording>('/dvr/recordings', formData);
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

  return (
    <div>
      <SettingsHeader
        title="DVR Recordings"
        subtitle="Manage scheduled and completed DVR recordings"
        showSaveButton={false}
      />

      <div className="max-w-6xl mx-auto px-6">
        {/* FFmpeg Warning */}
        {ffmpegAvailable === false && (
          <div className="mb-6 bg-yellow-950/30 border border-yellow-900/50 rounded-lg p-4 flex items-start">
            <ExclamationTriangleIcon className="w-6 h-6 text-yellow-400 mr-3 flex-shrink-0 mt-0.5" />
            <div className="flex-1">
              <h3 className="text-lg font-semibold text-yellow-400 mb-1">FFmpeg Not Found</h3>
              <p className="text-sm text-gray-300">
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

        {/* Stats Cards */}
        {stats && (
          <div className="grid grid-cols-2 md:grid-cols-5 gap-4 mb-8">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-800 rounded-lg p-4">
              <div className="text-2xl font-bold text-white">{stats.totalRecordings}</div>
              <div className="text-sm text-gray-400">Total Recordings</div>
            </div>
            <div className="bg-gradient-to-br from-gray-900 to-black border border-blue-900/30 rounded-lg p-4">
              <div className="text-2xl font-bold text-blue-400">{stats.scheduledCount}</div>
              <div className="text-sm text-gray-400">Scheduled</div>
            </div>
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-4">
              <div className="text-2xl font-bold text-red-400">{stats.recordingCount}</div>
              <div className="text-sm text-gray-400">Recording Now</div>
            </div>
            <div className="bg-gradient-to-br from-gray-900 to-black border border-green-900/30 rounded-lg p-4">
              <div className="text-2xl font-bold text-green-400">{stats.completedCount}</div>
              <div className="text-sm text-gray-400">Completed</div>
            </div>
            <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-800 rounded-lg p-4">
              <div className="text-2xl font-bold text-white">{formatFileSize(stats.totalStorageUsed)}</div>
              <div className="text-sm text-gray-400">Storage Used</div>
            </div>
          </div>
        )}

        {/* Info Box */}
        <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
          <div className="flex items-start">
            <VideoCameraIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
            <div>
              <h3 className="text-lg font-semibold text-white mb-2">About DVR Recordings</h3>
              <ul className="space-y-2 text-sm text-gray-300">
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    <strong>Automatic Recording:</strong> Events with IPTV channel mappings are recorded automatically
                  </span>
                </li>
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    <strong>Manual Recording:</strong> Schedule recordings for any channel and time
                  </span>
                </li>
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    <strong>Pre/Post Padding:</strong> Start recording early and end late to capture full events
                  </span>
                </li>
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    Recordings are saved as .ts (Transport Stream) files for best compatibility
                  </span>
                </li>
              </ul>
            </div>
          </div>
        </div>

        {/* Recordings List */}
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-6">
            <div className="flex items-center space-x-4">
              <h3 className="text-xl font-semibold text-white">Recordings</h3>
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value as RecordingStatus | 'All')}
                className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
              >
                <option value="All">All Status</option>
                <option value="Scheduled">Scheduled</option>
                <option value="Recording">Recording</option>
                <option value="Completed">Completed</option>
                <option value="Imported">Imported</option>
                <option value="Failed">Failed</option>
                <option value="Cancelled">Cancelled</option>
              </select>
            </div>
            <div className="flex items-center space-x-2">
              <button
                onClick={loadRecordings}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded-lg transition-colors"
                title="Refresh"
              >
                <ArrowPathIcon className="w-5 h-5" />
              </button>
              <button
                onClick={() => setShowScheduleModal(true)}
                disabled={ffmpegAvailable === false}
                className={`flex items-center px-4 py-2 rounded-lg transition-colors ${
                  ffmpegAvailable !== false
                    ? 'bg-red-600 hover:bg-red-700 text-white'
                    : 'bg-gray-700 text-gray-500 cursor-not-allowed'
                }`}
              >
                <PlusIcon className="w-4 h-4 mr-2" />
                Schedule Recording
              </button>
            </div>
          </div>

          <div className="space-y-3">
            {recordings.map((recording) => (
              <div
                key={recording.id}
                className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
              >
                <div className="flex items-start justify-between">
                  <div className="flex items-start space-x-4 flex-1">
                    {/* Status Icon */}
                    <div className="mt-1">
                      {getStatusIcon(recording.status)}
                    </div>

                    {/* Recording Info */}
                    <div className="flex-1">
                      <div className="flex items-center space-x-3 mb-2">
                        <h4 className="text-lg font-semibold text-white">{recording.eventTitle}</h4>
                        <span className={`px-2 py-0.5 text-xs rounded ${getStatusColor(recording.status)}`}>
                          {recording.status}
                        </span>
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
                            {new Date(recording.scheduledStart).toLocaleString()}
                          </span>
                        </p>
                        <p>
                          <span className="text-gray-500">End:</span>{' '}
                          <span className="text-white">
                            {new Date(recording.scheduledEnd).toLocaleString()}
                          </span>
                        </p>
                        {recording.fileSize && recording.fileSize > 0 && (
                          <p>
                            <span className="text-gray-500">File Size:</span>{' '}
                            <span className="text-white">{formatFileSize(recording.fileSize)}</span>
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
                  <div className="flex items-center space-x-2 ml-4">
                    {recording.status === 'Scheduled' && (
                      <>
                        <button
                          onClick={() => handleStartRecording(recording.id)}
                          className="p-2 text-gray-400 hover:text-green-400 hover:bg-green-950/30 rounded transition-colors"
                          title="Start Now"
                        >
                          <PlayIcon className="w-5 h-5" />
                        </button>
                        <button
                          onClick={() => handleCancelRecording(recording.id)}
                          className="p-2 text-gray-400 hover:text-yellow-400 hover:bg-yellow-950/30 rounded transition-colors"
                          title="Cancel"
                        >
                          <XMarkIcon className="w-5 h-5" />
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
                    {(recording.status === 'Completed' || recording.status === 'Failed' || recording.status === 'Cancelled') && (
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

          {!isLoading && recordings.length === 0 && (
            <div className="text-center py-12">
              <VideoCameraIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
              <p className="text-gray-500 mb-2">No recordings found</p>
              <p className="text-sm text-gray-400">
                {statusFilter !== 'All'
                  ? `No ${statusFilter.toLowerCase()} recordings`
                  : 'Schedule a recording or add events with IPTV channel mappings'}
              </p>
            </div>
          )}
        </div>

        {/* Schedule Recording Modal */}
        {showScheduleModal && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-lg w-full my-8">
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-2xl font-bold text-white">Schedule Recording</h3>
                <button
                  onClick={() => {
                    setShowScheduleModal(false);
                    setFormData(defaultFormData);
                  }}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">Event Title *</label>
                  <input
                    type="text"
                    value={formData.eventTitle}
                    onChange={(e) => handleFormChange('eventTitle', e.target.value)}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="e.g., NFL: Patriots vs Cowboys"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">Channel *</label>
                  <select
                    value={formData.channelId}
                    onChange={(e) => handleFormChange('channelId', parseInt(e.target.value))}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  >
                    <option value={0}>Select a channel...</option>
                    {channels.map((channel) => (
                      <option key={channel.id} value={channel.id}>
                        {channel.name}
                      </option>
                    ))}
                  </select>
                  {channels.length === 0 && (
                    <p className="text-xs text-yellow-400 mt-1">
                      No enabled channels available. Enable channels in IPTV Channels settings.
                    </p>
                  )}
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Start Time *</label>
                    <input
                      type="datetime-local"
                      value={formData.scheduledStart}
                      onChange={(e) => handleFormChange('scheduledStart', e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
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

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Pre-padding (minutes)</label>
                    <input
                      type="number"
                      value={formData.prePadding}
                      onChange={(e) => handleFormChange('prePadding', parseInt(e.target.value) || 0)}
                      min="0"
                      max="60"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Start recording early</p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Post-padding (minutes)</label>
                    <input
                      type="number"
                      value={formData.postPadding}
                      onChange={(e) => handleFormChange('postPadding', parseInt(e.target.value) || 0)}
                      min="0"
                      max="120"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Continue recording after</p>
                  </div>
                </div>
              </div>

              <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
                <button
                  onClick={() => {
                    setShowScheduleModal(false);
                    setFormData(defaultFormData);
                  }}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={handleScheduleRecording}
                  disabled={!isFormValid()}
                  className={`px-4 py-2 rounded-lg transition-colors ${
                    isFormValid()
                      ? 'bg-red-600 hover:bg-red-700 text-white'
                      : 'bg-gray-700 text-gray-500 cursor-not-allowed'
                  }`}
                >
                  Schedule
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
                <div className="grid grid-cols-2 gap-4">
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
                    <p className="text-white">{new Date(viewingRecording.scheduledStart).toLocaleString()}</p>
                  </div>
                  <div>
                    <span className="text-gray-500 text-sm">Scheduled End</span>
                    <p className="text-white">{new Date(viewingRecording.scheduledEnd).toLocaleString()}</p>
                  </div>
                  {viewingRecording.actualStart && (
                    <div>
                      <span className="text-gray-500 text-sm">Actual Start</span>
                      <p className="text-white">{new Date(viewingRecording.actualStart).toLocaleString()}</p>
                    </div>
                  )}
                  {viewingRecording.actualEnd && (
                    <div>
                      <span className="text-gray-500 text-sm">Actual End</span>
                      <p className="text-white">{new Date(viewingRecording.actualEnd).toLocaleString()}</p>
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
      </div>
    </div>
  );
}
