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
  Cog6ToothIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  CpuChipIcon,
  FolderIcon,
  SparklesIcon,
  SpeakerWaveIcon,
  AdjustmentsHorizontalIcon,
  InformationCircleIcon,
  ChartBarIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import type { QualityProfile } from '../../types';
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

// DVR Settings Types
interface DvrSettings {
  defaultProfileId: number;
  recordingPath: string;
  fileNamingPattern: string;
  prePaddingMinutes: number;
  postPaddingMinutes: number;
  maxConcurrentRecordings: number;
  deleteAfterImport: boolean;
  recordingRetentionDays: number;
  hardwareAcceleration: number;
  ffmpegPath: string;
  enableReconnect: boolean;
  maxReconnectAttempts: number;
  reconnectDelaySeconds: number;
}

interface DvrQualityProfile {
  id: number;
  name: string;
  preset: number;
  videoCodec: string;
  audioCodec: string;
  videoBitrate: number;
  audioBitrate: number;
  resolution: string;
  frameRate: string;
  encodingPreset: string;
  container: string;
  isDefault: boolean;
  estimatedSizePerHourMb: number;
  estimatedQualityScore: number;
  estimatedCustomFormatScore: number;
  expectedQualityName: string;
  expectedFormatDescription: string;
  audioChannels?: string;
  constantRateFactor?: number;
}

interface DvrQualityScorePreview {
  qualityScore: number;
  customFormatScore: number;
  totalScore: number;
  qualityName: string;
  formatDescription: string;
  syntheticTitle: string;
  matchedFormats: string[];
}

interface HardwareAccelerationInfo {
  type: number;
  name: string;
  description: string;
  isAvailable: boolean;
}

// Hardware acceleration enum values
const HardwareAccelerationOptions: { value: number; label: string; description: string }[] = [
  { value: 0, label: 'None', description: 'Software encoding only (CPU)' },
  { value: 1, label: 'NVENC (NVIDIA)', description: 'NVIDIA GPU hardware encoding' },
  { value: 2, label: 'QuickSync (Intel)', description: 'Intel GPU hardware encoding' },
  { value: 3, label: 'AMF (AMD)', description: 'AMD GPU hardware encoding' },
  { value: 4, label: 'VAAPI (Linux)', description: 'Linux hardware encoding (Intel/AMD)' },
  { value: 5, label: 'VideoToolbox (macOS)', description: 'macOS hardware encoding' },
  { value: 99, label: 'Auto-detect', description: 'Automatically detect best available encoder' },
];

const defaultDvrSettings: DvrSettings = {
  defaultProfileId: 1,
  recordingPath: '',
  fileNamingPattern: '{Title} - {Date}',
  prePaddingMinutes: 5,
  postPaddingMinutes: 30,
  maxConcurrentRecordings: 0,
  deleteAfterImport: false,
  recordingRetentionDays: 0,
  hardwareAcceleration: 99,
  ffmpegPath: '',
  enableReconnect: true,
  maxReconnectAttempts: 5,
  reconnectDelaySeconds: 5,
};

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

  // Bulk selection state
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());

  // Modal state
  const [showScheduleModal, setShowScheduleModal] = useState(false);
  const [formData, setFormData] = useState<ScheduleFormData>(defaultFormData);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [showBulkDeleteConfirm, setShowBulkDeleteConfirm] = useState(false);
  const [viewingRecording, setViewingRecording] = useState<DvrRecording | null>(null);

  // Channel search state for modal
  const [channelSearch, setChannelSearch] = useState('');
  const [showChannelDropdown, setShowChannelDropdown] = useState(false);

  // FFmpeg state
  const [ffmpegAvailable, setFfmpegAvailable] = useState<boolean | null>(null);

  // DVR Settings state
  const [dvrSettings, setDvrSettings] = useState<DvrSettings>(defaultDvrSettings);
  const [qualityProfiles, setQualityProfiles] = useState<DvrQualityProfile[]>([]);
  const [availableHwAccel, setAvailableHwAccel] = useState<HardwareAccelerationInfo[]>([]);
  const [settingsExpanded, setSettingsExpanded] = useState(false);
  const [isSavingSettings, setIsSavingSettings] = useState(false);
  const [settingsHasChanges, setSettingsHasChanges] = useState(false);
  const [originalSettings, setOriginalSettings] = useState<DvrSettings>(defaultDvrSettings);

  // Custom profile editing state
  const [editingProfile, setEditingProfile] = useState<DvrQualityProfile | null>(null);
  const [scorePreview, setScorePreview] = useState<DvrQualityScorePreview | null>(null);
  const [isLoadingScorePreview, setIsLoadingScorePreview] = useState(false);
  const [gbPerHour, setGbPerHour] = useState(4); // Default 4 GB/hour

  // User's quality profiles (for TRaSH-style scoring)
  const [userQualityProfiles, setUserQualityProfiles] = useState<QualityProfile[]>([]);
  const [selectedQualityProfileId, setSelectedQualityProfileId] = useState<number | null>(null);

  // Load data on mount
  useEffect(() => {
    loadData();
    checkFfmpeg();
    loadDvrSettings();
    loadQualityProfiles();
    loadHardwareAcceleration();
    loadUserQualityProfiles();
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

  const loadDvrSettings = async () => {
    try {
      const { data } = await apiClient.get<DvrSettings>('/dvr/settings');
      setDvrSettings(data);
      setOriginalSettings(data);
      setSettingsHasChanges(false);
    } catch (err: any) {
      console.error('Failed to load DVR settings:', err);
    }
  };

  const loadQualityProfiles = async () => {
    try {
      const { data } = await apiClient.get<DvrQualityProfile[]>('/dvr/profiles');
      setQualityProfiles(data);
    } catch (err: any) {
      console.error('Failed to load quality profiles:', err);
    }
  };

  // Load user's quality profiles (for TRaSH-style scoring)
  const loadUserQualityProfiles = async () => {
    try {
      const { data } = await apiClient.get<QualityProfile[]>('/qualityprofile');
      setUserQualityProfiles(data);
      // Auto-select the first/default profile for scoring
      if (data.length > 0) {
        const defaultProfile = data.find(p => p.isDefault) || data[0];
        setSelectedQualityProfileId(defaultProfile.id);
      }
    } catch (err: any) {
      console.error('Failed to load user quality profiles:', err);
    }
  };

  const loadHardwareAcceleration = async () => {
    try {
      const { data } = await apiClient.get<HardwareAccelerationInfo[]>('/dvr/hardware-acceleration');
      setAvailableHwAccel(data);
    } catch (err: any) {
      console.error('Failed to load hardware acceleration info:', err);
    }
  };

  // Calculate video bitrate from GB per hour
  // Formula: GB/hour * 1024 MB * 8 bits / 3600 seconds = kbps
  const gbPerHourToKbps = (gb: number): number => {
    return Math.round((gb * 1024 * 8 * 1000) / 3600);
  };

  // Calculate GB per hour from video bitrate
  const kbpsToGbPerHour = (kbps: number): number => {
    return (kbps * 3600) / (1024 * 8 * 1000);
  };

  // Load score preview for current profile settings
  const loadScorePreview = async (profile: DvrQualityProfile, qualityProfileId?: number | null) => {
    try {
      setIsLoadingScorePreview(true);
      // Pass the quality profile ID for accurate TRaSH-style scoring
      const profileIdToUse = qualityProfileId ?? selectedQualityProfileId;
      const url = profileIdToUse
        ? `/dvr/profiles/calculate-scores?qualityProfileId=${profileIdToUse}`
        : '/dvr/profiles/calculate-scores';
      const { data } = await apiClient.post<DvrQualityScorePreview>(url, profile);
      setScorePreview(data);
    } catch (err: any) {
      console.error('Failed to load score preview:', err);
      setScorePreview(null);
    } finally {
      setIsLoadingScorePreview(false);
    }
  };

  // Handle profile changes and update preview
  const handleProfileChange = (field: keyof DvrQualityProfile, value: any) => {
    if (!editingProfile) return;

    const updated = { ...editingProfile, [field]: value };

    // Recalculate estimated size when bitrate changes
    if (field === 'videoBitrate' || field === 'audioBitrate') {
      const totalBitrate = (updated.videoBitrate || 0) + (updated.audioBitrate || 0);
      updated.estimatedSizePerHourMb = Math.round((totalBitrate * 3600) / (8 * 1000));
    }

    setEditingProfile(updated);

    // Update GB per hour display
    if (field === 'videoBitrate') {
      setGbPerHour(parseFloat(kbpsToGbPerHour(value).toFixed(1)));
    }

    // Debounce score preview update
    loadScorePreview(updated);
  };

  // Handle GB per hour slider change
  const handleGbPerHourChange = (value: number) => {
    setGbPerHour(value);
    const videoBitrate = gbPerHourToKbps(value);
    handleProfileChange('videoBitrate', videoBitrate);
  };

  // Start editing a profile
  const startEditingProfile = (profile: DvrQualityProfile) => {
    setEditingProfile({ ...profile });
    setGbPerHour(parseFloat(kbpsToGbPerHour(profile.videoBitrate || 8000).toFixed(1)));
    loadScorePreview(profile);
  };

  // Save edited profile
  const saveEditedProfile = async () => {
    if (!editingProfile) return;

    try {
      if (editingProfile.id) {
        await apiClient.put(`/dvr/profiles/${editingProfile.id}`, editingProfile);
        toast.success('Profile Updated');
      } else {
        await apiClient.post('/dvr/profiles', editingProfile);
        toast.success('Profile Created');
      }
      await loadQualityProfiles();
      setEditingProfile(null);
      setScorePreview(null);
    } catch (err: any) {
      toast.error('Failed to save profile', { description: err.message });
    }
  };

  const handleSettingsChange = (field: keyof DvrSettings, value: any) => {
    setDvrSettings(prev => {
      const updated = { ...prev, [field]: value };
      // Check if settings have changed from original
      setSettingsHasChanges(JSON.stringify(updated) !== JSON.stringify(originalSettings));
      return updated;
    });
  };

  const handleSaveSettings = async () => {
    try {
      setIsSavingSettings(true);
      await apiClient.put('/dvr/settings', dvrSettings);
      setOriginalSettings(dvrSettings);
      setSettingsHasChanges(false);
      toast.success('DVR Settings Saved', { description: 'Your DVR settings have been saved' });
    } catch (err: any) {
      toast.error('Failed to save settings', { description: err.message });
    } finally {
      setIsSavingSettings(false);
    }
  };

  const handleResetSettings = () => {
    setDvrSettings(originalSettings);
    setSettingsHasChanges(false);
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

  const handleBulkDelete = async () => {
    const ids = Array.from(selectedIds);
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
      setRecordings(prev => prev.filter(r => !selectedIds.has(r.id)));
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
    const deletableRecordings = recordings.filter(
      r => r.status === 'Scheduled' || r.status === 'Completed' || r.status === 'Failed' || r.status === 'Cancelled' || r.status === 'Imported'
    );
    if (selectedIds.size === deletableRecordings.length && deletableRecordings.length > 0) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(deletableRecordings.map(r => r.id)));
    }
  };

  // Check if a recording can be selected for bulk operations
  const canSelectRecording = (recording: DvrRecording) => {
    return recording.status === 'Scheduled' || recording.status === 'Completed' || recording.status === 'Failed' || recording.status === 'Cancelled' || recording.status === 'Imported';
  };

  // Get deletable recordings count
  const deletableRecordingsCount = recordings.filter(canSelectRecording).length;

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

  return (
    <div className="pb-8">
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

        {/* DVR Settings Section */}
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
          {/* Collapsible Header */}
          <button
            onClick={() => setSettingsExpanded(!settingsExpanded)}
            className="w-full flex items-center justify-between p-6 hover:bg-gray-800/30 transition-colors"
          >
            <div className="flex items-center">
              <Cog6ToothIcon className="w-6 h-6 text-red-400 mr-3" />
              <div className="text-left">
                <h3 className="text-xl font-semibold text-white">DVR Settings</h3>
                <p className="text-sm text-gray-400">Quality profiles, hardware acceleration, and recording options</p>
              </div>
            </div>
            {settingsExpanded ? (
              <ChevronUpIcon className="w-5 h-5 text-gray-400" />
            ) : (
              <ChevronDownIcon className="w-5 h-5 text-gray-400" />
            )}
          </button>

          {/* Settings Content */}
          {settingsExpanded && (
            <div className="p-6 pt-0 border-t border-gray-800">
              {/* Quality Profile Selection */}
              <div className="mb-8">
                <h4 className="text-lg font-semibold text-white mb-4 flex items-center">
                  <FilmIcon className="w-5 h-5 mr-2 text-purple-400" />
                  Recording Quality
                </h4>

                {/* Info about IPTV quality */}
                <div className="mb-4 p-3 bg-blue-950/30 border border-blue-900/50 rounded-lg">
                  <div className="flex items-start gap-2">
                    <InformationCircleIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
                    <div className="text-sm text-gray-300">
                      <p className="font-medium text-blue-400 mb-1">IPTV recordings are always HDTV-1080p quality</p>
                      <p className="text-gray-400">The video quality is locked to what your IPTV source provides (typically 1080p HDTV). You can control the file size by adjusting the bitrate below.</p>
                    </div>
                  </div>
                </div>

                {/* Profile Selection Cards */}
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
                  {qualityProfiles.map((profile) => (
                    <div
                      key={profile.id}
                      className={`p-4 rounded-lg border transition-all ${
                        dvrSettings.defaultProfileId === profile.id
                          ? 'border-red-600 bg-red-900/20'
                          : 'border-gray-700 bg-gray-800/50 hover:border-gray-600'
                      }`}
                    >
                      <div className="flex items-center justify-between mb-2">
                        <button
                          onClick={() => handleSettingsChange('defaultProfileId', profile.id)}
                          className="font-medium text-white hover:text-red-400 transition-colors"
                        >
                          {profile.name}
                        </button>
                        <button
                          onClick={() => startEditingProfile(profile)}
                          className="p-1 text-gray-500 hover:text-white transition-colors"
                          title="Edit profile"
                        >
                          <AdjustmentsHorizontalIcon className="w-4 h-4" />
                        </button>
                      </div>
                      <div className="text-xs text-gray-400 mb-2">{profile.expectedFormatDescription}</div>
                      <div className="flex items-center justify-between text-xs">
                        <span className="text-gray-500">{profile.expectedQualityName}</span>
                        {profile.estimatedSizePerHourMb > 0 && (
                          <span className="text-gray-500">~{(profile.estimatedSizePerHourMb / 1024).toFixed(1)} GB/hr</span>
                        )}
                      </div>
                      {/* Show scores */}
                      <div className="mt-2 pt-2 border-t border-gray-700/50 flex items-center gap-2">
                        <span className="text-xs text-gray-500">Score:</span>
                        <span className={`text-xs font-medium ${(profile.estimatedQualityScore + profile.estimatedCustomFormatScore) >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                          {profile.estimatedQualityScore + profile.estimatedCustomFormatScore}
                        </span>
                        <span className="text-xs text-gray-600">
                          (Q:{profile.estimatedQualityScore} CF:{profile.estimatedCustomFormatScore >= 0 ? '+' : ''}{profile.estimatedCustomFormatScore})
                        </span>
                      </div>
                    </div>
                  ))}
                </div>

                {/* Profile Editor Modal */}
                {editingProfile && (
                  <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
                    <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
                      <div className="flex items-center justify-between mb-6">
                        <h3 className="text-xl font-bold text-white">Edit Recording Profile: {editingProfile.name}</h3>
                        <button
                          onClick={() => { setEditingProfile(null); setScorePreview(null); }}
                          className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                        >
                          <XMarkIcon className="w-5 h-5" />
                        </button>
                      </div>

                      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                        {/* Left Column - Settings */}
                        <div className="space-y-6">
                          {/* GB Per Hour Slider */}
                          <div>
                            <label className="block text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                              <FolderIcon className="w-4 h-4 text-yellow-400" />
                              File Size: {gbPerHour.toFixed(1)} GB per hour
                            </label>
                            <input
                              type="range"
                              min="0.5"
                              max="50"
                              step="0.5"
                              value={gbPerHour}
                              onChange={(e) => handleGbPerHourChange(parseFloat(e.target.value))}
                              className="w-full h-2 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-red-600"
                            />
                            <div className="flex justify-between text-xs text-gray-500 mt-1">
                              <span>0.5 GB/hr</span>
                              <span>50 GB/hr</span>
                            </div>
                            <p className="text-xs text-gray-500 mt-2">
                              Video Bitrate: {(editingProfile.videoBitrate / 1000).toFixed(1)} Mbps
                            </p>
                          </div>

                          {/* Audio Settings */}
                          <div>
                            <label className="block text-sm font-medium text-gray-300 mb-3 flex items-center gap-2">
                              <SpeakerWaveIcon className="w-4 h-4 text-green-400" />
                              Audio Settings
                            </label>
                            <div className="grid grid-cols-2 gap-4">
                              <div>
                                <label className="block text-xs text-gray-400 mb-1">Audio Codec</label>
                                <select
                                  value={editingProfile.audioCodec}
                                  onChange={(e) => handleProfileChange('audioCodec', e.target.value)}
                                  className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                                >
                                  <option value="copy">Original (Copy)</option>
                                  <option value="aac">AAC</option>
                                  <option value="ac3">Dolby Digital (AC3)</option>
                                  <option value="eac3">Dolby Digital+ (E-AC3)</option>
                                </select>
                              </div>
                              <div>
                                <label className="block text-xs text-gray-400 mb-1">Audio Channels</label>
                                <select
                                  value={editingProfile.audioChannels || 'original'}
                                  onChange={(e) => handleProfileChange('audioChannels', e.target.value)}
                                  className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                                >
                                  <option value="original">Original</option>
                                  <option value="stereo">Stereo (2.0)</option>
                                  <option value="5.1">Surround (5.1)</option>
                                </select>
                              </div>
                            </div>
                            <div className="mt-3">
                              <label className="block text-xs text-gray-400 mb-1">Audio Bitrate (kbps)</label>
                              <input
                                type="number"
                                value={editingProfile.audioBitrate}
                                onChange={(e) => handleProfileChange('audioBitrate', parseInt(e.target.value) || 0)}
                                min="64"
                                max="640"
                                step="32"
                                className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                              />
                              <p className="text-xs text-gray-500 mt-1">128-192 kbps for stereo, 384-640 kbps for 5.1</p>
                            </div>
                          </div>

                          {/* Video Codec */}
                          <div>
                            <label className="block text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                              <VideoCameraIcon className="w-4 h-4 text-purple-400" />
                              Video Codec
                            </label>
                            <select
                              value={editingProfile.videoCodec}
                              onChange={(e) => handleProfileChange('videoCodec', e.target.value)}
                              className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                            >
                              <optgroup label="Recommended">
                                <option value="copy">Original (Copy) - No transcoding</option>
                              </optgroup>
                              <optgroup label="H.264/AVC (Most Compatible)">
                                <option value="h264">H.264 (x264) - Software</option>
                                <option value="h264_nvenc">H.264 (NVENC) - NVIDIA GPU</option>
                                <option value="h264_qsv">H.264 (QuickSync) - Intel GPU</option>
                                <option value="h264_amf">H.264 (AMF) - AMD GPU</option>
                              </optgroup>
                              <optgroup label="H.265/HEVC (Better Compression)">
                                <option value="hevc">H.265/HEVC (x265) - Software</option>
                                <option value="hevc_nvenc">H.265/HEVC (NVENC) - NVIDIA GPU</option>
                                <option value="hevc_qsv">H.265/HEVC (QuickSync) - Intel GPU</option>
                                <option value="hevc_amf">H.265/HEVC (AMF) - AMD GPU</option>
                              </optgroup>
                              <optgroup label="Next-Gen Codecs">
                                <option value="av1">AV1 (SVT-AV1) - Best compression, slow</option>
                                <option value="av1_nvenc">AV1 (NVENC) - RTX 40 series+</option>
                                <option value="av1_qsv">AV1 (QuickSync) - Intel Arc+</option>
                                <option value="vvc">H.266/VVC - Experimental</option>
                              </optgroup>
                              <optgroup label="Other">
                                <option value="vp9">VP9 - Google/YouTube codec</option>
                                <option value="mpeg2">MPEG-2 - Legacy compatibility</option>
                              </optgroup>
                            </select>
                            <p className="text-xs text-gray-500 mt-1">
                              "Original" preserves source quality. GPU encoders are faster but may have slightly lower quality.
                            </p>
                          </div>

                          {/* Container Format */}
                          <div>
                            <label className="block text-sm font-medium text-gray-300 mb-2">Container Format</label>
                            <select
                              value={editingProfile.container}
                              onChange={(e) => handleProfileChange('container', e.target.value)}
                              className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                            >
                              <optgroup label="Recommended">
                                <option value="mkv">Matroska (.mkv) - Best features, all codecs</option>
                                <option value="mp4">MP4 (.mp4) - Best compatibility</option>
                              </optgroup>
                              <optgroup label="Streaming">
                                <option value="ts">MPEG-TS (.ts) - Live stream native</option>
                                <option value="m2ts">M2TS (.m2ts) - Blu-ray format</option>
                              </optgroup>
                              <optgroup label="Other">
                                <option value="avi">AVI (.avi) - Legacy format</option>
                                <option value="webm">WebM (.webm) - Web optimized (VP9/AV1)</option>
                                <option value="mov">QuickTime (.mov) - Apple devices</option>
                              </optgroup>
                            </select>
                            <p className="text-xs text-gray-500 mt-1">
                              MKV supports all codecs and features. MP4 has best device compatibility. TS is native for IPTV.
                            </p>
                          </div>
                        </div>

                        {/* Right Column - Score Preview */}
                        <div className="bg-gray-800/50 rounded-lg p-4 border border-gray-700">
                          <h4 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
                            <SparklesIcon className="w-5 h-5 text-yellow-400" />
                            Predicted Format Scores
                          </h4>

                          {/* Quality Profile Selector for Scoring */}
                          <div className="mb-4 p-3 bg-gray-900/50 rounded-lg border border-gray-700">
                            <label className="flex items-center gap-2 text-sm text-gray-400 mb-2">
                              <ChartBarIcon className="w-4 h-4" />
                              Quality Profile for Scoring
                            </label>
                            <select
                              value={selectedQualityProfileId || ''}
                              onChange={(e) => {
                                const newId = e.target.value ? parseInt(e.target.value) : null;
                                setSelectedQualityProfileId(newId);
                                if (editingProfile) {
                                  loadScorePreview(editingProfile, newId);
                                }
                              }}
                              className="w-full px-3 py-2 bg-gray-800 border border-gray-600 rounded text-white text-sm focus:outline-none focus:border-red-500"
                            >
                              <option value="">No profile (fallback scoring)</option>
                              {userQualityProfiles.map((profile) => (
                                <option key={profile.id} value={profile.id}>
                                  {profile.name} {profile.isDefault ? '(Default)' : ''}
                                </option>
                              ))}
                            </select>
                            <p className="text-xs text-gray-500 mt-1">
                              Select your quality profile to calculate scores matching TRaSH Guides
                            </p>
                          </div>

                          {isLoadingScorePreview ? (
                            <div className="flex items-center justify-center py-8">
                              <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-red-600"></div>
                            </div>
                          ) : scorePreview ? (
                            <div className="space-y-4">
                              {/* Quality Name */}
                              <div className="p-3 bg-gray-900/50 rounded-lg">
                                <div className="text-sm text-gray-400 mb-1">Expected Quality</div>
                                <div className="text-lg font-medium text-white">{scorePreview.qualityName}</div>
                                <div className="text-xs text-gray-500 mt-1">{scorePreview.formatDescription}</div>
                              </div>

                              {/* Scores */}
                              <div className="grid grid-cols-3 gap-3">
                                <div className="p-3 bg-gray-900/50 rounded-lg text-center">
                                  <div className="text-xs text-gray-400 mb-1">Quality</div>
                                  <div className="text-xl font-bold text-blue-400">{scorePreview.qualityScore}</div>
                                </div>
                                <div className="p-3 bg-gray-900/50 rounded-lg text-center">
                                  <div className="text-xs text-gray-400 mb-1">Custom Format</div>
                                  <div className={`text-xl font-bold ${scorePreview.customFormatScore >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                                    {scorePreview.customFormatScore >= 0 ? '+' : ''}{scorePreview.customFormatScore}
                                  </div>
                                </div>
                                <div className="p-3 bg-gray-900/50 rounded-lg text-center">
                                  <div className="text-xs text-gray-400 mb-1">Total</div>
                                  <div className={`text-xl font-bold ${scorePreview.totalScore >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                                    {scorePreview.totalScore}
                                  </div>
                                </div>
                              </div>

                              {/* Matched Custom Formats */}
                              {scorePreview.matchedFormats && scorePreview.matchedFormats.length > 0 && (
                                <div>
                                  <div className="text-sm text-gray-400 mb-2">Matched Custom Formats</div>
                                  <div className="flex flex-wrap gap-2">
                                    {scorePreview.matchedFormats.map((format, idx) => (
                                      <span
                                        key={idx}
                                        className={`px-2 py-1 text-xs rounded ${
                                          format.includes('+') ? 'bg-green-900/30 text-green-400' :
                                          format.includes('-') ? 'bg-red-900/30 text-red-400' :
                                          'bg-gray-700 text-gray-300'
                                        }`}
                                      >
                                        {format}
                                      </span>
                                    ))}
                                  </div>
                                </div>
                              )}

                              {/* Synthetic Title (for debugging/verification) */}
                              <div className="pt-3 border-t border-gray-700">
                                <div className="text-xs text-gray-500 mb-1">Scene Title Preview (for scoring)</div>
                                <code className="text-xs text-gray-400 bg-black/50 px-2 py-1 rounded block overflow-x-auto">
                                  {scorePreview.syntheticTitle}
                                </code>
                              </div>

                              {/* TRaSH Guides Note */}
                              <div className="text-xs text-gray-500 pt-2 border-t border-gray-700">
                                {selectedQualityProfileId
                                  ? 'Scores are calculated using your selected quality profile and custom formats, matching TRaSH Guides scoring.'
                                  : 'Select a quality profile above to get accurate TRaSH Guides scoring. Currently using fallback scoring.'}
                              </div>
                            </div>
                          ) : (
                            <div className="text-center py-8 text-gray-500">
                              <p>Unable to calculate scores</p>
                              <p className="text-xs mt-1">Make sure you have a quality profile configured</p>
                            </div>
                          )}
                        </div>
                      </div>

                      {/* Save/Cancel Buttons */}
                      <div className="flex items-center justify-end gap-3 mt-6 pt-4 border-t border-gray-800">
                        <button
                          onClick={() => { setEditingProfile(null); setScorePreview(null); }}
                          className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                        >
                          Cancel
                        </button>
                        <button
                          onClick={saveEditedProfile}
                          className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                        >
                          Save Profile
                        </button>
                      </div>
                    </div>
                  </div>
                )}
              </div>

              {/* Hardware Acceleration */}
              <div className="mb-8">
                <h4 className="text-lg font-semibold text-white mb-4 flex items-center">
                  <CpuChipIcon className="w-5 h-5 mr-2 text-blue-400" />
                  Hardware Acceleration
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Encoding Method</label>
                    <select
                      value={dvrSettings.hardwareAcceleration}
                      onChange={(e) => handleSettingsChange('hardwareAcceleration', parseInt(e.target.value))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    >
                      {HardwareAccelerationOptions.map((opt) => {
                        const hwInfo = availableHwAccel.find(h => h.type === opt.value);
                        const isDetected = opt.value === 0 || opt.value === 99 || hwInfo?.isAvailable;
                        return (
                          <option key={opt.value} value={opt.value}>
                            {opt.label} {!isDetected && availableHwAccel.length > 0 && '(Not Detected)'}
                          </option>
                        );
                      })}
                    </select>
                    <p className="text-xs text-gray-500 mt-1">
                      {HardwareAccelerationOptions.find(o => o.value === dvrSettings.hardwareAcceleration)?.description}
                    </p>
                    {(() => {
                      const selected = dvrSettings.hardwareAcceleration;
                      const hwInfo = availableHwAccel.find(h => h.type === selected);
                      const isDetected = selected === 0 || selected === 99 || hwInfo?.isAvailable;
                      if (!isDetected && selected !== 0 && selected !== 99 && availableHwAccel.length > 0) {
                        return (
                          <p className="text-xs text-yellow-500 mt-1">
                            This encoder was not detected on the current system. It may still work if available in your Docker container or runtime environment.
                          </p>
                        );
                      }
                      return null;
                    })()}
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">FFmpeg Path (Optional)</label>
                    <input
                      type="text"
                      value={dvrSettings.ffmpegPath}
                      onChange={(e) => handleSettingsChange('ffmpegPath', e.target.value)}
                      placeholder="Leave empty to use system PATH"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Custom path to FFmpeg binary</p>
                  </div>
                </div>
                {/* Available Hardware Info */}
                {availableHwAccel.length > 0 && (
                  <div className="mt-4 p-3 bg-gray-800/50 rounded-lg">
                    <p className="text-sm text-gray-400 mb-2">Detected Hardware Encoders:</p>
                    <div className="flex flex-wrap gap-2">
                      {availableHwAccel.filter(h => h.isAvailable).map((hw) => (
                        <span key={hw.type} className="px-2 py-1 bg-green-900/30 text-green-400 text-xs rounded">
                          {hw.name}
                        </span>
                      ))}
                      {availableHwAccel.filter(h => h.isAvailable).length === 0 && (
                        <span className="text-gray-500 text-xs">No hardware encoders detected - using software encoding</span>
                      )}
                    </div>
                  </div>
                )}
              </div>

              {/* Recording Path and Naming */}
              <div className="mb-8">
                <h4 className="text-lg font-semibold text-white mb-4 flex items-center">
                  <FolderIcon className="w-5 h-5 mr-2 text-yellow-400" />
                  Storage Settings
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Recording Path</label>
                    <input
                      type="text"
                      value={dvrSettings.recordingPath}
                      onChange={(e) => handleSettingsChange('recordingPath', e.target.value)}
                      placeholder="Leave empty to use root folder"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Where to save recordings (empty = root folder)</p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">File Naming Pattern</label>
                    <input
                      type="text"
                      value={dvrSettings.fileNamingPattern}
                      onChange={(e) => handleSettingsChange('fileNamingPattern', e.target.value)}
                      placeholder="{Title} - {Date}"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Available: {'{Title}'}, {'{Date}'}, {'{League}'}, {'{Channel}'}</p>
                  </div>
                </div>
              </div>

              {/* Padding Settings */}
              <div className="mb-8">
                <h4 className="text-lg font-semibold text-white mb-4 flex items-center">
                  <ClockIcon className="w-5 h-5 mr-2 text-green-400" />
                  Recording Padding
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Pre-Padding (minutes)</label>
                    <input
                      type="number"
                      value={dvrSettings.prePaddingMinutes}
                      onChange={(e) => handleSettingsChange('prePaddingMinutes', parseInt(e.target.value) || 0)}
                      min="0"
                      max="60"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Start recording before scheduled time</p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Post-Padding (minutes)</label>
                    <input
                      type="number"
                      value={dvrSettings.postPaddingMinutes}
                      onChange={(e) => handleSettingsChange('postPaddingMinutes', parseInt(e.target.value) || 0)}
                      min="0"
                      max="180"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">Continue recording after scheduled end (for overtime)</p>
                  </div>
                </div>
              </div>

              {/* Advanced Settings */}
              <div className="mb-8">
                <h4 className="text-lg font-semibold text-white mb-4">Advanced Settings</h4>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Max Concurrent Recordings</label>
                    <input
                      type="number"
                      value={dvrSettings.maxConcurrentRecordings}
                      onChange={(e) => handleSettingsChange('maxConcurrentRecordings', parseInt(e.target.value) || 0)}
                      min="0"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">0 = unlimited</p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Recording Retention (days)</label>
                    <input
                      type="number"
                      value={dvrSettings.recordingRetentionDays}
                      onChange={(e) => handleSettingsChange('recordingRetentionDays', parseInt(e.target.value) || 0)}
                      min="0"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">0 = keep forever</p>
                  </div>
                  <div className="flex items-center">
                    <label className="flex items-center cursor-pointer">
                      <input
                        type="checkbox"
                        checked={dvrSettings.deleteAfterImport}
                        onChange={(e) => handleSettingsChange('deleteAfterImport', e.target.checked)}
                        className="w-4 h-4 text-red-600 bg-gray-800 border-gray-700 rounded focus:ring-red-500"
                      />
                      <span className="ml-2 text-sm text-gray-300">Delete after import</span>
                    </label>
                  </div>
                </div>
              </div>

              {/* Reconnection Settings */}
              <div className="mb-6">
                <h4 className="text-lg font-semibold text-white mb-4">Stream Reconnection</h4>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div className="flex items-center">
                    <label className="flex items-center cursor-pointer">
                      <input
                        type="checkbox"
                        checked={dvrSettings.enableReconnect}
                        onChange={(e) => handleSettingsChange('enableReconnect', e.target.checked)}
                        className="w-4 h-4 text-red-600 bg-gray-800 border-gray-700 rounded focus:ring-red-500"
                      />
                      <span className="ml-2 text-sm text-gray-300">Enable auto-reconnect</span>
                    </label>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Max Reconnect Attempts</label>
                    <input
                      type="number"
                      value={dvrSettings.maxReconnectAttempts}
                      onChange={(e) => handleSettingsChange('maxReconnectAttempts', parseInt(e.target.value) || 1)}
                      min="1"
                      max="20"
                      disabled={!dvrSettings.enableReconnect}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 disabled:opacity-50"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Reconnect Delay (seconds)</label>
                    <input
                      type="number"
                      value={dvrSettings.reconnectDelaySeconds}
                      onChange={(e) => handleSettingsChange('reconnectDelaySeconds', parseInt(e.target.value) || 1)}
                      min="1"
                      max="60"
                      disabled={!dvrSettings.enableReconnect}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 disabled:opacity-50"
                    />
                  </div>
                </div>
              </div>

              {/* Save/Reset Buttons */}
              {settingsHasChanges && (
                <div className="flex items-center justify-end space-x-3 pt-4 border-t border-gray-800">
                  <button
                    onClick={handleResetSettings}
                    className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                  >
                    Reset
                  </button>
                  <button
                    onClick={handleSaveSettings}
                    disabled={isSavingSettings}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors disabled:opacity-50"
                  >
                    {isSavingSettings ? 'Saving...' : 'Save Settings'}
                  </button>
                </div>
              )}
            </div>
          )}
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
                Manual Recording
              </button>
            </div>
          </div>

          {/* Bulk Selection Controls */}
          {deletableRecordingsCount > 0 && (
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

          <div className="space-y-3">
            {recordings.map((recording) => (
              <div
                key={recording.id}
                className={`group bg-black/30 border rounded-lg p-4 transition-all ${
                  selectedIds.has(recording.id) ? 'border-red-600 bg-red-950/20' : 'border-gray-800 hover:border-red-900/50'
                }`}
              >
                <div className="flex items-start justify-between">
                  <div className="flex items-start space-x-4 flex-1">
                    {/* Selection Checkbox */}
                    {canSelectRecording(recording) && (
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
                  onClick={() => {
                    setShowScheduleModal(false);
                    setFormData(defaultFormData);
                    setChannelSearch('');
                    setShowChannelDropdown(false);
                  }}
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
                  onClick={() => {
                    setShowScheduleModal(false);
                    setFormData(defaultFormData);
                    setChannelSearch('');
                    setShowChannelDropdown(false);
                  }}
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
