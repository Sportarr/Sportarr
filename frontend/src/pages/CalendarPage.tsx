import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ChevronLeftIcon, ChevronRightIcon, TvIcon, FunnelIcon, CalendarDaysIcon } from '@heroicons/react/24/outline';
import { useNavigate } from 'react-router-dom';
import { useEvents } from '../api/hooks';
import type { Event } from '../types';
import { useSettings } from '../hooks/useSettings';
import { useTimezone } from '../hooks/useTimezone';
import {
  addDays,
  addMonths,
  endOfWeek,
  formatDateInputValue,
  formatMonthLabel,
  formatWeekLabel,
  getAgendaRange,
  getCalendarWeeks,
  getWeekDays,
  getWeekdayNames,
  startOfWeek,
} from '../utils/dateUtils';
import { convertToTimezone, formatTimeInTimezone, getDateInTimezone, getTodayInTimezone } from '../utils/timezone';

type CalendarView = 'month' | 'week' | 'agenda';
type FirstDayOfWeek = 'sunday' | 'monday';

interface CalendarUISettings {
  firstDayOfWeek?: string;
}

const TOOLBAR_GROUP_CLASS = 'inline-flex min-w-max items-center space-x-1 rounded-lg bg-gray-900 p-1';
const TOOLBAR_BUTTON_BASE_CLASS = 'rounded-md px-3 py-1.5 text-sm transition-all whitespace-nowrap';
const TOOLBAR_BUTTON_INACTIVE_CLASS = 'text-gray-400 hover:bg-gray-800 hover:text-white';
const TOOLBAR_BUTTON_ACTIVE_CLASS = 'bg-red-600 text-white';

// Sport color mappings (matching Sonarr/Radarr style)
// Note: Fighting now uses rose while Motorsport uses fuchsia so they stay distinct from Today (amber) and Live (red)
const SPORT_COLORS = {
  Fighting: { surface: 'bg-rose-900/35', border: 'border-rose-500/70', accent: 'bg-rose-500' },
  Soccer: { surface: 'bg-emerald-900/35', border: 'border-emerald-500/70', accent: 'bg-emerald-500' },
  Basketball: { surface: 'bg-amber-900/35', border: 'border-amber-500/70', accent: 'bg-amber-500' },
  Football: { surface: 'bg-blue-950/35', border: 'border-blue-600/70', accent: 'bg-blue-600' },
  Baseball: { surface: 'bg-violet-900/35', border: 'border-violet-500/70', accent: 'bg-violet-500' },
  Hockey: { surface: 'bg-cyan-900/35', border: 'border-cyan-500/70', accent: 'bg-cyan-500' },
  Tennis: { surface: 'bg-yellow-900/35', border: 'border-yellow-500/70', accent: 'bg-yellow-500' },
  Golf: { surface: 'bg-lime-900/35', border: 'border-lime-500/70', accent: 'bg-lime-500' },
  Motorsport: { surface: 'bg-fuchsia-900/35', border: 'border-fuchsia-500/70', accent: 'bg-fuchsia-500' },
  Other: { surface: 'bg-slate-800/85', border: 'border-slate-500/70', accent: 'bg-slate-500' }
} as const;

type SportColorKey = keyof typeof SPORT_COLORS;

const SPORT_TYPE_TO_COLOR: Record<string, SportColorKey> = {
  hockey: 'Hockey',
  'ice hockey': 'Hockey',
  football: 'Football',
  'american football': 'Football',
  baseball: 'Baseball',
  basketball: 'Basketball',
  soccer: 'Soccer',
  tennis: 'Tennis',
  golf: 'Golf',
  fighting: 'Fighting',
  boxing: 'Fighting',
  mma: 'Fighting',
  'mixed martial arts': 'Fighting',
  kickboxing: 'Fighting',
  'muay thai': 'Fighting',
  wrestling: 'Fighting',
  motorsport: 'Motorsport',
  racing: 'Motorsport',
};

const getSportCategory = (sport: string | undefined): SportColorKey => {
  if (!sport) return 'Other';

  const trimmed = sport.trim();
  const sportKey = trimmed.toLowerCase();
  return SPORT_TYPE_TO_COLOR[sportKey] || 'Other';
};

const getSportDisplayLabel = (sport: string | undefined) => {
  if (!sport) return 'Other';

  const trimmed = sport.trim();
  const category = getSportCategory(trimmed);

  if (category !== 'Other') {
    return category;
  }

  return trimmed;
};

const getSportColors = (sport: string) => {
  return SPORT_COLORS[getSportCategory(sport)];
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

const getAdjacentDate = (date: Date, view: CalendarView, direction: -1 | 1) => {
  switch (view) {
    case 'week':
      return addDays(date, direction * 7);
    case 'agenda':
    case 'month':
    default:
      return addMonths(date, direction);
  }
};

function EventCard({
  event,
  timezone,
  onClick,
}: {
  event: Event;
  timezone: string | null;
  onClick: () => void;
}) {
  const sportColors = getSportColors(event.sport || 'default');
  const isLive = isEventLive(event, timezone);
  const timeLabel = formatTimeInTimezone(event.eventDate, timezone, {
    hour: 'numeric',
    minute: '2-digit',
  });
  const displaySport = getSportDisplayLabel(event.sport);

  return (
    <button
      type="button"
      onClick={onClick}
      data-testid={`calendar-event-${event.id}`}
      className={`${sportColors.surface} ${isLive ? 'border-red-500 ring-2 ring-red-500/40 animate-pulse' : sportColors.border} relative block w-full overflow-hidden rounded-sm border px-1.5 pb-1 pt-[20.5px] text-left shadow-sm transition-all hover:opacity-95`}
      title={`${event.title}${event.venue ? `\n${event.venue}` : ''}${event.broadcast ? `\nTV: ${event.broadcast}` : ''}`}
    >
      {/* Sport Badge */}
      {displaySport && (
        <span
          data-testid={`calendar-event-sport-${event.id}`}
          className={`${sportColors.accent} absolute left-0 top-0 max-w-[68%] rounded-br-sm px-1.5 py-0.5 text-[8px] font-semibold uppercase tracking-[0.08em] text-white truncate whitespace-nowrap`}
        >
          {displaySport}
        </span>
      )}

      {/* Time */}
      <p className="absolute right-1 top-0.5 text-[9px] font-medium text-gray-300">
        {timeLabel}
      </p>
      {/* Title */}
      <p className="whitespace-normal break-words text-[11px] font-normal leading-tight text-white transition-colors md:text-[12px]">
        {event.title}
      </p>

      {/* Status indicators */}
      <div className="mt-px flex flex-wrap items-center gap-1">
        {isLive && (
          <span className="rounded bg-red-600 px-1 py-0.5 text-[9px] font-bold text-white animate-pulse">
            LIVE
          </span>
        )}
        {event.hasFile && (
          <span className="rounded bg-green-600/30 px-1 py-0.5 text-[9px] text-green-300">
            Downloaded
          </span>
        )}
        {event.broadcast && (
          <span className="inline-flex items-center gap-0.5 text-[9px] text-green-300">
            <TvIcon className="h-2.5 w-2.5" />
            TV
          </span>
        )}
      </div>
    </button>
  );
}

function AgendaSection({
  date,
  events,
  timezone,
}: {
  date: Date;
  events: Event[];
  timezone: string | null;
}) {
  const navigate = useNavigate();

  return (
    <div className="border-b border-gray-800/80 py-3 last:border-b-0">
      <div className="mb-2 text-sm font-semibold text-white">
        {date.toLocaleDateString('en-US', {
          weekday: 'long',
          month: 'long',
          day: 'numeric',
          year: 'numeric',
        })}
      </div>
      <div className="space-y-2">
        {events.map(event => (
          <EventCard
            key={event.id}
            event={event}
            timezone={timezone}
            onClick={() => {
              if (event.leagueId) {
                navigate(`/leagues/${event.leagueId}`);
              }
            }}
          />
        ))}
      </div>
    </div>
  );
}

export default function CalendarPage() {
  const { data: events, isLoading, error } = useEvents();
  const { timezone, loading: timezoneLoading } = useTimezone();
  const [uiSettings, , settingsLoading] = useSettings<CalendarUISettings>('uiSettings', { firstDayOfWeek: 'sunday' });
  const navigate = useNavigate();
  const dateInputRef = useRef<HTMLInputElement>(null);
  const [currentDate, setCurrentDate] = useState<Date | null>(null);
  const [currentView, setCurrentView] = useState<CalendarView>('month');
  const [filterSport, setFilterSport] = useState<string>('all');
  const [filterTvOnly, setFilterTvOnly] = useState(false);
  const firstDayOfWeek: FirstDayOfWeek = uiSettings.firstDayOfWeek === 'monday' ? 'monday' : 'sunday';

  useEffect(() => {
    if (!timezoneLoading && !currentDate) {
      setCurrentDate(getTodayInTimezone(timezone));
    }
  }, [timezoneLoading, timezone, currentDate]);

  // Get unique sport categories from events for filter
  const uniqueSports = useMemo(() => {
    if (!events) return [];

    return Array.from(new Set(
      events
        .map(event => getSportCategory(event.sport))
    )) as string[];
  }, [events]);
  // Get "today" in the user's configured timezone
  const today = useMemo(() => getTodayInTimezone(timezone), [timezone]);
  const filterEvent = useCallback((event: Event) => {
    if (!event.monitored) return false; // Only show monitored events

    // Apply TV availability filter
    if (filterTvOnly && !event.broadcast) return false;

    // Apply sport filter
    if (filterSport !== 'all' && getSportCategory(event.sport) !== filterSport) return false;

    return true;
  }, [filterSport, filterTvOnly]);
  const visibleEvents = useMemo(() => {
    return (events ?? [])
      .filter(filterEvent)
      .sort((left, right) => new Date(left.eventDate).getTime() - new Date(right.eventDate).getTime());
  }, [events, filterEvent]);

  // Filter events for a specific day (respecting user's timezone)
  const getEventsForDay = (date: Date) => {
    // Get the date string for comparison (in user's timezone)
    const targetDateStr = formatDateInputValue(date);

    // Convert the event date from UTC to user's timezone and compare
    return visibleEvents.filter(event => getDateInTimezone(event.eventDate, timezone) === targetDateStr);
  };

  const isToday = useCallback((date: Date) => {
    return date.getDate() === today.getDate() &&
      date.getMonth() === today.getMonth() &&
      date.getFullYear() === today.getFullYear();
  }, [today]);

  // Navigate to a specific date
  const goToDate = (dateString: string) => {
    const selectedDate = new Date(`${dateString}T00:00:00`);

    // Month, week, and agenda views all anchor off the selected date now
    setCurrentDate(selectedDate);
  };

  const weekdayNames = useMemo(() => getWeekdayNames(firstDayOfWeek), [firstDayOfWeek]);
  const calendarWeeks = useMemo(
    () => (currentDate ? getCalendarWeeks(currentDate, firstDayOfWeek) : []),
    [currentDate, firstDayOfWeek]
  );
  // Get array of 7 days for the active week (respecting the configured first day of week)
  const weekDays = useMemo(
    () => (currentDate ? getWeekDays(currentDate, firstDayOfWeek) : []),
    [currentDate, firstDayOfWeek]
  );
  const agendaGroups = useMemo(() => {
    if (!currentDate) return [];

    const { start, end } = getAgendaRange(currentDate);
    const startStamp = new Date(start.getFullYear(), start.getMonth(), start.getDate()).getTime();
    const endStamp = new Date(end.getFullYear(), end.getMonth(), end.getDate()).getTime();
    const grouped = new Map<string, { date: Date; events: Event[] }>();

    visibleEvents
      .filter(event => {
        const eventDate = convertToTimezone(event.eventDate, timezone);
        const eventStamp = new Date(eventDate.getFullYear(), eventDate.getMonth(), eventDate.getDate()).getTime();
        return eventStamp >= startStamp && eventStamp <= endStamp;
      })
      .forEach(event => {
        const eventDate = convertToTimezone(event.eventDate, timezone);
        const key = formatDateInputValue(eventDate);
        const existing = grouped.get(key);

        if (existing) {
          existing.events.push(event);
          return;
        }

        grouped.set(key, {
          date: new Date(eventDate.getFullYear(), eventDate.getMonth(), eventDate.getDate()),
          events: [event],
        });
      });

    return Array.from(grouped.values());
  }, [currentDate, timezone, visibleEvents]);

  const headerLabel = useMemo(() => {
    if (!currentDate) return '';
    if (currentView === 'week') return formatWeekLabel(weekDays);
    if (currentView === 'agenda') return 'Agenda';
    return formatMonthLabel(currentDate);
  }, [currentDate, currentView, weekDays]);

  const headerSubLabel = useMemo(() => {
    if (!currentDate) return '';
    if (currentView === 'agenda') {
      const { start, end } = getAgendaRange(currentDate);
      return `${start.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} - ${end.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}`;
    }
    return '';
  }, [currentDate, currentView]);

  const showTodayButton = useMemo(() => {
    if (!currentDate) return false;
    if (currentView === 'month') {
      return currentDate.getMonth() !== today.getMonth() || currentDate.getFullYear() !== today.getFullYear();
    }

    if (currentView === 'week') {
      return !weekDays.some(day => isToday(day));
    }

    const { start, end } = getAgendaRange(currentDate);
    return today < startOfWeek(start, firstDayOfWeek) || today > endOfWeek(end, firstDayOfWeek);
  }, [currentDate, currentView, firstDayOfWeek, isToday, today, weekDays]);

  if (isLoading || timezoneLoading || settingsLoading || !currentDate) {
    return (
      <div className="p-8">
        <div className="flex h-64 items-center justify-center">
          <div className="h-12 w-12 animate-spin rounded-full border-b-2 border-red-600"></div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="rounded border border-red-700 bg-red-900 px-4 py-3 text-red-100">
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
        <div className="mb-2 md:mb-3">
          <div className="mb-2 flex flex-col justify-between gap-2 xl:flex-row xl:items-start">
            <div className="min-w-0">
              <h1 className="text-2xl font-bold text-white md:text-3xl">Calendar</h1>
            </div>

            {headerSubLabel && (
              <div className="text-sm font-medium text-red-400 md:text-base xl:self-center">
                {headerSubLabel}
              </div>
            )}

            <div className="overflow-x-auto xl:max-w-[calc(100%-16rem)]">
              <div className="flex min-w-max flex-wrap items-center justify-start gap-2 xl:justify-end">
                {/* Calendar Navigation */}
                <div className={TOOLBAR_GROUP_CLASS}>
                  {/* Today Button */}
                  {showTodayButton && (
                    <button
                      onClick={() => setCurrentDate(today)}
                      className={`${TOOLBAR_BUTTON_BASE_CLASS} ${TOOLBAR_BUTTON_ACTIVE_CLASS}`}
                      title="Go to current date"
                    >
                      Today
                    </button>
                  )}

                  <button
                    onClick={() => setCurrentDate(getAdjacentDate(currentDate, currentView, -1))}
                    className={`${TOOLBAR_BUTTON_BASE_CLASS} ${TOOLBAR_BUTTON_INACTIVE_CLASS}`}
                    title={`Previous ${currentView}`}
                  >
                    <ChevronLeftIcon className="h-5 w-5" />
                  </button>

                  {/* Fixed width container for date range */}
                  <div className="min-w-[170px] rounded-md bg-gray-800 px-3 py-1.5 text-center md:min-w-[230px]">
                    <p data-testid="calendar-current-month-label" className="truncate text-sm font-semibold text-white">
                      {headerLabel}
                    </p>
                  </div>

                  <button
                    onClick={() => setCurrentDate(getAdjacentDate(currentDate, currentView, 1))}
                    className={`${TOOLBAR_BUTTON_BASE_CLASS} ${TOOLBAR_BUTTON_INACTIVE_CLASS}`}
                    title={`Next ${currentView}`}
                  >
                    <ChevronRightIcon className="h-5 w-5" />
                  </button>

                  {/* Date Picker */}
                  <div className="relative">
                    <input
                      ref={dateInputRef}
                      data-testid="calendar-date-input"
                      type="date"
                      value={formatDateInputValue(currentDate)}
                      className="absolute h-0 w-0 opacity-0"
                      onChange={(event) => event.target.value && goToDate(event.target.value)}
                    />
                    <button
                      onClick={() => dateInputRef.current?.showPicker()}
                      className={`${TOOLBAR_BUTTON_BASE_CLASS} ${TOOLBAR_BUTTON_INACTIVE_CLASS}`}
                      title="Go to date"
                    >
                      <CalendarDaysIcon className="h-5 w-5" />
                    </button>
                  </div>
                </div>

                {/* View Switcher */}
                <div className={TOOLBAR_GROUP_CLASS}>
                  {(['month', 'week', 'agenda'] as CalendarView[]).map(view => (
                    <button
                      key={view}
                      type="button"
                      onClick={() => setCurrentView(view)}
                      className={`${TOOLBAR_BUTTON_BASE_CLASS} ${
                        currentView === view ? TOOLBAR_BUTTON_ACTIVE_CLASS : TOOLBAR_BUTTON_INACTIVE_CLASS
                      }`}
                    >
                      {view.charAt(0).toUpperCase() + view.slice(1)}
                    </button>
                  ))}
                </div>

                {/* Filters */}
                <div className={TOOLBAR_GROUP_CLASS}>
                  <div className="inline-flex items-center gap-2 rounded-md bg-gray-800 px-3 py-1.5 text-sm text-gray-400">
                    <FunnelIcon className="h-4 w-4" />
                    <span>Filter</span>
                    {(filterSport !== 'all' || filterTvOnly) && (
                      <span className="rounded-full bg-red-600 px-1.5 py-0.5 text-xs text-white">
                        {(filterSport !== 'all' ? 1 : 0) + (filterTvOnly ? 1 : 0)}
                      </span>
                    )}
                  </div>

                  {/* Sport Filter */}
                  <select
                    value={filterSport}
                    onChange={(event) => setFilterSport(event.target.value)}
                    className="rounded-md bg-gray-800 px-3 py-1.5 text-sm text-white transition-all focus:outline-none focus:ring-1 focus:ring-red-600"
                  >
                    <option value="all">All Sports</option>
                    {uniqueSports.map(sport => (
                      <option key={sport} value={sport}>{sport}</option>
                    ))}
                  </select>

                  {/* TV Only Filter */}
                  <label
                    className={`flex cursor-pointer items-center gap-2 rounded-md px-3 py-2 text-sm transition-all ${
                      filterTvOnly ? TOOLBAR_BUTTON_ACTIVE_CLASS : TOOLBAR_BUTTON_INACTIVE_CLASS
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={filterTvOnly}
                      onChange={(event) => setFilterTvOnly(event.target.checked)}
                      className="sr-only"
                    />
                    <TvIcon className="h-4 w-4" />
                    <span>TV Only</span>
                  </label>

                  {(filterSport !== 'all' || filterTvOnly) && (
                    <button
                      onClick={() => {
                        setFilterSport('all');
                        setFilterTvOnly(false);
                      }}
                      className={`${TOOLBAR_BUTTON_BASE_CLASS} ${TOOLBAR_BUTTON_INACTIVE_CLASS}`}
                    >
                      Clear
                    </button>
                  )}
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Calendar Grid - Month/week table or agenda list */}
        {currentView === 'agenda' ? (
          <div className="rounded-sm bg-gray-950/60 px-4 py-2" data-testid="calendar-agenda">
            {agendaGroups.length > 0 ? (
              agendaGroups.map(group => (
                <AgendaSection
                  key={formatDateInputValue(group.date)}
                  date={group.date}
                  events={group.events}
                  timezone={timezone}
                />
              ))
            ) : (
              <div className="py-8 text-center text-sm text-gray-500">No events in this agenda range</div>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px] table-fixed border-collapse" data-testid="calendar-table">
              <thead>
                <tr>
                  {weekdayNames.map(dayName => (
                    <th key={dayName} className="border-b border-gray-700/35 px-1 py-1 text-left text-xs font-semibold uppercase tracking-[0.12em] text-gray-500">
                      {dayName}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody data-testid="calendar-weeks">
                {(currentView === 'month' ? calendarWeeks : [weekDays.map(date => ({ date, isCurrentMonth: true }))]).map((week, weekIndex) => (
                  <tr key={`${week[0].date.toISOString()}-${weekIndex}`} data-testid={`calendar-week-${weekIndex}`}>
                    {week.map(day => {
                      const dayEvents = getEventsForDay(day.date);
                      const currentDayIsToday = isToday(day.date);

                      return (
                        <td
                          key={day.date.toISOString()}
                          data-testid={`calendar-day-${formatDateInputValue(day.date)}`}
                          className={`h-[132px] align-top border-b border-r border-gray-700/35 ${currentView === 'week' ? 'md:h-[200px]' : 'md:h-[152px]'}`}
                        >
                          <div className="flex h-full flex-col px-1 py-0.5">
                            {/* Day Header */}
                            <div className="mb-0.5 flex items-center justify-between">
                              <div className={`text-xs ${currentDayIsToday ? 'rounded-full bg-amber-500 px-2 py-0.5 font-bold text-black' : 'font-semibold text-gray-300'}`}>
                                {day.date.getDate()}
                              </div>
                              {currentView === 'month' && !day.isCurrentMonth ? (
                                <div className="text-[10px] uppercase tracking-[0.1em] text-gray-600">
                                  {day.date.toLocaleDateString('en-US', { month: 'short' })}
                                </div>
                              ) : null}
                            </div>

                            {/* Events for the day - stacked within the active cell */}
                            <div
                              data-testid={`calendar-day-events-${formatDateInputValue(day.date)}`}
                              className="space-y-1 overflow-y-auto pr-0.5"
                            >
                              {dayEvents.map(event => (
                                <EventCard
                                  key={event.id}
                                  event={event}
                                  timezone={timezone}
                                  onClick={() => {
                                    if (event.leagueId) {
                                      navigate(`/leagues/${event.leagueId}`);
                                    }
                                  }}
                                />
                              ))}
                            </div>
                          </div>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Legend */}
        <div className="mt-4">
          <h3 className="mb-2 text-sm font-semibold text-gray-400">Legend</h3>
          <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between lg:gap-4">
            <div className="flex flex-wrap gap-2 text-sm text-gray-400" data-testid="calendar-main-legend">
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 rounded bg-amber-500"></div>
                <span>Today</span>
              </div>
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 rounded bg-green-600"></div>
                <span>Downloaded</span>
              </div>
              <div className="flex items-center gap-2">
                <TvIcon className="h-3 w-3 text-green-400" />
                <span>TV Schedule Available</span>
              </div>
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 animate-pulse rounded bg-red-600 ring-2 ring-red-500/50"></div>
                <span>Live Now</span>
              </div>
            </div>

            {/* Sport Colors */}
            <div className="flex flex-wrap items-center gap-2 text-sm text-gray-400 lg:justify-end" data-testid="calendar-sport-legend">
              {Object.entries(SPORT_COLORS)
                .map(([sport, colors]) => (
                  <div key={sport} className="flex items-center gap-2">
                    <div data-testid={`calendar-sport-legend-${sport}`} className={`h-3 w-3 rounded ${colors.accent}`}></div>
                    <span>{sport}</span>
                  </div>
                ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
