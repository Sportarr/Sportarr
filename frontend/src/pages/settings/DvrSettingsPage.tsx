import { useState, useEffect } from 'react';
import {
  FilmIcon,
  ChartBarIcon,
  VideoCameraIcon,
  FolderIcon,
  SpeakerWaveIcon,
  SparklesIcon,
  CpuChipIcon,
  DocumentTextIcon,
  CloudArrowDownIcon,
  InformationCircleIcon,
  ClockIcon,
  ExclamationTriangleIcon,
} from '@heroicons/react/24/outline';
import { Link } from 'react-router-dom';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import { apiGet } from '../../utils/api';
import type { QualityProfile } from '../../types';
import SettingsHeader from '../../components/SettingsHeader';

// Naming preset types (same as MediaManagementSettings)
interface NamingPreset {
  format: string;
  description: string;
  supportsMultiPart: boolean;
}

interface NamingPresets {
  file: Record<string, NamingPreset>;
  folder: Record<string, { format: string; description: string }>;
}

// DVR Settings Types
interface DvrSettings {
  defaultProfileId: number;
  recordingPath: string;
  fileNamingPattern: string;
  prePaddingMinutes: number;
  postPaddingMinutes: number;
  maxConcurrentRecordings: number;
  simultaneousChannels: number;
  // What to do when a new recording would push an IPTV source past
  // its MaxStreams cap or push past maxConcurrentRecordings.
  // "Refuse" rejects with 409. "Queue" lets the row sit Scheduled
  // past its start time until a slot frees. "Preempt" cancels the
  // lowest-priority overlapping Scheduled row to make room
  // (active Recording rows are never preempted).
  conflictPolicy: string;
  deleteAfterImport: boolean;
  recordingRetentionDays: number;
  keepLastRecordingsPerLeague: number;
  hardwareAcceleration: number;
  ffmpegPath: string;
  postRecordingCommand: string;
  overtimeGuardEnabled: boolean;
  overtimeMaxExtensionMinutes: number;
  enableReconnect: boolean;
  maxReconnectAttempts: number;
  reconnectDelaySeconds: number;
  // Catchup: download finished events from the provider's timeshift
  // archive (channels with tv_archive) instead of recording live.
  useCatchupWhenAvailable: boolean;
  catchupReadyGraceMinutes: number;
  catchupTimeshiftMode: string;
  catchupBackfillHours: number;
  // Encoding settings (stored directly in config)
  videoCodec: string;
  audioCodec: string;
  audioChannels: string;
  audioBitrate: number;
  videoBitrate: number;
  container: string;
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

// Hardware acceleration enum values with Docker requirements (matching Tdarr's approach)
const HardwareAccelerationOptions: { value: number; label: string; description: string; dockerRequirements?: string }[] = [
  { value: 0, label: 'None', description: 'Software encoding only (CPU)' },
  {
    value: 1,
    label: 'NVENC (NVIDIA)',
    description: 'NVIDIA GPU hardware encoding',
    dockerRequirements: 'Docker: --gpus=all, Environment: NVIDIA_DRIVER_CAPABILITIES=all, NVIDIA_VISIBLE_DEVICES=all'
  },
  {
    value: 2,
    label: 'QuickSync (Intel)',
    description: 'Intel GPU hardware encoding',
    dockerRequirements: 'Docker: devices: /dev/dri:/dev/dri (Intel 8th gen+ recommended)'
  },
  {
    value: 3,
    label: 'AMF (AMD)',
    description: 'AMD GPU hardware encoding',
    dockerRequirements: 'Docker: devices: /dev/dri:/dev/dri, /dev/kfd:/dev/kfd (Windows: use d3d11va)'
  },
  {
    value: 4,
    label: 'VAAPI (Linux)',
    description: 'Linux hardware encoding (Intel/AMD)',
    dockerRequirements: 'Docker: devices: /dev/dri:/dev/dri (requires mesa-va-drivers)'
  },
  {
    value: 5,
    label: 'VideoToolbox (macOS)',
    description: 'macOS hardware encoding',
    dockerRequirements: 'Native macOS only (not available in Docker)'
  },
  { value: 99, label: 'Auto-detect', description: 'Automatically detect best available encoder' },
];

const defaultDvrSettings: DvrSettings = {
  defaultProfileId: 1,
  recordingPath: '',
  fileNamingPattern: '{Title} - {Date}',
  prePaddingMinutes: 5,
  postPaddingMinutes: 30,
  maxConcurrentRecordings: 0,
  simultaneousChannels: 1,
  conflictPolicy: 'Refuse',
  deleteAfterImport: false,
  recordingRetentionDays: 0,
  keepLastRecordingsPerLeague: 0,
  hardwareAcceleration: 99,
  ffmpegPath: '',
  postRecordingCommand: '',
  overtimeGuardEnabled: true,
  overtimeMaxExtensionMinutes: 120,
  enableReconnect: true,
  maxReconnectAttempts: 5,
  reconnectDelaySeconds: 5,
  // Catchup
  useCatchupWhenAvailable: true,
  catchupReadyGraceMinutes: 15,
  catchupTimeshiftMode: 'auto',
  catchupBackfillHours: 48,
  // Encoding settings
  videoCodec: 'copy',
  audioCodec: 'copy',
  audioChannels: 'original',
  audioBitrate: 192,
  videoBitrate: 0,
  container: 'mp4',
};

export default function DvrSettingsPage() {
  // State
  // FFmpeg state
  const [ffmpegAvailable, setFfmpegAvailable] = useState<boolean | null>(null);

  // DVR Settings state
  const [dvrSettings, setDvrSettings] = useState<DvrSettings>(defaultDvrSettings);
  const [availableHwAccel, setAvailableHwAccel] = useState<HardwareAccelerationInfo[]>([]);
  const [isSavingSettings, setIsSavingSettings] = useState(false);
  const [settingsHasChanges, setSettingsHasChanges] = useState(false);
  const [originalSettings, setOriginalSettings] = useState<DvrSettings>(defaultDvrSettings);

  // Score preview state for encoding settings
  const [scorePreview, setScorePreview] = useState<DvrQualityScorePreview | null>(null);
  const [isLoadingScorePreview, setIsLoadingScorePreview] = useState(false);
  const [gbPerHour, setGbPerHour] = useState(4); // Default 4 GB/hour

  // User's quality profiles (for TRaSH-style scoring)
  const [userQualityProfiles, setUserQualityProfiles] = useState<QualityProfile[]>([]);
  const [selectedQualityProfileId, setSelectedQualityProfileId] = useState<number | null>(null);


  // Naming presets state (TRaSH Guides naming conventions)
  const [namingPresets, setNamingPresets] = useState<NamingPresets | null>(null);
  const [selectedNamingPreset, setSelectedNamingPreset] = useState<string>('');

  // Current encoding settings (directly editable, not tied to profile cards)
  const [currentEncodingSettings, setCurrentEncodingSettings] = useState({
    videoCodec: 'copy',
    audioCodec: 'copy',
    audioChannels: 'original',
    audioBitrate: 192,
    videoBitrate: 8000,
    container: 'mkv',
  });

  // Load settings on mount
  useEffect(() => {
    checkFfmpeg();
    loadDvrSettings();
    loadHardwareAcceleration();
    loadUserQualityProfiles();
    loadNamingPresets();
  }, []);

  // Load score preview when quality profile is selected (including on initial load)
  useEffect(() => {
    if (selectedQualityProfileId) {
      loadScorePreviewForSettings(selectedQualityProfileId, currentEncodingSettings);
    }
  }, [selectedQualityProfileId]);

  const checkFfmpeg = async () => {
    try {
      // The backend route is /api/dvr/ffmpeg/status. This used to call a
      // nonexistent /dvr/ffmpeg/check, which the SPA fallback answered
      // with HTML, so the UI reported FFmpeg missing on every install
      // regardless of reality.
      const { data } = await apiClient.get<{ available: boolean; version?: string; path?: string }>('/dvr/ffmpeg/status');
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
      // Sync encoding settings from config to the inline editor
      setCurrentEncodingSettings({
        videoCodec: data.videoCodec || 'copy',
        audioCodec: data.audioCodec || 'copy',
        audioChannels: data.audioChannels || 'original',
        audioBitrate: data.audioBitrate || 192,
        videoBitrate: data.videoBitrate || 0,
        container: data.container || 'mp4',
      });
      // Also update GB per hour slider based on video bitrate
      if (data.videoBitrate > 0) {
        setGbPerHour(kbpsToGbPerHour(data.videoBitrate));
      }
    } catch (err: any) {
      console.error('Failed to load DVR settings:', err);
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

  // Load TRaSH Guides naming presets
  const loadNamingPresets = async () => {
    try {
      // Note: enableMultiPartEpisodes is typically true for DVR (fighting sports have multiple parts)
      const response = await apiGet('/api/trash/naming-presets?enableMultiPartEpisodes=true');
      if (response.ok) {
        const data = await response.json();
        setNamingPresets(data);
      }
    } catch (error) {
      console.error('Failed to load naming presets:', error);
    }
  };

  // Handle applying a naming preset
  const handleApplyNamingPreset = (presetKey: string) => {
    if (!namingPresets?.file?.[presetKey]) return;
    const preset = namingPresets.file[presetKey];
    handleSettingsChange('fileNamingPattern', preset.format);
    setSelectedNamingPreset(presetKey);
    toast.success('Naming preset applied', {
      description: preset.description,
    });
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


  // Handle encoding setting change (for inline settings)
  const handleEncodingSettingChange = (field: string, value: any) => {
    const updated = { ...currentEncodingSettings, [field]: value };
    setCurrentEncodingSettings(updated);
    // Also update dvrSettings so it gets saved
    setDvrSettings(prev => ({ ...prev, [field]: value }));
    setSettingsHasChanges(true);
    // Update score preview
    loadScorePreviewForSettings(selectedQualityProfileId, updated);
  };

  // Handle GB per hour slider change for inline settings
  const handleGbPerHourChangeForSettings = (value: number) => {
    setGbPerHour(value);
    const videoBitrate = gbPerHourToKbps(value);
    const updated = { ...currentEncodingSettings, videoBitrate };
    setCurrentEncodingSettings(updated);
    // Also update dvrSettings so it gets saved
    setDvrSettings(prev => ({ ...prev, videoBitrate }));
    setSettingsHasChanges(true);
    loadScorePreviewForSettings(selectedQualityProfileId, updated);
  };

  // Load score preview for inline settings (not modal)
  // Note: Resolution is auto-detected from channel, so we use 1080p as default for preview
  const loadScorePreviewForSettings = async (
    qualityProfileId?: number | null,
    encodingSettings?: typeof currentEncodingSettings
  ) => {
    const profileIdToUse = qualityProfileId ?? selectedQualityProfileId;
    const settingsToUse = encodingSettings ?? currentEncodingSettings;

    if (!profileIdToUse) {
      setScorePreview(null);
      return;
    }

    try {
      setIsLoadingScorePreview(true);
      const params = new URLSearchParams();
      params.append('qualityProfileId', profileIdToUse.toString());
      // Use 1080p as default for preview - actual recordings use channel's detected quality
      params.append('sourceResolution', '1080p');

      // Build a profile-like object from current encoding settings
      const profileData: Partial<DvrQualityProfile> = {
        videoCodec: settingsToUse.videoCodec,
        audioCodec: settingsToUse.audioCodec,
        audioChannels: settingsToUse.audioChannels,
        audioBitrate: settingsToUse.audioBitrate,
        videoBitrate: settingsToUse.videoBitrate,
        container: settingsToUse.container,
      };

      const { data } = await apiClient.post<DvrQualityScorePreview>(
        `/dvr/profiles/calculate-scores?${params}`,
        profileData
      );
      setScorePreview(data);
    } catch (err: any) {
      console.error('Failed to load score preview:', err);
      setScorePreview(null);
    } finally {
      setIsLoadingScorePreview(false);
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

  return (
    <div className="pb-8">
      <SettingsHeader
        title="DVR Settings"
        subtitle="Recording quality, hardware acceleration, storage, padding, and catchup options"
        onSave={handleSaveSettings}
        isSaving={isSavingSettings}
        hasUnsavedChanges={settingsHasChanges}
        saveButtonText={isSavingSettings ? 'Saving...' : 'Save Settings'}
      >
        {settingsHasChanges && (
          <button
            onClick={handleResetSettings}
            className="inline-flex items-center gap-2 rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-200 transition-colors hover:bg-gray-700"
          >
            Reset
          </button>
        )}
        <Link
          to="/iptv/recordings"
          className="inline-flex items-center gap-2 rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-200 transition-colors hover:bg-gray-700"
        >
          <VideoCameraIcon className="h-5 w-5 text-gray-400" />
          Recordings
        </Link>
      </SettingsHeader>

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

      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
        <div className="p-6">
              {/* Recording Quality & Encoding Settings */}
              <div className="mb-8">
                <h4 className="text-lg font-semibold text-white mb-4 flex items-center">
                  <FilmIcon className="w-5 h-5 mr-2 text-purple-400" />
                  Recording Quality & Encoding
                </h4>

                {/* Quality Profile & Source Resolution */}
                <div className="mb-6 p-4 bg-gray-800/50 rounded-lg border border-gray-700">
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    {/* Quality Profile Selector */}
                    <div>
                      <label className="flex items-center gap-2 text-sm font-medium text-white mb-2">
                        <ChartBarIcon className="w-5 h-5 text-yellow-400" />
                        Quality Profile for Scoring
                      </label>
                      <select
                        value={selectedQualityProfileId || ''}
                        onChange={(e) => {
                          const newId = e.target.value ? parseInt(e.target.value) : null;
                          setSelectedQualityProfileId(newId);
                          loadScorePreviewForSettings(newId);
                        }}
                        className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-500"
                      >
                        <option value="">-- Select a Quality Profile --</option>
                        {userQualityProfiles.map((profile) => (
                          <option key={profile.id} value={profile.id}>
                            {profile.name} {profile.isDefault ? '(Default)' : ''}
                          </option>
                        ))}
                      </select>
                      <p className="text-xs text-gray-400 mt-1">
                        Scores will match your profile's custom format scores
                      </p>
                    </div>

                    {/* Source Resolution Info */}
                    <div>
                      <label className="flex items-center gap-2 text-sm font-medium text-white mb-2">
                        <VideoCameraIcon className="w-5 h-5 text-blue-400" />
                        Source Resolution
                      </label>
                      <div className="px-3 py-2 bg-gray-900/50 border border-gray-700 rounded-lg">
                        <p className="text-sm text-gray-300">Auto-detected from channel</p>
                        <p className="text-xs text-gray-500 mt-1">
                          Resolution is detected from each IPTV channel's name (4K, FHD, HD, SD) and used for quality scoring.
                        </p>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                  {/* Left Column - Encoding Settings */}
                  <div className="space-y-6">
                    {/* GB Per Hour Slider */}
                    <div>
                      <label className="text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                        <FolderIcon className="w-4 h-4 text-yellow-400" />
                        File Size: {gbPerHour.toFixed(1)} GB per hour
                      </label>
                      <input
                        type="range"
                        min="0.5"
                        max="50"
                        step="0.5"
                        value={gbPerHour}
                        onChange={(e) => handleGbPerHourChangeForSettings(parseFloat(e.target.value))}
                        className="w-full h-2 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-red-600"
                      />
                      <div className="flex justify-between text-xs text-gray-500 mt-1">
                        <span>0.5 GB/hr</span>
                        <span>50 GB/hr</span>
                      </div>
                      <p className="text-xs text-gray-500 mt-2">
                        Video Bitrate: {(currentEncodingSettings.videoBitrate / 1000).toFixed(1)} Mbps
                      </p>
                    </div>

                    {/* Video Codec */}
                    <div>
                      <label className="text-sm font-medium text-gray-300 mb-2 flex items-center gap-2">
                        <VideoCameraIcon className="w-4 h-4 text-purple-400" />
                        Video Codec
                      </label>
                      <select
                        value={currentEncodingSettings.videoCodec}
                        onChange={(e) => handleEncodingSettingChange('videoCodec', e.target.value)}
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

                    {/* Audio Settings */}
                    <div>
                      <label className="text-sm font-medium text-gray-300 mb-3 flex items-center gap-2">
                        <SpeakerWaveIcon className="w-4 h-4 text-green-400" />
                        Audio Settings
                      </label>
                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                        <div>
                          <label className="block text-xs text-gray-400 mb-1">Audio Codec</label>
                          <select
                            value={currentEncodingSettings.audioCodec}
                            onChange={(e) => handleEncodingSettingChange('audioCodec', e.target.value)}
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
                            value={currentEncodingSettings.audioChannels}
                            onChange={(e) => handleEncodingSettingChange('audioChannels', e.target.value)}
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
                          value={currentEncodingSettings.audioBitrate}
                          onChange={(e) => handleEncodingSettingChange('audioBitrate', parseInt(e.target.value) || 0)}
                          min="64"
                          max="640"
                          step="32"
                          className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                        />
                        <p className="text-xs text-gray-500 mt-1">128-192 kbps for stereo, 384-640 kbps for 5.1</p>
                      </div>
                    </div>

                    {/* Container Format */}
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Container Format</label>
                      <select
                        value={currentEncodingSettings.container}
                        onChange={(e) => handleEncodingSettingChange('container', e.target.value)}
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
                      Format Score Preview
                    </h4>

                    {!selectedQualityProfileId ? (
                      <div className="text-center py-8 text-gray-500">
                        <ChartBarIcon className="w-12 h-12 mx-auto mb-3 text-gray-600" />
                        <p className="font-medium">Select a Quality Profile</p>
                        <p className="text-xs mt-1">Choose a quality profile above to see how your encoding choices will be scored.</p>
                      </div>
                    ) : isLoadingScorePreview ? (
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

                        {/* Matched Custom Formats with Individual Scores */}
                        {scorePreview.matchedFormats && scorePreview.matchedFormats.length > 0 ? (
                          <div className="p-3 bg-gray-900/50 rounded-lg">
                            <div className="text-sm text-gray-400 mb-3">Your Encoding Choices → Profile Scores</div>
                            <div className="space-y-2">
                              {scorePreview.matchedFormats.map((format, idx) => {
                                const match = format.match(/^(.+?)\s*\(([+-]?\d+)\)$/);
                                const formatName = match ? match[1] : format;
                                const formatScore = match ? parseInt(match[2]) : 0;

                                return (
                                  <div key={idx} className="flex items-center justify-between py-1.5 border-b border-gray-700/50 last:border-0">
                                    <span className="text-sm text-gray-300">{formatName}</span>
                                    <span className={`text-sm font-mono font-medium ${
                                      formatScore > 0 ? 'text-green-400' :
                                      formatScore < 0 ? 'text-red-400' :
                                      'text-gray-400'
                                    }`}>
                                      {formatScore > 0 ? '+' : ''}{formatScore}
                                    </span>
                                  </div>
                                );
                              })}
                            </div>
                          </div>
                        ) : (
                          <div className="p-3 bg-gray-900/50 rounded-lg text-center text-gray-500 text-sm">
                            No custom formats matched your encoding settings
                          </div>
                        )}

                        {/* Total Scores Summary */}
                        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                          <div className="p-3 bg-gray-900/50 rounded-lg">
                            <div className="text-xs text-gray-400 mb-1">Quality Score</div>
                            <div className="text-lg font-bold text-blue-400">{scorePreview.qualityScore}</div>
                            <div className="text-xs text-gray-500">Based on {scorePreview.qualityName}</div>
                          </div>
                          <div className="p-3 bg-gray-900/50 rounded-lg">
                            <div className="text-xs text-gray-400 mb-1">Custom Format Score</div>
                            <div className={`text-lg font-bold ${scorePreview.customFormatScore >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                              {scorePreview.customFormatScore >= 0 ? '+' : ''}{scorePreview.customFormatScore}
                            </div>
                            <div className="text-xs text-gray-500">Sum of matched formats</div>
                          </div>
                        </div>

                        {/* Grand Total */}
                        <div className="p-4 bg-gradient-to-r from-gray-900 to-gray-800 rounded-lg border border-gray-600">
                          <div className="flex items-center justify-between">
                            <div>
                              <div className="text-sm text-gray-400">Total Score</div>
                              <div className="text-xs text-gray-500 mt-0.5">Quality + Custom Formats</div>
                            </div>
                            <div className={`text-2xl font-bold ${scorePreview.totalScore >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                              {scorePreview.totalScore}
                            </div>
                          </div>
                        </div>

                        {/* Synthetic Title */}
                        <div className="pt-3 border-t border-gray-700">
                          <div className="text-xs text-gray-500 mb-1">Scene Title Preview (for scoring)</div>
                          <code className="text-xs text-gray-400 bg-black/50 px-2 py-1 rounded block overflow-x-auto">
                            {scorePreview.syntheticTitle}
                          </code>
                        </div>

                        {/* Notes */}
                        <div className="text-xs text-gray-500 pt-2 border-t border-gray-700 space-y-1">
                          <p>Scores calculated using "{userQualityProfiles.find(p => p.id === selectedQualityProfileId)?.name}" profile (preview assumes 1080p source).</p>
                          <p className="text-blue-400">Actual scores will vary based on each channel's detected resolution (4K, FHD, HD, SD).</p>
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
                      const selectedOption = HardwareAccelerationOptions.find(o => o.value === selected);
                      const hwInfo = availableHwAccel.find(h => h.type === selected);
                      const isDetected = selected === 0 || selected === 99 || hwInfo?.isAvailable;

                      return (
                        <>
                          {/* Show Docker requirements for hardware encoders */}
                          {selectedOption?.dockerRequirements && (
                            <p className="text-xs text-blue-400 mt-1 font-mono">
                              {selectedOption.dockerRequirements}
                            </p>
                          )}
                          {/* Show warning if not detected */}
                          {!isDetected && selected !== 0 && selected !== 99 && availableHwAccel.length > 0 && (
                            <p className="text-xs text-yellow-500 mt-1">
                              ⚠️ Not detected - ensure Docker has access to GPU device. Will fall back to software encoding if unavailable.
                            </p>
                          )}
                        </>
                      );
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
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Post-Recording Command (Optional)</label>
                    <input
                      type="text"
                      value={dvrSettings.postRecordingCommand}
                      onChange={(e) => handleSettingsChange('postRecordingCommand', e.target.value)}
                      placeholder="/scripts/comskip.sh"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Script or executable run after each completed recording (comskip, transcode, notify).
                      Details are passed as environment variables: SPORTARR_RECORDING_PATH, SPORTARR_RECORDING_TITLE,
                      SPORTARR_RECORDING_ID, SPORTARR_EVENT_ID, SPORTARR_DURATION_SECONDS, SPORTARR_FILE_SIZE.
                    </p>
                  </div>
                  <div className="md:col-span-2">
                    <label className="flex items-start space-x-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={dvrSettings.overtimeGuardEnabled}
                        onChange={(e) => handleSettingsChange('overtimeGuardEnabled', e.target.checked)}
                        className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                      />
                      <div>
                        <span className="text-white font-medium">Overtime Guard</span>
                        <p className="text-sm text-gray-400 mt-1">
                          Keep recording past the scheduled end while live scores say the event is still in progress,
                          so overtime, extra innings, and stoppage time aren't cut off. Extends in 10-minute steps.
                        </p>
                      </div>
                    </label>
                  </div>
                  {dvrSettings.overtimeGuardEnabled && (
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Max Overtime Extension (Minutes)</label>
                      <input
                        type="number"
                        min={10}
                        max={360}
                        value={dvrSettings.overtimeMaxExtensionMinutes}
                        onChange={(e) => handleSettingsChange('overtimeMaxExtensionMinutes', parseInt(e.target.value) || 120)}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      />
                      <p className="text-xs text-gray-500 mt-1">Ceiling on total extension per recording</p>
                    </div>
                  )}
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

                {/* Recording Path */}
                <div className="mb-6">
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

                {/* File Naming - Enhanced with TRaSH presets */}
                <div className="p-4 bg-gray-800/50 rounded-lg border border-gray-700">
                  <div className="flex items-center justify-between mb-3">
                    <label className="flex items-center gap-2 text-sm font-medium text-white">
                      <DocumentTextIcon className="w-5 h-5 text-purple-400" />
                      File Naming Pattern
                    </label>
                    {namingPresets?.file && Object.keys(namingPresets.file).length > 0 && (
                      <div className="flex items-center gap-2">
                        <CloudArrowDownIcon className="w-4 h-4 text-purple-400" />
                        <select
                          value={selectedNamingPreset}
                          onChange={(e) => handleApplyNamingPreset(e.target.value)}
                          className="px-3 py-1 bg-gray-800 border border-purple-700 rounded text-sm text-purple-200 focus:outline-none focus:border-purple-500"
                        >
                          <option value="" className="bg-gray-900 text-gray-300">TRaSH Naming Presets...</option>
                          {Object.entries(namingPresets.file).map(([key, preset]) => (
                            <option key={key} value={key} className="bg-gray-900 text-white">
                              {key.replace(/-/g, ' ').replace(/\b\w/g, l => l.toUpperCase())}
                              {preset.supportsMultiPart ? ' (Multi-Part)' : ''}
                            </option>
                          ))}
                        </select>
                      </div>
                    )}
                  </div>

                  <input
                    type="text"
                    value={dvrSettings.fileNamingPattern}
                    onChange={(e) => {
                      handleSettingsChange('fileNamingPattern', e.target.value);
                      setSelectedNamingPreset(''); // Clear preset selection when manually editing
                    }}
                    placeholder="{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}"
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-red-600"
                  />

                  {/* Token Helper */}
                  <div className="mt-3 p-3 bg-black/30 rounded-lg border border-gray-800">
                    <p className="text-xs font-medium text-gray-400 mb-2">Available Tokens (click to insert):</p>
                    <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                      {[
                        { token: '{Series}', desc: 'League name', category: 'Plex' },
                        { token: '{Season}', desc: 's2024', category: 'Plex' },
                        { token: '{Episode}', desc: 'e12', category: 'Plex' },
                        { token: '{Part}', desc: 'pt1/pt2/pt3', category: 'Plex' },
                        { token: '{Event Title}', desc: 'Event name', category: 'Event' },
                        { token: '{Air Date}', desc: '2024-04-13', category: 'Event' },
                        { token: '{Quality Full}', desc: 'HDTV-1080p', category: 'Quality' },
                        { token: '{Release Group}', desc: 'DVR', category: 'Release' },
                      ].map((item) => (
                        <button
                          key={item.token}
                          type="button"
                          onClick={() => {
                            const currentFormat = dvrSettings.fileNamingPattern || '';
                            handleSettingsChange('fileNamingPattern', currentFormat + item.token);
                            setSelectedNamingPreset('');
                          }}
                          className="text-left px-2 py-1.5 bg-gray-800 hover:bg-gray-700 border border-gray-700 hover:border-purple-600 rounded text-xs transition-colors group"
                        >
                          <div className="font-mono text-purple-400 group-hover:text-purple-300">{item.token}</div>
                          <div className="text-gray-500 text-[10px]">{item.desc}</div>
                        </button>
                      ))}
                    </div>
                  </div>

                  {/* Live Preview */}
                  <div className="mt-3 p-3 bg-gradient-to-r from-blue-950/30 to-purple-950/30 border border-blue-900/50 rounded-lg">
                    <p className="text-xs font-medium text-blue-300 mb-1">Preview:</p>
                    <p className="text-white font-mono text-sm break-all">
                      {(dvrSettings.fileNamingPattern || '{Title} - {Date}')
                        .replace(/{Series}/gi, 'UFC')
                        .replace(/{Season}/gi, 's2024')
                        .replace(/{Episode}/gi, 'e12')
                        .replace(/{Part}/gi, ' - pt3')
                        .replace(/{Event Title}/gi, 'UFC 315')
                        .replace(/{Title}/gi, 'UFC 315')
                        .replace(/{Air Date}/gi, '2024-12-26')
                        .replace(/{Date}/gi, '2024-12-26')
                        .replace(/{League}/gi, 'UFC')
                        .replace(/{Channel}/gi, 'ESPN')
                        .replace(/{Quality Full}/gi, 'HDTV-1080p')
                        .replace(/{Quality}/gi, '1080p')
                        .replace(/{Release Group}/gi, 'DVR')
                      }.mkv
                    </p>
                    <p className="text-[10px] text-gray-500 mt-1">
                      Using TRaSH-compatible naming ensures Plex and other media players recognize your recordings correctly
                    </p>
                  </div>

                  {/* Info about naming consistency */}
                  <div className="mt-3 p-2 bg-blue-950/20 border border-blue-900/30 rounded text-xs text-gray-400">
                    <InformationCircleIcon className="w-4 h-4 text-blue-400 inline mr-1" />
                    Use the same naming format as your Media Management settings for consistent file organization across DVR recordings and imported files.
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
                    <label className="block text-sm font-medium text-gray-300 mb-2">Channels per Event</label>
                    <input
                      type="number"
                      value={dvrSettings.simultaneousChannels}
                      onChange={(e) => handleSettingsChange('simultaneousChannels', Math.min(5, Math.max(1, parseInt(e.target.value) || 1)))}
                      min="1"
                      max="5"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Record each event from this many channels at once, preferring different providers.
                      Redundancy against one stream dropping mid-event; costs extra tuner slots. 1 = preferred channel only.
                    </p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">When at Recording Limit</label>
                    <select
                      value={dvrSettings.conflictPolicy}
                      onChange={(e) => handleSettingsChange('conflictPolicy', e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    >
                      <option value="Refuse">Refuse (default - return 409)</option>
                      <option value="Queue">Queue and start when slot frees</option>
                      <option value="Preempt">Preempt lowest-priority scheduled</option>
                    </select>
                    <p className="text-xs text-gray-500 mt-1">
                      Applies when an IPTV source's MaxStreams cap or the global cap above is reached.
                      Active recordings are never preempted.
                    </p>
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
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Keep Last N Recordings Per League</label>
                    <input
                      type="number"
                      value={dvrSettings.keepLastRecordingsPerLeague}
                      onChange={(e) => handleSettingsChange('keepLastRecordingsPerLeague', parseInt(e.target.value) || 0)}
                      min="0"
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Prune older finished recordings beyond the newest N per league. 0 = unlimited
                    </p>
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

              {/* Catchup Settings */}
              <div className="mb-6">
                <h4 className="text-lg font-semibold text-white mb-1">Catchup Recording</h4>
                <p className="text-xs text-gray-500 mb-4">
                  When a channel's provider keeps a catchup archive, finished events are downloaded
                  from the archive after they air instead of being recorded live — no start/end
                  guesswork, and missed events can still be grabbed while the archive retains them.
                  Channels without an archive always record live.
                </p>
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                  <div className="flex items-center">
                    <label className="flex items-center cursor-pointer">
                      <input
                        type="checkbox"
                        checked={dvrSettings.useCatchupWhenAvailable}
                        onChange={(e) => handleSettingsChange('useCatchupWhenAvailable', e.target.checked)}
                        className="w-4 h-4 text-red-600 bg-gray-800 border-gray-700 rounded focus:ring-red-500"
                      />
                      <span className="ml-2 text-sm text-gray-300">Use catchup when available</span>
                    </label>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Ready Grace (minutes)</label>
                    <input
                      type="number"
                      value={dvrSettings.catchupReadyGraceMinutes}
                      onChange={(e) => handleSettingsChange('catchupReadyGraceMinutes', parseInt(e.target.value) || 0)}
                      min="0"
                      max="180"
                      disabled={!dvrSettings.useCatchupWhenAvailable}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 disabled:opacity-50"
                    />
                    <p className="text-xs text-gray-500 mt-1">Wait after the event ends before downloading</p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Timeshift URL Style</label>
                    <select
                      value={dvrSettings.catchupTimeshiftMode}
                      onChange={(e) => handleSettingsChange('catchupTimeshiftMode', e.target.value)}
                      disabled={!dvrSettings.useCatchupWhenAvailable}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 disabled:opacity-50"
                    >
                      <option value="auto">Auto-detect (recommended)</option>
                      <option value="path">Path (most panels)</option>
                      <option value="php">PHP (older panels)</option>
                    </select>
                    <p className="text-xs text-gray-500 mt-1">Auto tries both and remembers what your provider supports</p>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Backfill Window (hours)</label>
                    <input
                      type="number"
                      value={dvrSettings.catchupBackfillHours}
                      onChange={(e) => handleSettingsChange('catchupBackfillHours', parseInt(e.target.value) || 0)}
                      min="0"
                      max="336"
                      disabled={!dvrSettings.useCatchupWhenAvailable}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 disabled:opacity-50"
                    />
                    <p className="text-xs text-gray-500 mt-1">How far back to grab missed events</p>
                  </div>
                </div>
              </div>

        </div>
      </div>
      </div>
    </div>
  );
}
