import { useEvents } from '../api/hooks';
import { ChevronLeftIcon, ChevronRightIcon, TvIcon, FunnelIcon, CalendarDaysIcon } from '@heroicons/react/24/outline';
import { useState, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import type { Event, Image } from '../types';
import { useTimezone } from '../hooks/useTimezone';
import { convertToTimezone, getDateInTimezone } from '../utils/timezone';

// Helper function to get image URL from either Image object or string
const getImageUrl = (images: Image[] | string[] | undefined): string | undefined => {
  if (!images || images.length === 0) return undefined;
  const first = images[0];
  return typeof first === 'string' ? first : first.remoteUrl;
};

// Sport color mappings (matching Sonarr/Radarr style)
// Note: Fighting uses pink/fuchsia to differentiate from Today (amber) and Live (red)
const SPORT_COLORS = {
  Fighting: { bg: 'bg-fuchsia-900/30', border: 'border-fuchsia-700', text: 'text-fuchsia-400', badge: 'bg-fuchsia-600' },
  Soccer: { bg: 'bg-green-900/30', border: 'border-green-700', text: 'text-green-400', badge: 'bg-green-600' },
  Basketball: { bg: 'bg-orange-900/30', border: 'border-orange-700', text: 'text-orange-400', badge: 'bg-orange-600' },
  Football: { bg: 'bg-blue-900/30', border: 'border-blue-700', text: 'text-blue-400', badge: 'bg-blue-600' },
  Baseball: { bg: 'bg-indigo-900/30', border: 'border-indigo-700', text: 'text-indigo-400', badge: 'bg-indigo-600' },
  Hockey: { bg: 'bg-cyan-900/30', border: 'border-cyan-700', text: 'text-cyan-400', badge: 'bg-cyan-600' },
  Tennis: { bg: 'bg-yellow-900/30', border: 'border-yellow-700', text: 'text-yellow-400', badge: 'bg-yellow-600' },
  Golf: { bg: 'bg-lime-900/30', border: 'border-lime-700', text: 'text-lime-400', badge: 'bg-lime-600' },
  Racing: { bg: 'bg-purple-900/30', border: 'border-purple-700', text: 'text-purple-400', badge: 'bg-purple-600' },
  default: { bg: 'bg-gray-900/30', border: 'border-gray-700', text: 'text-gray-400', badge: 'bg-gray-600' }
};

const getSportColors = (sport: string) => {
  return SPORT_COLORS[sport as keyof typeof SPORT_COLORS] || SPORT_COLORS.default;
};

// Check if an event is currently live based on time
// Events are considered live if current time is within 4 hours after start time
// (most sporting events last 2-4 hours)
const isEventLive = (event: Event, timezone: string | null): boolean => {
  // If status is explicitly "Live", it's live
  if (event.status === 'Live') return true;

  // Check if event is currently happening based on time
  const now = new Date();
  const eventDate = convertToTimezone(event.eventDate, timezone);

  // Event has started and is within 4 hours of start time
  const eventEndEstimate = new Date(eventDate.getTime() + 4 * 60 * 60 * 1000); // 4 hours after start

  return now >= eventDate && now <= eventEndEstimate;
};

export default function CalendarPage() {
  const { data: events, isLoading, error } = useEvents();
  const { timezone } = useTimezone();
  const navigate = useNavigate();
  const [currentWeekOffset, setCurrentWeekOffset] = useState(0);
  const [filterSport, setFilterSport] = useState<string>('all');
  const [filterTvOnly, setFilterTvOnly] = useState(false);
  const [showFilters, setShowFilters] = useState(false);
  const dateInputRef = useRef<HTMLInputElement>(null);

  // Get unique sports from events for filter
  const uniqueSports = Array.from(new Set(events?.map(e => e.sport).filter(Boolean))) as string[];

  // Get "today" in the user's configured timezone
  const getTodayInTimezone = () => {
    const now = new Date();

    // If timezone is set, get the current date in that timezone
    if (timezone) {
      const options: Intl.DateTimeFormatOptions = {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        timeZone: timezone,
      };
      const formatter = new Intl.DateTimeFormat('en-US', options);
      const parts = formatter.formatToParts(now);
      const getPart = (type: Intl.DateTimeFormatPartTypes) =>
        parts.find(p => p.type === type)?.value || '0';

      const year = parseInt(getPart('year'));
      const month = parseInt(getPart('month')) - 1;
      const day = parseInt(getPart('day'));
      return new Date(year, month, day, 0, 0, 0, 0);
    }

    return new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0, 0);
  };

  // Get the start of the current week (Sunday) in user's timezone
  const getWeekStart = (offset: number = 0) => {
    const today = getTodayInTimezone();
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

  // Filter events for a specific day (respecting user's timezone)
  const getEventsForDay = (date: Date, allEvents: Event[] | undefined) => {
    if (!allEvents) return [];

    // Get the date string for comparison (in user's timezone)
    const targetDateStr = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;

    return allEvents.filter(event => {
      if (!event.monitored) return false; // Only show monitored events

      // Apply sport filter
      if (filterSport !== 'all' && event.sport !== filterSport) return false;

      // Apply TV availability filter
      if (filterTvOnly && !event.broadcast) return false;

      // Convert the event date from UTC to user's timezone and compare
      const eventDateStr = getDateInTimezone(event.eventDate, timezone);
      return eventDateStr === targetDateStr;
    });
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
    const today = getTodayInTimezone();

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

  const isToday = (date: Date) => {
    const today = getTodayInTimezone();
    return date.getDate() === today.getDate() &&
           date.getMonth() === today.getMonth() &&
           date.getFullYear() === today.getFullYear();
  };

  if (isLoading) {
    return (
      <div className="p-8">
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="bg-red-900 border border-red-700 text-red-100 px-4 py-3 rounded">
          <p className="font-bold">Error loading events</p>
          <p className="text-sm">{(error as Error).message}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-8">
      <div className="mx-auto">
        {/* Header */}
        <div className="mb-4 md:mb-6">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-4">
            <div>
              <h1 className="text-2xl md:text-3xl font-bold text-white mb-1 md:mb-2">Calendar</h1>
              <p className="text-sm md:text-base text-gray-400">
                View your monitored sports events
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

          {/* Filters */}
          <div className="flex flex-wrap items-center gap-2 md:gap-4">
            <button
              onClick={() => setShowFilters(!showFilters)}
              className="flex items-center gap-2 px-3 md:px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors text-sm md:text-base"
            >
              <FunnelIcon className="w-4 md:w-5 h-4 md:h-5" />
              Filters
              {(filterSport !== 'all' || filterTvOnly) && (
                <span className="px-1.5 md:px-2 py-0.5 bg-red-600 text-white text-xs rounded-full">
                  {(filterSport !== 'all' ? 1 : 0) + (filterTvOnly ? 1 : 0)}
                </span>
              )}
            </button>

            {showFilters && (
              <div className="flex flex-wrap items-center gap-2 md:gap-4 animate-fade-in w-full md:w-auto">
                {/* Sport Filter */}
                <select
                  value={filterSport}
                  onChange={(e) => setFilterSport(e.target.value)}
                  className="px-2 md:px-3 py-2 bg-gray-800 border border-gray-700 text-white rounded-lg focus:outline-none focus:border-red-600 text-sm md:text-base"
                >
                  <option value="all">All Sports</option>
                  {uniqueSports.map(sport => (
                    <option key={sport} value={sport}>{sport}</option>
                  ))}
                </select>

                {/* TV Only Filter */}
                <label className="flex items-center gap-2 cursor-pointer text-sm md:text-base">
                  <input
                    type="checkbox"
                    checked={filterTvOnly}
                    onChange={(e) => setFilterTvOnly(e.target.checked)}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                  />
                  <TvIcon className="w-4 md:w-5 h-4 md:h-5 text-green-400" />
                  <span className="text-white">TV Only</span>
                </label>

                {(filterSport !== 'all' || filterTvOnly) && (
                  <button
                    onClick={() => {
                      setFilterSport('all');
                      setFilterTvOnly(false);
                    }}
                    className="text-red-400 hover:text-red-300 text-xs md:text-sm"
                  >
                    Clear
                  </button>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Calendar Grid - Stacked on mobile, 7 columns on desktop */}
        <div className="grid grid-cols-1 md:grid-cols-7 gap-2 md:gap-2">
          {weekDays.map((day, index) => {
            const dayEvents = getEventsForDay(day, events);
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

                {/* Events for the day - grid layout with up to 3 columns based on event count */}
                <div className={`p-1.5 md:p-2 grid gap-1.5 md:gap-2 ${
                  dayEvents.length === 1 ? 'grid-cols-1' :
                  dayEvents.length === 2 ? 'grid-cols-2' :
                  dayEvents.length >= 3 ? 'grid-cols-3' : 'grid-cols-1'
                }`}>
                  {dayEvents.length > 0 ? (
                    dayEvents.map(event => {
                      const sportColors = getSportColors(event.sport || 'default');
                      const isLive = isEventLive(event, timezone);
                      const isMultiColumn = dayEvents.length >= 2;

                      return (
                        <div
                          key={event.id}
                          onClick={() => event.leagueId && navigate(`/leagues/${event.leagueId}`)}
                          className={`${sportColors.bg} hover:opacity-80 border ${isLive ? 'border-red-500 ring-2 ring-red-500/50 animate-pulse' : sportColors.border} rounded p-1.5 transition-all cursor-pointer group relative`}
                          title={`${event.title}${event.venue ? `\n${event.venue}` : ''}${event.broadcast ? `\n📺 ${event.broadcast}` : ''}`}
                        >
                          {/* Compact layout for multi-column view */}
                          <div className="min-w-0">
                            {/* Sport Badge - smaller in multi-column */}
                            {event.sport && (
                              <span className={`inline-block px-1 py-0.5 ${sportColors.badge} text-white text-[10px] rounded mb-0.5`}>
                                {event.sport}
                              </span>
                            )}

                            {/* Title - truncate more aggressively in multi-column */}
                            <p className={`text-[11px] font-semibold text-white group-hover:text-gray-200 transition-colors ${isMultiColumn ? 'line-clamp-2' : 'line-clamp-2'}`}>
                              {event.title}
                            </p>

                            {/* Status indicators - compact row */}
                            <div className="flex items-center gap-1 mt-0.5 flex-wrap">
                              {isLive && (
                                <span className="px-1 py-0.5 bg-red-600 text-white text-[9px] rounded animate-pulse font-bold">
                                  LIVE
                                </span>
                              )}
                              {event.hasFile && (
                                <span className="px-1 py-0.5 bg-green-600/30 text-green-400 text-[9px] rounded">
                                  ✓
                                </span>
                              )}
                              {event.broadcast && !isMultiColumn && (
                                <span className="flex items-center gap-0.5 text-[9px] text-green-400">
                                  <TvIcon className="w-2.5 h-2.5" />
                                </span>
                              )}
                            </div>
                          </div>
                        </div>
                      );
                    })
                  ) : (
                    <div className="text-center py-4 text-gray-600 text-xs">
                      No events
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        {/* Legend */}
        <div className="mt-6">
          <h3 className="text-sm font-semibold text-gray-400 mb-3">Legend</h3>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <div className="w-3 h-3 bg-amber-500 rounded"></div>
              <span>Today</span>
            </div>
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <div className="w-3 h-3 bg-green-600 rounded"></div>
              <span>Downloaded</span>
            </div>
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <TvIcon className="w-3 h-3 text-green-400" />
              <span>TV Schedule Available</span>
            </div>
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <div className="w-3 h-3 bg-red-600 rounded ring-2 ring-red-500/50 animate-pulse"></div>
              <span>Live Now</span>
            </div>
          </div>

          {/* Sport Colors */}
          <div className="mt-4">
            <h4 className="text-xs font-semibold text-gray-500 mb-2">Sport Colors</h4>
            <div className="flex flex-wrap gap-2">
              {Object.entries(SPORT_COLORS).filter(([key]) => key !== 'default').map(([sport, colors]) => (
                <div key={sport} className="flex items-center gap-2">
                  <div className={`w-3 h-3 ${colors.badge} rounded`}></div>
                  <span className="text-xs text-gray-500">{sport}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
