import { useState, useEffect, useMemo } from 'react';
import {
  CalendarDaysIcon,
  ClockIcon,
  PlayCircleIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  XCircleIcon,
  ArrowPathIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  SignalIcon,
  FilmIcon,
  TrophyIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../../api/client';

// Types
type RecordingStatus = 'Scheduled' | 'Recording' | 'Completed' | 'Failed' | 'Cancelled' | 'Importing' | 'Imported';

interface DvrRecording {
  id: number;
  eventId?: number;
  eventTitle: string;
  channelId: number;
  channelName: string;
  leagueName?: string;
  scheduledStart: string;
  scheduledEnd: string;
  actualStart?: string;
  actualEnd?: string;
  status: RecordingStatus;
  filePath?: string;
  fileSize?: number;
  errorMessage?: string;
  prePaddingMinutes: number;
  postPaddingMinutes: number;
  qualityProfileId?: number;
  qualityProfileName?: string;
}

interface SportEvent {
  id: number;
  title: string;
  scheduledDate: string;
  league: {
    id: number;
    name: string;
  };
  hasRecording: boolean;
  recordingId?: number;
}

export default function DvrSchedulePage() {
  const [recordings, setRecordings] = useState<DvrRecording[]>([]);
  const [upcomingEvents, setUpcomingEvents] = useState<SportEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [viewMode, setViewMode] = useState<'list' | 'calendar'>('list');
  const [currentWeekStart, setCurrentWeekStart] = useState(() => {
    const today = new Date();
    const dayOfWeek = today.getDay();
    const diff = today.getDate() - dayOfWeek + (dayOfWeek === 0 ? -6 : 1);
    return new Date(today.setDate(diff));
  });

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    setLoading(true);
    try {
      // Load scheduled and recording status recordings
      const recordingsResponse = await apiClient.get<DvrRecording[]>('/dvr/recordings');
      const scheduledRecordings = recordingsResponse.data.filter(
        r => r.status === 'Scheduled' || r.status === 'Recording'
      );
      setRecordings(scheduledRecordings);

      // Load upcoming events that could be recorded (events with channel mappings)
      try {
        const eventsResponse = await apiClient.get<SportEvent[]>('/events/upcoming?days=14&withChannelMappings=true');
        setUpcomingEvents(eventsResponse.data || []);
      } catch {
        // Events endpoint might not exist yet
        setUpcomingEvents([]);
      }
    } catch (err: any) {
      console.error('Failed to load data:', err);
      toast.error('Failed to load schedule data');
    } finally {
      setLoading(false);
    }
  };

  // Group recordings by date
  const recordingsByDate = useMemo(() => {
    const grouped: Record<string, DvrRecording[]> = {};
    recordings.forEach(recording => {
      const date = new Date(recording.scheduledStart).toISOString().split('T')[0];
      if (!grouped[date]) {
        grouped[date] = [];
      }
      grouped[date].push(recording);
    });
    // Sort recordings within each date
    Object.values(grouped).forEach(arr => {
      arr.sort((a, b) => new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime());
    });
    return grouped;
  }, [recordings]);

  // Get week days for calendar view
  const weekDays = useMemo(() => {
    const days = [];
    for (let i = 0; i < 7; i++) {
      const date = new Date(currentWeekStart);
      date.setDate(date.getDate() + i);
      days.push(date);
    }
    return days;
  }, [currentWeekStart]);

  const navigateWeek = (direction: 'prev' | 'next') => {
    setCurrentWeekStart(prev => {
      const newDate = new Date(prev);
      newDate.setDate(newDate.getDate() + (direction === 'next' ? 7 : -7));
      return newDate;
    });
  };

  const formatTime = (dateString: string) => {
    return new Date(dateString).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString([], { weekday: 'short', month: 'short', day: 'numeric' });
  };

  const formatDateFull = (date: Date) => {
    return date.toLocaleDateString([], { weekday: 'short', month: 'short', day: 'numeric' });
  };

  const getStatusColor = (status: RecordingStatus) => {
    switch (status) {
      case 'Scheduled':
        return 'text-blue-400 bg-blue-900/30';
      case 'Recording':
        return 'text-red-400 bg-red-900/30 animate-pulse';
      case 'Completed':
        return 'text-green-400 bg-green-900/30';
      case 'Failed':
        return 'text-red-400 bg-red-900/30';
      case 'Cancelled':
        return 'text-gray-400 bg-gray-900/30';
      default:
        return 'text-gray-400 bg-gray-900/30';
    }
  };

  const getStatusIcon = (status: RecordingStatus) => {
    switch (status) {
      case 'Scheduled':
        return <ClockIcon className="w-4 h-4" />;
      case 'Recording':
        return <PlayCircleIcon className="w-4 h-4" />;
      case 'Completed':
        return <CheckCircleIcon className="w-4 h-4" />;
      case 'Failed':
        return <XCircleIcon className="w-4 h-4" />;
      case 'Cancelled':
        return <XCircleIcon className="w-4 h-4" />;
      default:
        return <ClockIcon className="w-4 h-4" />;
    }
  };

  const getDurationMinutes = (start: string, end: string) => {
    const startDate = new Date(start);
    const endDate = new Date(end);
    return Math.round((endDate.getTime() - startDate.getTime()) / 1000 / 60);
  };

  const isToday = (date: Date) => {
    const today = new Date();
    return date.toDateString() === today.toDateString();
  };

  // Get recordings for a specific date (for calendar view)
  const getRecordingsForDate = (date: Date) => {
    const dateStr = date.toISOString().split('T')[0];
    return recordingsByDate[dateStr] || [];
  };

  return (
    <div className="p-4 md:p-8 max-w-7xl mx-auto">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-8">
        <div>
          <h1 className="text-3xl font-bold text-white flex items-center gap-3">
            <CalendarDaysIcon className="w-8 h-8 text-red-500" />
            DVR Schedule
          </h1>
          <p className="text-gray-400 mt-1">
            Upcoming scheduled recordings and events
          </p>
        </div>
        <div className="flex items-center gap-3">
          {/* View Toggle */}
          <div className="flex bg-gray-800 rounded-lg p-1">
            <button
              onClick={() => setViewMode('list')}
              className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                viewMode === 'list' ? 'bg-red-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              List
            </button>
            <button
              onClick={() => setViewMode('calendar')}
              className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                viewMode === 'calendar' ? 'bg-red-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              Calendar
            </button>
          </div>
          <button
            onClick={loadData}
            disabled={loading}
            className="p-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors disabled:opacity-50"
            title="Refresh"
          >
            <ArrowPathIcon className={`w-5 h-5 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {/* Stats Row */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-4">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-blue-900/30 rounded-lg">
              <ClockIcon className="w-5 h-5 text-blue-400" />
            </div>
            <div>
              <div className="text-2xl font-bold text-white">
                {recordings.filter(r => r.status === 'Scheduled').length}
              </div>
              <div className="text-sm text-gray-400">Scheduled</div>
            </div>
          </div>
        </div>
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-4">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-red-900/30 rounded-lg">
              <PlayCircleIcon className="w-5 h-5 text-red-400" />
            </div>
            <div>
              <div className="text-2xl font-bold text-white">
                {recordings.filter(r => r.status === 'Recording').length}
              </div>
              <div className="text-sm text-gray-400">Recording Now</div>
            </div>
          </div>
        </div>
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-4">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-purple-900/30 rounded-lg">
              <TrophyIcon className="w-5 h-5 text-purple-400" />
            </div>
            <div>
              <div className="text-2xl font-bold text-white">
                {upcomingEvents.length}
              </div>
              <div className="text-sm text-gray-400">Upcoming Events</div>
            </div>
          </div>
        </div>
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-4">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-green-900/30 rounded-lg">
              <SignalIcon className="w-5 h-5 text-green-400" />
            </div>
            <div>
              <div className="text-2xl font-bold text-white">
                {new Set(recordings.map(r => r.channelId)).size}
              </div>
              <div className="text-sm text-gray-400">Active Channels</div>
            </div>
          </div>
        </div>
      </div>

      {/* Loading State */}
      {loading && (
        <div className="flex items-center justify-center py-12">
          <div className="w-8 h-8 border-2 border-red-500 border-t-transparent rounded-full animate-spin" />
        </div>
      )}

      {/* No Recordings */}
      {!loading && recordings.length === 0 && (
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-12 text-center">
          <CalendarDaysIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
          <h3 className="text-xl font-semibold text-white mb-2">No Scheduled Recordings</h3>
          <p className="text-gray-400 max-w-md mx-auto">
            There are no upcoming recordings scheduled. Recordings are automatically created when events
            have channel mappings, or you can create manual recordings from the Recordings page.
          </p>
        </div>
      )}

      {/* List View */}
      {!loading && viewMode === 'list' && recordings.length > 0 && (
        <div className="space-y-6">
          {Object.entries(recordingsByDate)
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([date, dateRecordings]) => (
              <div key={date} className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
                <div className="px-4 py-3 bg-black/30 border-b border-gray-800 flex items-center gap-2">
                  <CalendarDaysIcon className="w-5 h-5 text-red-400" />
                  <span className="font-semibold text-white">
                    {formatDate(date)}
                    {isToday(new Date(date)) && (
                      <span className="ml-2 px-2 py-0.5 text-xs bg-red-600 text-white rounded">Today</span>
                    )}
                  </span>
                  <span className="text-gray-500 text-sm">({dateRecordings.length} recording{dateRecordings.length !== 1 ? 's' : ''})</span>
                </div>
                <div className="divide-y divide-gray-800">
                  {dateRecordings.map(recording => (
                    <div key={recording.id} className="p-4 hover:bg-gray-800/30 transition-colors">
                      <div className="flex items-start justify-between gap-4">
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-3 mb-1">
                            <span className={`flex items-center gap-1 px-2 py-1 rounded text-xs font-medium ${getStatusColor(recording.status)}`}>
                              {getStatusIcon(recording.status)}
                              {recording.status}
                            </span>
                            {recording.leagueName && (
                              <span className="text-xs text-purple-400 bg-purple-900/30 px-2 py-1 rounded">
                                {recording.leagueName}
                              </span>
                            )}
                          </div>
                          <h4 className="font-medium text-white truncate">{recording.eventTitle}</h4>
                          <div className="flex items-center gap-4 mt-2 text-sm text-gray-400">
                            <span className="flex items-center gap-1">
                              <SignalIcon className="w-4 h-4" />
                              {recording.channelName}
                            </span>
                            <span className="flex items-center gap-1">
                              <ClockIcon className="w-4 h-4" />
                              {formatTime(recording.scheduledStart)} - {formatTime(recording.scheduledEnd)}
                            </span>
                            <span className="text-gray-500">
                              ({getDurationMinutes(recording.scheduledStart, recording.scheduledEnd)} min)
                            </span>
                          </div>
                          {recording.qualityProfileName && (
                            <div className="mt-1 text-xs text-gray-500 flex items-center gap-1">
                              <FilmIcon className="w-3 h-3" />
                              {recording.qualityProfileName}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))}
        </div>
      )}

      {/* Calendar View */}
      {!loading && viewMode === 'calendar' && (
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
          {/* Calendar Header */}
          <div className="flex items-center justify-between px-4 py-3 bg-black/30 border-b border-gray-800">
            <button
              onClick={() => navigateWeek('prev')}
              className="p-2 hover:bg-gray-700 rounded-lg transition-colors"
            >
              <ChevronLeftIcon className="w-5 h-5 text-gray-400" />
            </button>
            <span className="font-semibold text-white">
              {formatDateFull(weekDays[0])} - {formatDateFull(weekDays[6])}
            </span>
            <button
              onClick={() => navigateWeek('next')}
              className="p-2 hover:bg-gray-700 rounded-lg transition-colors"
            >
              <ChevronRightIcon className="w-5 h-5 text-gray-400" />
            </button>
          </div>

          {/* Calendar Grid */}
          <div className="grid grid-cols-7 divide-x divide-gray-800">
            {weekDays.map(day => (
              <div key={day.toISOString()} className="min-h-[200px]">
                <div className={`px-2 py-2 text-center border-b border-gray-800 ${
                  isToday(day) ? 'bg-red-900/30' : 'bg-black/30'
                }`}>
                  <div className="text-xs text-gray-500 uppercase">
                    {day.toLocaleDateString([], { weekday: 'short' })}
                  </div>
                  <div className={`text-lg font-semibold ${isToday(day) ? 'text-red-400' : 'text-white'}`}>
                    {day.getDate()}
                  </div>
                </div>
                <div className="p-1 space-y-1">
                  {getRecordingsForDate(day).map(recording => (
                    <div
                      key={recording.id}
                      className={`p-1.5 rounded text-xs ${getStatusColor(recording.status)} cursor-pointer hover:opacity-80 transition-opacity`}
                      title={`${recording.eventTitle}\n${formatTime(recording.scheduledStart)} - ${formatTime(recording.scheduledEnd)}\n${recording.channelName}`}
                    >
                      <div className="font-medium truncate">{formatTime(recording.scheduledStart)}</div>
                      <div className="truncate text-gray-300">{recording.eventTitle}</div>
                    </div>
                  ))}
                  {getRecordingsForDate(day).length === 0 && (
                    <div className="text-center text-gray-600 text-xs py-4">
                      No recordings
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Info Note */}
      <div className="mt-8 p-4 bg-blue-900/20 border border-blue-900/30 rounded-lg">
        <div className="flex items-start gap-3">
          <ExclamationTriangleIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-gray-300">
            <p className="font-medium text-white mb-1">How DVR Scheduling Works</p>
            <ul className="list-disc list-inside space-y-1 text-gray-400">
              <li>Recordings are automatically scheduled when events from tracked leagues have channel mappings</li>
              <li>Map channels to leagues in the <span className="text-blue-400">Channels</span> page to enable automatic recording</li>
              <li>Manual recordings can be created from the <span className="text-blue-400">Recordings</span> page</li>
              <li>Pre/post padding is applied based on DVR settings</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
