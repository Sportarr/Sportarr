import { useState, useEffect, useMemo, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import {
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
  FunnelIcon,
  CalendarDaysIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import { useTimezone } from '../../hooks/useTimezone';
import { getDateInTimezone, formatTimeInTimezone, formatDateInTimezone } from '../../utils/timezone';

// Types
type RecordingStatus = 'Scheduled' | 'Recording' | 'Completed' | 'Failed' | 'Cancelled' | 'Importing' | 'Imported';

interface DvrRecording {
  id: number;
  eventId?: number;
  eventTitle: string;
  channelId: number;
  channelName: string;
  leagueId?: number;
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

// Status color mappings for recording status
const STATUS_COLORS = {
  Scheduled: { bg: 'bg-blue-900/30', border: 'border-blue-700', text: 'text-blue-400', badge: 'bg-blue-600' },
  Recording: { bg: 'bg-red-900/30', border: 'border-red-700', text: 'text-red-400', badge: 'bg-red-600' },
  Completed: { bg: 'bg-green-900/30', border: 'border-green-700', text: 'text-green-400', badge: 'bg-green-600' },
  Failed: { bg: 'bg-red-900/30', border: 'border-red-700', text: 'text-red-400', badge: 'bg-red-600' },
  Cancelled: { bg: 'bg-gray-900/30', border: 'border-gray-700', text: 'text-gray-400', badge: 'bg-gray-600' },
  Importing: { bg: 'bg-purple-900/30', border: 'border-purple-700', text: 'text-purple-400', badge: 'bg-purple-600' },
  Imported: { bg: 'bg-green-900/30', border: 'border-green-700', text: 'text-green-400', badge: 'bg-green-600' },
  default: { bg: 'bg-gray-900/30', border: 'border-gray-700', text: 'text-gray-400', badge: 'bg-gray-600' }
};

const getStatusColors = (status: RecordingStatus) => {
  return STATUS_COLORS[status] || STATUS_COLORS.default;
};

export default function DvrSchedulePage() {
  const [recordings, setRecordings] = useState<DvrRecording[]>([]);
  const [upcomingEvents, setUpcomingEvents] = useState<SportEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [viewMode, setViewMode] = useState<'list' | 'calendar'>('calendar');
  const [currentWeekOffset, setCurrentWeekOffset] = useState(0);
  const [filterStatus, setFilterStatus] = useState<string>('all');
  const [showFilters, setShowFilters] = useState(false);
  const { timezone } = useTimezone();
  const navigate = useNavigate();
  const dateInputRef = useRef<HTMLInputElement>(null);

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

  // Get the start of the current week (Sunday)
  const getWeekStart = (offset: number = 0) => {
    const today = new Date();
    const dayOfWeek = today.getDay(); // 0 = Sunday, 6 = Saturday
    const weekStart = new Date(today);
    weekStart.setDate(today.getDate() - dayOfWeek + (offset * 7));
    weekStart.setHours(0, 0, 0, 0);
    return weekStart;
  };

  // Get array of 7 days for the week (Sunday to Saturday)
  const getWeekDays = (offset: number = 0) => {
    const weekStart = getWeekStart(offset);
    const days = [];
    for (let i = 0; i < 7; i++) {
      const day = new Date(weekStart);
      day.setDate(weekStart.getDate() + i);
      days.push(day);
    }
    return days;
  };

  const weekDays = getWeekDays(currentWeekOffset);
  const weekStart = weekDays[0];
  const weekEnd = weekDays[6];

  const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  const monthNames = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

  const formatWeekRange = () => {
    const startMonth = monthNames[weekStart.getMonth()];
    const endMonth = monthNames[weekEnd.getMonth()];
    const startDay = weekStart.getDate();
    const endDay = weekEnd.getDate();
    const year = weekEnd.getFullYear();

    if (startMonth === endMonth) {
      return `${startMonth} ${startDay} - ${endDay}, ${year}`;
    }
    return `${startMonth} ${startDay} - ${endMonth} ${endDay}, ${year}`;
  };

  // Navigate to a specific date
  const goToDate = (dateString: string) => {
    const selectedDate = new Date(dateString + 'T00:00:00');
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    // Calculate the week offset from today
    const todayDayOfWeek = today.getDay();
    const todayWeekStart = new Date(today);
    todayWeekStart.setDate(today.getDate() - todayDayOfWeek);

    const selectedDayOfWeek = selectedDate.getDay();
    const selectedWeekStart = new Date(selectedDate);
    selectedWeekStart.setDate(selectedDate.getDate() - selectedDayOfWeek);

    const diffTime = selectedWeekStart.getTime() - todayWeekStart.getTime();
    const diffWeeks = Math.round(diffTime / (7 * 24 * 60 * 60 * 1000));

    setCurrentWeekOffset(diffWeeks);
  };

  // Group recordings by date (in user's timezone)
  const recordingsByDate = useMemo(() => {
    const grouped: Record<string, DvrRecording[]> = {};
    recordings.forEach(recording => {
      // Apply status filter
      if (filterStatus !== 'all' && recording.status !== filterStatus) return;

      // Get the date in user's timezone
      const date = getDateInTimezone(recording.scheduledStart, timezone);
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
  }, [recordings, filterStatus, timezone]);

  const formatTime = (dateString: string) => {
    return formatTimeInTimezone(dateString, timezone, { hour: '2-digit', minute: '2-digit' });
  };

  // Format a date string that's ALREADY in the user's timezone (YYYY-MM-DD format)
  // Do NOT use formatDateInTimezone here - that would double-convert!
  const formatDate = (dateString: string) => {
    // Parse the date as local time (not UTC) since it's already in user's timezone
    const [year, month, day] = dateString.split('-').map(Number);
    const date = new Date(year, month - 1, day);
    return date.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
  };

  const getStatusIcon = (status: RecordingStatus) => {
    switch (status) {
      case 'Scheduled':
        return <ClockIcon className="w-3 h-3" />;
      case 'Recording':
        return <PlayCircleIcon className="w-3 h-3" />;
      case 'Completed':
        return <CheckCircleIcon className="w-3 h-3" />;
      case 'Failed':
        return <XCircleIcon className="w-3 h-3" />;
      case 'Cancelled':
        return <XCircleIcon className="w-3 h-3" />;
      case 'Importing':
        return <ArrowPathIcon className="w-3 h-3 animate-spin" />;
      case 'Imported':
        return <CheckCircleIcon className="w-3 h-3" />;
      default:
        return <ClockIcon className="w-3 h-3" />;
    }
  };

  const getDurationMinutes = (start: string, end: string) => {
    const startDate = new Date(start);
    const endDate = new Date(end);
    return Math.round((endDate.getTime() - startDate.getTime()) / 1000 / 60);
  };

  const isToday = (date: Date) => {
    const today = new Date();
    return date.getDate() === today.getDate() &&
           date.getMonth() === today.getMonth() &&
           date.getFullYear() === today.getFullYear();
  };

  // Get recordings for a specific date (for calendar view, respecting user's timezone)
  const getRecordingsForDate = (date: Date) => {
    // Format the calendar date as YYYY-MM-DD to match the keys in recordingsByDate
    const dateStr = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    return recordingsByDate[dateStr] || [];
  };

  // Get unique statuses for filter
  const uniqueStatuses = Array.from(new Set(recordings.map(r => r.status))) as RecordingStatus[];

  return (
    <div className="p-4 md:p-8">
      <div className="mx-auto">
        {/* Header */}
        <div className="mb-4 md:mb-6">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-4">
            <div>
              <h1 className="text-2xl md:text-3xl font-bold text-white mb-1 md:mb-2">DVR Schedule</h1>
              <p className="text-sm md:text-base text-gray-400">
                View and manage your scheduled recordings
              </p>
            </div>

            {/* Week Navigation */}
            <div className="flex items-center justify-center gap-2 md:gap-3">
              <button
                onClick={() => setCurrentWeekOffset(currentWeekOffset - 1)}
                className="p-2 hover:bg-red-900/20 rounded-lg transition-colors"
                title="Previous week"
              >
                <ChevronLeftIcon className="w-5 md:w-6 h-5 md:h-6 text-gray-400 hover:text-white" />
              </button>

              {/* Fixed width container for date range */}
              <div className="text-center w-[180px] md:w-[280px]">
                <p className="text-sm md:text-lg font-semibold text-white truncate">{formatWeekRange()}</p>
                {currentWeekOffset === 0 && (
                  <p className="text-xs md:text-sm text-red-400">Current Week</p>
                )}
              </div>

              <button
                onClick={() => setCurrentWeekOffset(currentWeekOffset + 1)}
                className="p-2 hover:bg-red-900/20 rounded-lg transition-colors"
                title="Next week"
              >
                <ChevronRightIcon className="w-5 md:w-6 h-5 md:h-6 text-gray-400 hover:text-white" />
              </button>

              {/* Date Picker */}
              <div className="relative">
                <input
                  ref={dateInputRef}
                  type="date"
                  className="absolute opacity-0 w-0 h-0"
                  onChange={(e) => e.target.value && goToDate(e.target.value)}
                />
                <button
                  onClick={() => dateInputRef.current?.showPicker()}
                  className="p-2 hover:bg-red-900/20 rounded-lg transition-colors"
                  title="Go to date"
                >
                  <CalendarDaysIcon className="w-5 md:w-6 h-5 md:h-6 text-gray-400 hover:text-white" />
                </button>
              </div>

              {/* Today Button */}
              {currentWeekOffset !== 0 && (
                <button
                  onClick={() => setCurrentWeekOffset(0)}
                  className="px-2 md:px-3 py-1 text-xs md:text-sm bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  title="Go to current week"
                >
                  Today
                </button>
              )}
            </div>
          </div>

          {/* Filters & View Toggle */}
          <div className="flex flex-wrap items-center justify-between gap-2 md:gap-4">
            <div className="flex flex-wrap items-center gap-2 md:gap-4">
              <button
                onClick={() => setShowFilters(!showFilters)}
                className="flex items-center gap-2 px-3 md:px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors text-sm md:text-base"
              >
                <FunnelIcon className="w-4 md:w-5 h-4 md:h-5" />
                Filters
                {filterStatus !== 'all' && (
                  <span className="px-1.5 md:px-2 py-0.5 bg-red-600 text-white text-xs rounded-full">
                    1
                  </span>
                )}
              </button>

              {showFilters && (
                <div className="flex flex-wrap items-center gap-2 md:gap-4 animate-fade-in w-full md:w-auto">
                  {/* Status Filter */}
                  <select
                    value={filterStatus}
                    onChange={(e) => setFilterStatus(e.target.value)}
                    className="px-2 md:px-3 py-2 bg-gray-800 border border-gray-700 text-white rounded-lg focus:outline-none focus:border-red-600 text-sm md:text-base"
                  >
                    <option value="all">All Statuses</option>
                    {uniqueStatuses.map(status => (
                      <option key={status} value={status}>{status}</option>
                    ))}
                  </select>

                  {filterStatus !== 'all' && (
                    <button
                      onClick={() => setFilterStatus('all')}
                      className="text-red-400 hover:text-red-300 text-xs md:text-sm"
                    >
                      Clear
                    </button>
                  )}
                </div>
              )}
            </div>

            <div className="flex items-center gap-3">
              {/* View Toggle */}
              <div className="flex bg-gray-800 rounded-lg p-1">
                <button
                  onClick={() => setViewMode('calendar')}
                  className={`px-3 md:px-4 py-1.5 md:py-2 rounded-lg text-xs md:text-sm font-medium transition-colors ${
                    viewMode === 'calendar' ? 'bg-red-600 text-white' : 'text-gray-400 hover:text-white'
                  }`}
                >
                  Calendar
                </button>
                <button
                  onClick={() => setViewMode('list')}
                  className={`px-3 md:px-4 py-1.5 md:py-2 rounded-lg text-xs md:text-sm font-medium transition-colors ${
                    viewMode === 'list' ? 'bg-red-600 text-white' : 'text-gray-400 hover:text-white'
                  }`}
                >
                  List
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
        </div>

        {/* Loading State */}
        {loading && (
          <div className="flex items-center justify-center h-64">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
          </div>
        )}

        {/* Calendar View - Stacked on mobile, 7 columns on desktop */}
        {!loading && viewMode === 'calendar' && (
          <div className="grid grid-cols-1 md:grid-cols-7 gap-2 md:gap-2">
            {weekDays.map((day, index) => {
              const dayRecordings = getRecordingsForDate(day);
              const today = isToday(day);

              return (
                <div
                  key={day.toISOString()}
                  className={`bg-gradient-to-br from-gray-900 to-black border rounded-lg overflow-hidden min-h-[100px] md:min-h-[200px] ${
                    today ? 'border-amber-500 shadow-lg shadow-amber-900/30' : 'border-red-900/30'
                  }`}
                >
                  {/* Day Header */}
                  <div className={`px-2 md:px-3 py-1.5 md:py-2 border-b ${today ? 'bg-amber-950/40 border-amber-900/40' : 'bg-gray-800/30 border-red-900/20'}`}>
                    <div className="flex md:block items-center gap-2">
                      <div className="text-xs text-gray-400 font-medium">
                        {dayNames[index]}
                      </div>
                      <div className={`text-base md:text-lg font-bold ${today ? 'text-amber-400' : 'text-white'}`}>
                        {day.getDate()}
                      </div>
                    </div>
                  </div>

                  {/* Recordings for the day - grid layout with up to 3 columns based on recording count */}
                  <div className={`p-1.5 md:p-2 grid gap-1.5 md:gap-2 ${
                    dayRecordings.length === 1 ? 'grid-cols-1' :
                    dayRecordings.length === 2 ? 'grid-cols-2' :
                    dayRecordings.length >= 3 ? 'grid-cols-3' : 'grid-cols-1'
                  }`}>
                    {dayRecordings.length > 0 ? (
                      dayRecordings.map(recording => {
                        const statusColors = getStatusColors(recording.status);
                        const isRecording = recording.status === 'Recording';
                        const isMultiColumn = dayRecordings.length >= 2;

                        return (
                          <div
                            key={recording.id}
                            onClick={() => recording.leagueId && navigate(`/leagues/${recording.leagueId}`)}
                            className={`${statusColors.bg} hover:opacity-80 border ${isRecording ? 'border-red-500 ring-2 ring-red-500/50 animate-pulse' : statusColors.border} rounded p-1.5 transition-all cursor-pointer group relative`}
                            title={`${recording.eventTitle}\n${formatTime(recording.scheduledStart)} - ${formatTime(recording.scheduledEnd)}\n📡 ${recording.channelName}`}
                          >
                            {/* Compact layout */}
                            <div className="min-w-0">
                              {/* Status Badge - compact */}
                              <span className={`inline-flex items-center gap-0.5 px-1 py-0.5 ${statusColors.badge} text-white text-[10px] rounded mb-0.5`}>
                                {getStatusIcon(recording.status)}
                                {recording.status}
                              </span>

                              {/* Title */}
                              <p className="text-[11px] font-semibold text-white line-clamp-2 group-hover:text-gray-200 transition-colors">
                                {recording.eventTitle}
                              </p>

                              {/* Time & Channel - compact */}
                              <div className="flex items-center gap-1 mt-0.5 text-[9px] text-gray-400">
                                <ClockIcon className="w-2.5 h-2.5" />
                                <span>{formatTime(recording.scheduledStart)}</span>
                                {!isMultiColumn && (
                                  <>
                                    <SignalIcon className="w-2.5 h-2.5 ml-1" />
                                    <span className="truncate">{recording.channelName}</span>
                                  </>
                                )}
                              </div>
                            </div>
                          </div>
                        );
                      })
                    ) : (
                      <div className="text-center py-4 text-gray-600 text-xs">
                        No recordings
                      </div>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* List View */}
        {!loading && viewMode === 'list' && (
          <div className="space-y-4">
            {Object.entries(recordingsByDate).length === 0 ? (
              <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-12 text-center">
                <CalendarDaysIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                <h3 className="text-xl font-semibold text-white mb-2">No Scheduled Recordings</h3>
                <p className="text-gray-400 max-w-md mx-auto">
                  There are no upcoming recordings scheduled. Recordings are automatically created when events
                  have channel mappings, or you can create manual recordings from the Recordings page.
                </p>
              </div>
            ) : (
              Object.entries(recordingsByDate)
                .sort(([a], [b]) => a.localeCompare(b))
                .map(([date, dateRecordings]) => (
                  <div key={date} className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
                    <div className={`px-4 py-3 border-b flex items-center gap-2 ${
                      isToday(new Date(date)) ? 'bg-amber-950/40 border-amber-900/40' : 'bg-gray-800/30 border-red-900/20'
                    }`}>
                      <CalendarDaysIcon className={`w-5 h-5 ${isToday(new Date(date)) ? 'text-amber-400' : 'text-red-400'}`} />
                      <span className={`font-semibold ${isToday(new Date(date)) ? 'text-amber-400' : 'text-white'}`}>
                        {formatDate(date)}
                      </span>
                      {isToday(new Date(date)) && (
                        <span className="px-2 py-0.5 text-xs bg-amber-600 text-white rounded">Today</span>
                      )}
                      <span className="text-gray-500 text-sm">({dateRecordings.length} recording{dateRecordings.length !== 1 ? 's' : ''})</span>
                    </div>
                    <div className="divide-y divide-gray-800/50">
                      {dateRecordings.map(recording => {
                        const statusColors = getStatusColors(recording.status);
                        return (
                          <div
                            key={recording.id}
                            onClick={() => recording.leagueId && navigate(`/leagues/${recording.leagueId}`)}
                            className={`p-4 hover:bg-gray-800/30 transition-colors ${recording.leagueId ? 'cursor-pointer' : ''}`}
                          >
                            <div className="flex items-start justify-between gap-4">
                              <div className="flex-1 min-w-0">
                                <div className="flex items-center gap-3 mb-1">
                                  <span className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium ${statusColors.badge} text-white`}>
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
                                <div className="flex flex-wrap items-center gap-4 mt-2 text-sm text-gray-400">
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
                        );
                      })}
                    </div>
                  </div>
                ))
            )}
          </div>
        )}

        {/* Legend */}
        <div className="mt-6">
          <h3 className="text-sm font-semibold text-gray-400 mb-3">Legend</h3>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <div className="w-3 h-3 bg-amber-500 rounded"></div>
              <span>Today</span>
            </div>
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <div className="w-3 h-3 bg-blue-600 rounded"></div>
              <span>Scheduled</span>
            </div>
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <div className="w-3 h-3 bg-red-600 rounded ring-2 ring-red-500/50 animate-pulse"></div>
              <span>Recording</span>
            </div>
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <div className="w-3 h-3 bg-green-600 rounded"></div>
              <span>Completed</span>
            </div>
          </div>

          {/* Status Colors */}
          <div className="mt-4">
            <h4 className="text-xs font-semibold text-gray-500 mb-2">Status Colors</h4>
            <div className="flex flex-wrap gap-2">
              {Object.entries(STATUS_COLORS).filter(([key]) => key !== 'default').map(([status, colors]) => (
                <div key={status} className="flex items-center gap-2">
                  <div className={`w-3 h-3 ${colors.badge} rounded`}></div>
                  <span className="text-xs text-gray-500">{status}</span>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Info Note */}
        <div className="mt-6 p-4 bg-blue-900/20 border border-blue-900/30 rounded-lg">
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
    </div>
  );
}
