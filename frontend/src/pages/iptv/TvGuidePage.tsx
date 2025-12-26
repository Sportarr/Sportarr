import { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import {
  ClockIcon,
  PlayCircleIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  FunnelIcon,
  CalendarDaysIcon,
  TvIcon,
  VideoCameraIcon,
  MagnifyingGlassIcon,
  ArrowPathIcon,
  PlusIcon,
  Cog6ToothIcon,
  InformationCircleIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import { useTimezone } from '../../hooks/useTimezone';
import { formatTimeInTimezone, formatDateInTimezone } from '../../utils/timezone';

// Types
interface TvGuideProgram {
  id: number;
  title: string;
  description?: string;
  category?: string;
  startTime: string;
  endTime: string;
  iconUrl?: string;
  isSportsProgram: boolean;
  hasDvrRecording: boolean;
  dvrRecordingId?: number;
  dvrRecordingStatus?: string;
  matchedEventId?: number;
}

interface TvGuideChannel {
  id: number;
  name: string;
  logoUrl?: string;
  channelNumber?: number;
  tvgId?: string;
  programs: TvGuideProgram[];
}

interface TvGuideResponse {
  startTime: string;
  endTime: string;
  channels: TvGuideChannel[];
  totalChannels: number;
}

interface EpgSource {
  id: number;
  name: string;
  url: string;
  isActive: boolean;
  lastUpdated?: string;
  lastError?: string;
  programCount: number;
}

// DVR status colors
const DVR_STATUS_COLORS: Record<string, { bg: string; border: string; text: string }> = {
  Scheduled: { bg: 'bg-blue-900/50', border: 'border-blue-500', text: 'text-blue-400' },
  Recording: { bg: 'bg-red-900/50', border: 'border-red-500', text: 'text-red-400' },
  Completed: { bg: 'bg-green-900/50', border: 'border-green-500', text: 'text-green-400' },
  Failed: { bg: 'bg-red-900/50', border: 'border-red-500', text: 'text-red-400' },
};

// Time slot width in pixels (30 minutes = 120px)
const SLOT_WIDTH = 120;
const CHANNEL_WIDTH = 200;
const HEADER_HEIGHT = 48;
const ROW_HEIGHT = 64;

export default function TvGuidePage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const { timezone } = useTimezone();

  // State
  const [guideData, setGuideData] = useState<TvGuideResponse | null>(null);
  const [epgSources, setEpgSources] = useState<EpgSource[]>([]);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [currentTime, setCurrentTime] = useState(new Date());
  const [timeOffset, setTimeOffset] = useState(0); // Hours offset from now
  const [selectedProgram, setSelectedProgram] = useState<TvGuideProgram | null>(null);
  const [selectedChannel, setSelectedChannel] = useState<TvGuideChannel | null>(null);

  // Filters from URL params
  const scheduledOnly = searchParams.get('scheduledOnly') === 'true';
  const sportsOnly = searchParams.get('sportsOnly') === 'true';
  const enabledOnly = searchParams.get('enabledOnly') !== 'false'; // Default true
  const selectedGroup = searchParams.get('group') || '';

  const [showFilters, setShowFilters] = useState(false);
  const [showEpgSettings, setShowEpgSettings] = useState(false);
  const [newEpgUrl, setNewEpgUrl] = useState('');
  const [newEpgName, setNewEpgName] = useState('');
  const [channelGroups, setChannelGroups] = useState<string[]>([]);

  const gridRef = useRef<HTMLDivElement>(null);

  // Update current time every minute
  useEffect(() => {
    const interval = setInterval(() => {
      setCurrentTime(new Date());
    }, 60000);
    return () => clearInterval(interval);
  }, []);

  // Load data
  useEffect(() => {
    loadGuideData();
    loadEpgSources();
    loadChannelGroups();
  }, [timeOffset, scheduledOnly, sportsOnly, enabledOnly, selectedGroup]);

  const loadGuideData = async () => {
    setLoading(true);
    try {
      const startTime = new Date();
      startTime.setHours(startTime.getHours() + timeOffset);
      startTime.setMinutes(0, 0, 0);

      const endTime = new Date(startTime);
      endTime.setHours(endTime.getHours() + 12);

      const params = new URLSearchParams({
        start: startTime.toISOString(),
        end: endTime.toISOString(),
        offset: '0',
        limit: '500', // Increased from 100 to show more channels
      });

      if (scheduledOnly) params.set('scheduledOnly', 'true');
      if (sportsOnly) params.set('sportsOnly', 'true');
      if (enabledOnly) params.set('enabledOnly', 'true');
      if (selectedGroup) params.set('group', selectedGroup);

      const response = await apiClient.get<TvGuideResponse>(`/epg/guide?${params}`);
      setGuideData(response.data);
    } catch (error) {
      console.error('Failed to load TV Guide:', error);
      toast.error('Failed to load TV Guide');
    } finally {
      setLoading(false);
    }
  };

  const loadEpgSources = async () => {
    try {
      const response = await apiClient.get<EpgSource[]>('/epg/sources');
      setEpgSources(response.data);
    } catch (error) {
      console.error('Failed to load EPG sources:', error);
    }
  };

  const loadChannelGroups = async () => {
    try {
      const response = await apiClient.get<string[]>('/epg/groups');
      setChannelGroups(response.data);
    } catch (error) {
      console.error('Failed to load channel groups:', error);
    }
  };

  const syncEpgSources = async () => {
    setSyncing(true);
    try {
      await apiClient.post('/epg/sync-all');
      toast.success('EPG sync started');
      await loadEpgSources();
      await loadGuideData();
    } catch (error) {
      console.error('Failed to sync EPG:', error);
      toast.error('Failed to sync EPG sources');
    } finally {
      setSyncing(false);
    }
  };

  const addEpgSource = async () => {
    if (!newEpgUrl.trim() || !newEpgName.trim()) {
      toast.error('Please enter both name and URL');
      return;
    }

    try {
      await apiClient.post('/epg/sources', {
        name: newEpgName.trim(),
        url: newEpgUrl.trim(),
      });
      toast.success('EPG source added');
      setNewEpgUrl('');
      setNewEpgName('');
      await loadEpgSources();
    } catch (error) {
      console.error('Failed to add EPG source:', error);
      toast.error('Failed to add EPG source');
    }
  };

  const deleteEpgSource = async (id: number) => {
    try {
      await apiClient.delete(`/epg/sources/${id}`);
      toast.success('EPG source deleted');
      await loadEpgSources();
    } catch (error) {
      console.error('Failed to delete EPG source:', error);
      toast.error('Failed to delete EPG source');
    }
  };

  const scheduleDvr = async (program: TvGuideProgram) => {
    try {
      await apiClient.post(`/epg/programs/${program.id}/schedule-dvr`);
      toast.success(`DVR scheduled for "${program.title}"`);
      await loadGuideData();
    } catch (error: any) {
      console.error('Failed to schedule DVR:', error);
      toast.error(error.response?.data?.error || 'Failed to schedule DVR');
    }
  };

  const cancelDvr = async (recordingId: number) => {
    try {
      await apiClient.post(`/dvr/recordings/${recordingId}/cancel`);
      toast.success('DVR recording cancelled');
      await loadGuideData();
    } catch (error) {
      console.error('Failed to cancel DVR:', error);
      toast.error('Failed to cancel DVR');
    }
  };

  // Toggle filter
  const toggleFilter = (key: string, value: boolean) => {
    const newParams = new URLSearchParams(searchParams);
    if (value) {
      newParams.set(key, 'true');
    } else {
      newParams.delete(key);
    }
    setSearchParams(newParams);
  };

  // Calculate time slots for the grid
  const timeSlots = useMemo(() => {
    if (!guideData) return [];

    const start = new Date(guideData.startTime);
    const slots: Date[] = [];

    for (let i = 0; i < 24; i++) { // 24 slots of 30 min = 12 hours
      const slot = new Date(start);
      slot.setMinutes(slot.getMinutes() + i * 30);
      slots.push(slot);
    }

    return slots;
  }, [guideData]);

  // Calculate program position and width
  const getProgramStyle = useCallback((program: TvGuideProgram) => {
    if (!guideData) return { left: 0, width: 0 };

    const guideStart = new Date(guideData.startTime).getTime();
    const guideEnd = new Date(guideData.endTime).getTime();
    const programStart = Math.max(new Date(program.startTime).getTime(), guideStart);
    const programEnd = Math.min(new Date(program.endTime).getTime(), guideEnd);

    const totalDuration = guideEnd - guideStart;
    const startOffset = (programStart - guideStart) / totalDuration;
    const duration = (programEnd - programStart) / totalDuration;

    const totalWidth = SLOT_WIDTH * 24; // 24 slots

    return {
      left: startOffset * totalWidth,
      width: Math.max(duration * totalWidth - 2, 40), // Min width of 40px, -2 for gap
    };
  }, [guideData]);

  // Calculate current time indicator position
  const currentTimePosition = useMemo(() => {
    if (!guideData) return null;

    const guideStart = new Date(guideData.startTime).getTime();
    const guideEnd = new Date(guideData.endTime).getTime();
    const now = currentTime.getTime();

    if (now < guideStart || now > guideEnd) return null;

    const totalDuration = guideEnd - guideStart;
    const offset = (now - guideStart) / totalDuration;
    const totalWidth = SLOT_WIDTH * 24;

    return offset * totalWidth;
  }, [guideData, currentTime]);

  // Format time for display
  const formatTime = (date: Date | string) => {
    const d = typeof date === 'string' ? new Date(date) : date;
    return formatTimeInTimezone(d.toISOString(), timezone, { hour: '2-digit', minute: '2-digit' });
  };

  if (loading && !guideData) {
    return (
      <div className="p-8 flex items-center justify-center">
        <ArrowPathIcon className="w-8 h-8 animate-spin text-red-500" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="bg-gradient-to-br from-gray-900 to-black border-b border-red-900/30 p-4">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h1 className="text-2xl font-bold text-white flex items-center gap-2">
              <TvIcon className="w-7 h-7 text-red-500" />
              TV Guide
            </h1>
            <p className="text-gray-400 text-sm mt-1">
              Browse EPG data and schedule DVR recordings
            </p>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() => setShowEpgSettings(!showEpgSettings)}
              className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded-lg transition-colors"
              title="EPG Settings"
            >
              <Cog6ToothIcon className="w-5 h-5" />
            </button>
            <button
              onClick={syncEpgSources}
              disabled={syncing}
              className="flex items-center gap-2 px-3 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors disabled:opacity-50"
            >
              <ArrowPathIcon className={`w-4 h-4 ${syncing ? 'animate-spin' : ''}`} />
              Sync EPG
            </button>
          </div>
        </div>

        {/* Time Navigation */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <button
              onClick={() => setTimeOffset(prev => prev - 6)}
              className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded-lg transition-colors"
            >
              <ChevronLeftIcon className="w-5 h-5" />
            </button>
            <button
              onClick={() => setTimeOffset(0)}
              className="px-3 py-1 text-sm bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
            >
              Now
            </button>
            <button
              onClick={() => setTimeOffset(prev => prev + 6)}
              className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded-lg transition-colors"
            >
              <ChevronRightIcon className="w-5 h-5" />
            </button>

            {guideData && (
              <span className="text-gray-400 text-sm ml-4">
                {formatDateInTimezone(guideData.startTime, timezone, { weekday: 'short', month: 'short', day: 'numeric' })}
                {' '}
                {formatTime(guideData.startTime)} - {formatTime(guideData.endTime)}
              </span>
            )}
          </div>

          {/* Filters */}
          <div className="flex items-center gap-4">
            <button
              onClick={() => setShowFilters(!showFilters)}
              className={`flex items-center gap-2 px-3 py-1 rounded-lg transition-colors ${
                showFilters ? 'bg-red-600 text-white' : 'bg-gray-800 text-gray-300 hover:bg-gray-700'
              }`}
            >
              <FunnelIcon className="w-4 h-4" />
              Filters
            </button>
          </div>
        </div>

        {/* Filter Panel */}
        {showFilters && (
          <div className="mt-4 p-4 bg-gray-800/50 rounded-lg border border-gray-700">
            <div className="flex flex-wrap gap-4">
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={scheduledOnly}
                  onChange={(e) => toggleFilter('scheduledOnly', e.target.checked)}
                  className="rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                />
                <span className="text-gray-300 text-sm">Scheduled recordings only</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={sportsOnly}
                  onChange={(e) => toggleFilter('sportsOnly', e.target.checked)}
                  className="rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                />
                <span className="text-gray-300 text-sm">Sports channels only</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={enabledOnly}
                  onChange={(e) => toggleFilter('enabledOnly', e.target.checked)}
                  className="rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                />
                <span className="text-gray-300 text-sm">Enabled channels only</span>
              </label>

              {/* Group Filter */}
              {channelGroups.length > 0 && (
                <div className="flex items-center gap-2">
                  <span className="text-gray-300 text-sm">Group:</span>
                  <select
                    value={selectedGroup}
                    onChange={(e) => {
                      const newParams = new URLSearchParams(searchParams);
                      if (e.target.value) {
                        newParams.set('group', e.target.value);
                      } else {
                        newParams.delete('group');
                      }
                      setSearchParams(newParams);
                    }}
                    className="px-3 py-1.5 bg-gray-700 border border-gray-600 rounded-lg text-white text-sm focus:border-red-500 focus:ring-1 focus:ring-red-500"
                  >
                    <option value="">All Groups</option>
                    {channelGroups.map((group) => (
                      <option key={group} value={group}>
                        {group}
                      </option>
                    ))}
                  </select>
                </div>
              )}
            </div>
          </div>
        )}

        {/* EPG Settings Panel */}
        {showEpgSettings && (
          <div className="mt-4 p-4 bg-gray-800/50 rounded-lg border border-gray-700">
            <h3 className="text-white font-semibold mb-3">EPG Sources</h3>

            {/* Add new source */}
            <div className="flex gap-2 mb-4">
              <input
                type="text"
                placeholder="Source name"
                value={newEpgName}
                onChange={(e) => setNewEpgName(e.target.value)}
                className="flex-1 max-w-xs px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-400 focus:border-red-500 focus:ring-1 focus:ring-red-500"
              />
              <input
                type="text"
                placeholder="XMLTV URL (http://... or .xml.gz)"
                value={newEpgUrl}
                onChange={(e) => setNewEpgUrl(e.target.value)}
                className="flex-[2] px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-400 focus:border-red-500 focus:ring-1 focus:ring-red-500"
              />
              <button
                onClick={addEpgSource}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                <PlusIcon className="w-5 h-5" />
              </button>
            </div>

            {/* Existing sources */}
            {epgSources.length === 0 ? (
              <p className="text-gray-400 text-sm">No EPG sources configured. Add an XMLTV URL above to get started.</p>
            ) : (
              <div className="space-y-2">
                {epgSources.map((source) => (
                  <div key={source.id} className="flex items-center justify-between p-3 bg-gray-700/50 rounded-lg">
                    <div>
                      <div className="text-white font-medium">{source.name}</div>
                      <div className="text-gray-400 text-xs truncate max-w-md">{source.url}</div>
                      <div className="text-gray-500 text-xs mt-1">
                        {source.programCount} programs
                        {source.lastUpdated && ` | Updated: ${formatDateInTimezone(source.lastUpdated, timezone, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}`}
                        {source.lastError && <span className="text-red-400"> | Error: {source.lastError}</span>}
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <button
                        onClick={() => apiClient.post(`/epg/sources/${source.id}/sync`).then(() => { toast.success('Syncing...'); loadEpgSources(); })}
                        className="p-2 text-gray-400 hover:text-white hover:bg-gray-600 rounded transition-colors"
                        title="Sync this source"
                      >
                        <ArrowPathIcon className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => deleteEpgSource(source.id)}
                        className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-900/30 rounded transition-colors"
                        title="Delete"
                      >
                        <span className="text-lg">&times;</span>
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Guide Grid */}
      <div className="flex-1 overflow-hidden">
        {!guideData || guideData.channels.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-gray-400">
            <TvIcon className="w-16 h-16 mb-4 opacity-50" />
            <p className="text-lg">No TV Guide data available</p>
            <p className="text-sm mt-2">
              {epgSources.length === 0
                ? 'Add an EPG source above to get started'
                : 'Try syncing your EPG sources or adjusting filters'}
            </p>
          </div>
        ) : (
          <div className="h-full overflow-auto" ref={gridRef}>
            <div className="relative" style={{ minWidth: CHANNEL_WIDTH + SLOT_WIDTH * 24 }}>
              {/* Time Header */}
              <div
                className="sticky top-0 z-20 flex bg-gray-900 border-b border-gray-700"
                style={{ height: HEADER_HEIGHT }}
              >
                {/* Channel column header */}
                <div
                  className="sticky left-0 z-30 bg-gray-900 border-r border-gray-700 flex items-center px-4"
                  style={{ width: CHANNEL_WIDTH, minWidth: CHANNEL_WIDTH }}
                >
                  <span className="text-gray-400 font-medium">Channel</span>
                </div>

                {/* Time slots */}
                <div className="flex">
                  {timeSlots.map((slot, i) => (
                    <div
                      key={i}
                      className="flex items-center justify-center border-r border-gray-700 text-gray-400 text-sm"
                      style={{ width: SLOT_WIDTH, minWidth: SLOT_WIDTH }}
                    >
                      {formatTime(slot)}
                    </div>
                  ))}
                </div>
              </div>

              {/* Channel Rows */}
              {guideData.channels.map((channel) => (
                <div
                  key={channel.id}
                  className="flex border-b border-gray-800 hover:bg-gray-800/30"
                  style={{ height: ROW_HEIGHT }}
                >
                  {/* Channel Info */}
                  <div
                    className="sticky left-0 z-10 bg-gray-900 border-r border-gray-700 flex items-center gap-2 px-3"
                    style={{ width: CHANNEL_WIDTH, minWidth: CHANNEL_WIDTH }}
                  >
                    {channel.logoUrl ? (
                      <img
                        src={channel.logoUrl}
                        alt={channel.name}
                        className="w-8 h-8 object-contain rounded"
                        onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }}
                      />
                    ) : (
                      <div className="w-8 h-8 bg-gray-700 rounded flex items-center justify-center">
                        <TvIcon className="w-4 h-4 text-gray-500" />
                      </div>
                    )}
                    <div className="flex-1 min-w-0">
                      <div className="text-white text-sm font-medium truncate">{channel.name}</div>
                      {channel.channelNumber && (
                        <div className="text-gray-500 text-xs">{channel.channelNumber}</div>
                      )}
                    </div>
                  </div>

                  {/* Programs */}
                  <div className="relative flex-1" style={{ width: SLOT_WIDTH * 24 }}>
                    {/* Grid lines */}
                    <div className="absolute inset-0 flex pointer-events-none">
                      {timeSlots.map((_, i) => (
                        <div
                          key={i}
                          className="border-r border-gray-800/50"
                          style={{ width: SLOT_WIDTH }}
                        />
                      ))}
                    </div>

                    {/* Current time indicator */}
                    {currentTimePosition !== null && (
                      <div
                        className="absolute top-0 bottom-0 w-0.5 bg-red-500 z-10"
                        style={{ left: currentTimePosition }}
                      />
                    )}

                    {/* Program blocks */}
                    {channel.programs.map((program, i) => {
                      const style = getProgramStyle(program);
                      const dvrColors = program.dvrRecordingStatus
                        ? DVR_STATUS_COLORS[program.dvrRecordingStatus]
                        : null;

                      return (
                        <div
                          key={`${program.id}-${i}`}
                          className={`absolute top-1 bottom-1 rounded cursor-pointer overflow-hidden transition-all hover:z-10 hover:scale-[1.02] ${
                            program.hasDvrRecording
                              ? `${dvrColors?.bg || 'bg-blue-900/50'} ${dvrColors?.border || 'border-blue-500'} border-2`
                              : program.isSportsProgram
                                ? 'bg-green-900/40 border border-green-700/50 hover:bg-green-900/60'
                                : 'bg-gray-800 border border-gray-700 hover:bg-gray-700'
                          }`}
                          style={{
                            left: style.left,
                            width: style.width,
                          }}
                          onClick={() => {
                            setSelectedProgram(program);
                            setSelectedChannel(channel);
                          }}
                          title={`${program.title}\n${formatTime(program.startTime)} - ${formatTime(program.endTime)}`}
                        >
                          <div className="p-1.5 h-full flex flex-col">
                            <div className={`text-xs font-medium truncate ${
                              program.hasDvrRecording
                                ? dvrColors?.text || 'text-blue-400'
                                : 'text-white'
                            }`}>
                              {program.title}
                            </div>
                            <div className="text-[10px] text-gray-400 truncate">
                              {formatTime(program.startTime)} - {formatTime(program.endTime)}
                            </div>
                            {program.hasDvrRecording && (
                              <div className="mt-auto">
                                <VideoCameraIcon className={`w-3 h-3 ${dvrColors?.text || 'text-blue-400'}`} />
                              </div>
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Program Details Modal */}
      {selectedProgram && selectedChannel && (
        <div
          className="fixed inset-0 bg-black/70 flex items-center justify-center z-50 p-4"
          onClick={() => { setSelectedProgram(null); setSelectedChannel(null); }}
        >
          <div
            className="bg-gray-900 border border-gray-700 rounded-xl max-w-lg w-full max-h-[90vh] overflow-auto"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="p-6">
              <div className="flex items-start justify-between mb-4">
                <div>
                  <h2 className="text-xl font-bold text-white">{selectedProgram.title}</h2>
                  <p className="text-gray-400 text-sm mt-1">{selectedChannel.name}</p>
                </div>
                <button
                  onClick={() => { setSelectedProgram(null); setSelectedChannel(null); }}
                  className="text-gray-400 hover:text-white p-1"
                >
                  <span className="text-2xl">&times;</span>
                </button>
              </div>

              <div className="space-y-4">
                <div className="flex items-center gap-4 text-sm">
                  <div className="flex items-center gap-1 text-gray-400">
                    <ClockIcon className="w-4 h-4" />
                    {formatTime(selectedProgram.startTime)} - {formatTime(selectedProgram.endTime)}
                  </div>
                  {selectedProgram.category && (
                    <span className="px-2 py-0.5 bg-gray-700 text-gray-300 rounded text-xs">
                      {selectedProgram.category}
                    </span>
                  )}
                  {selectedProgram.isSportsProgram && (
                    <span className="px-2 py-0.5 bg-green-900/50 text-green-400 rounded text-xs">
                      Sports
                    </span>
                  )}
                </div>

                {selectedProgram.description && (
                  <p className="text-gray-300 text-sm">{selectedProgram.description}</p>
                )}

                {selectedProgram.hasDvrRecording ? (
                  <div className={`p-3 rounded-lg ${DVR_STATUS_COLORS[selectedProgram.dvrRecordingStatus || '']?.bg || 'bg-blue-900/30'}`}>
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <VideoCameraIcon className={`w-5 h-5 ${DVR_STATUS_COLORS[selectedProgram.dvrRecordingStatus || '']?.text || 'text-blue-400'}`} />
                        <span className={DVR_STATUS_COLORS[selectedProgram.dvrRecordingStatus || '']?.text || 'text-blue-400'}>
                          DVR: {selectedProgram.dvrRecordingStatus}
                        </span>
                      </div>
                      {selectedProgram.dvrRecordingStatus === 'Scheduled' && (
                        <button
                          onClick={() => selectedProgram.dvrRecordingId && cancelDvr(selectedProgram.dvrRecordingId)}
                          className="px-3 py-1 bg-red-600 hover:bg-red-700 text-white text-sm rounded transition-colors"
                        >
                          Cancel
                        </button>
                      )}
                    </div>
                  </div>
                ) : selectedProgram.id > 0 ? (
                  <button
                    onClick={() => scheduleDvr(selectedProgram)}
                    className="w-full flex items-center justify-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    <VideoCameraIcon className="w-5 h-5" />
                    Schedule DVR Recording
                  </button>
                ) : (
                  <div className="p-3 bg-gray-800 rounded-lg text-gray-400 text-sm">
                    <InformationCircleIcon className="w-5 h-5 inline mr-2" />
                    This is a DVR-only entry (no EPG data). Use the Recordings page to manage.
                  </div>
                )}

                {selectedProgram.matchedEventId && (
                  <button
                    onClick={() => navigate(`/events/${selectedProgram.matchedEventId}`)}
                    className="w-full flex items-center justify-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                  >
                    View Matched Event
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Legend */}
      <div className="bg-gray-900 border-t border-gray-700 px-4 py-2 flex items-center gap-6 text-xs">
        <span className="text-gray-500">Legend:</span>
        <div className="flex items-center gap-1">
          <div className="w-3 h-3 bg-gray-800 border border-gray-700 rounded" />
          <span className="text-gray-400">Regular</span>
        </div>
        <div className="flex items-center gap-1">
          <div className="w-3 h-3 bg-green-900/40 border border-green-700/50 rounded" />
          <span className="text-gray-400">Sports</span>
        </div>
        <div className="flex items-center gap-1">
          <div className="w-3 h-3 bg-blue-900/50 border-2 border-blue-500 rounded" />
          <span className="text-gray-400">Scheduled DVR</span>
        </div>
        <div className="flex items-center gap-1">
          <div className="w-3 h-3 bg-red-900/50 border-2 border-red-500 rounded" />
          <span className="text-gray-400">Recording</span>
        </div>
        <div className="flex items-center gap-1 ml-auto">
          <div className="w-0.5 h-3 bg-red-500" />
          <span className="text-gray-400">Current Time</span>
        </div>
      </div>
    </div>
  );
}
