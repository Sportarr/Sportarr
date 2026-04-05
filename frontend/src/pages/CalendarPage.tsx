import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ChevronLeftIcon, ChevronRightIcon, TvIcon, FunnelIcon, CalendarDaysIcon, XCircleIcon } from '@heroicons/react/24/outline';
import { CheckCircleIcon } from '@heroicons/react/24/solid';
import { useNavigate } from 'react-router-dom';
import PageShell, { PageErrorState, PageLoadingState } from '../components/PageShell';
import { useEvents } from '../api/hooks';
import type { Event } from '../types';
import { useSettings } from '../hooks/useSettings';
import { useUISettings } from '../hooks/useUISettings';
import { useCompactView } from '../hooks/useCompactView';
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
import type { FirstDayOfWeek } from '../utils/dateUtils';
import { convertToTimezone, formatTimeInTimezone, getDateInTimezone, getNowInTimezone, getTodayInTimezone } from '../utils/timezone';

type CalendarView = 'month' | 'week' | 'agenda';

interface CalendarUISettings {
  firstDayOfWeek?: string;
}

const TOOLBAR_GROUP_CLASS = 'inline-flex min-w-max items-center space-x-1 rounded-lg bg-gray-900 p-1';
const TOOLBAR_BUTTON_BASE_CLASS = 'rounded-md px-3 py-1.5 text-sm transition-all whitespace-nowrap';
const TOOLBAR_BUTTON_INACTIVE_CLASS = 'text-gray-400 hover:bg-gray-800 hover:text-white';
const TOOLBAR_BUTTON_ACTIVE_CLASS = 'bg-red-600 text-white';

// Sport color mappings (matching Sonarr/Radarr style)
// Reserved colors (do not assign to sports):
//   green  = Downloaded indicator
//   red    = Live Now indicator
//   amber  = Today indicator
// Soccer uses indigo (not emerald/green) so the green checkmark unambiguously
// means "downloaded file". Golf uses orange (not lime) for the same reason.
const SPORT_COLORS = {
  Fighting: { surface: 'bg-rose-900/35', border: 'border-rose-500/70', accent: 'bg-rose-500' },
  Soccer: { surface: 'bg-indigo-900/35', border: 'border-indigo-500/70', accent: 'bg-indigo-500' },
  Basketball: { surface: 'bg-amber-900/35', border: 'border-amber-500/70', accent: 'bg-amber-500' },
  Football: { surface: 'bg-blue-950/35', border: 'border-blue-600/70', accent: 'bg-blue-600' },
  Baseball: { surface: 'bg-violet-900/35', border: 'border-violet-500/70', accent: 'bg-violet-500' },
  Hockey: { surface: 'bg-cyan-900/35', border: 'border-cyan-500/70', accent: 'bg-cyan-500' },
  Tennis: { surface: 'bg-yellow-900/35', border: 'border-yellow-500/70', accent: 'bg-yellow-500' },
  Golf: { surface: 'bg-orange-900/35', border: 'border-orange-500/70', accent: 'bg-orange-500' },
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

  // Use timezone-aware "now" so the comparison is consistent with converted event dates
  const now = getNowInTimezone(timezone);
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
      {/* Top row */}
      <div className="absolute left-0 right-0 top-0 flex items-center justify-between overflow-hidden">
        <div className="flex min-w-0 items-start gap-0.5">
          {displaySport && (
            <span
              data-testid={`calendar-event-sport-${event.id}`}
              className={`${sportColors.accent} shrink-0 rounded-br-sm px-1.5 py-0.5 text-[8px] font-semibold uppercase tracking-[0.08em] text-white`}
            >
              {displaySport}
            </span>
          )}
          <div className="flex items-center gap-0.5 pt-0.5">
            {event.broadcast && (
              <TvIcon className="h-3.5 w-3.5 shrink-0 text-green-300" />
            )}
            {event.hasFile && (
              <CheckCircleIcon className="h-3.5 w-3.5 shrink-0 text-green-500" />
            )}
            {!event.hasFile && !isLive && convertToTimezone(event.eventDate, timezone) < getNowInTimezone(timezone) && (
              <XCircleIcon className="h-3.5 w-3.5 shrink-0 text-gray-500" />
            )}
          </div>
        </div>
        <div className="flex shrink-0 items-center">
          {isLive ? (
            <span className="rounded-bl-sm bg-red-500 px-1 py-0.5 text-[9px] font-bold text-white animate-pulse">
              LIVE
            </span>
          ) : (
            <span className="pr-1 text-[9px] font-medium text-gray-300">{timeLabel}</span>
          )}
        </div>
      </div>

      {/* Title */}
      <p className="whitespace-normal break-words text-[11px] font-normal leading-tight text-white transition-colors md:text-[12px]">
        {event.title}
      </p>
    </button>
  );
}

function SpaciousAgendaEventCard({
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
    weekday: 'short', month: 'short', day: 'numeric',
    hour: 'numeric', minute: '2-digit',
  });

  return (
    <button
      type="button"
      onClick={onClick}
      className={`w-full text-left rounded-lg p-4 border transition-all hover:opacity-90 ${sportColors.surface} ${isLive ? 'border-red-500 ring-2 ring-red-500/40 animate-pulse' : sportColors.border}`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex flex-wrap items-center gap-2 mb-1">
            <span className={`${sportColors.accent} px-2 py-0.5 text-xs font-semibold text-white rounded`}>
              {getSportDisplayLabel(event.sport)}
            </span>
            {isLive && (
              <span className="px-2 py-0.5 bg-red-500 text-white text-xs font-bold rounded animate-pulse">LIVE</span>
            )}
            {event.hasFile && (
              <CheckCircleIcon className="w-4 h-4 text-green-500 flex-shrink-0" />
            )}
            {event.broadcast && (
              <span className="flex items-center gap-1 text-xs text-green-300">
                <TvIcon className="w-3.5 h-3.5" />
                {event.broadcast}
              </span>
            )}
          </div>
          <h3 className="text-lg font-semibold text-white truncate">{event.title}</h3>
          {event.homeTeamName && event.awayTeamName && (
            <p className="text-sm text-gray-400 mt-0.5">{event.homeTeamName} vs {event.awayTeamName}</p>
          )}
          {event.venue && (
            <p className="text-sm text-gray-500 mt-0.5">{event.venue}</p>
          )}
        </div>
        <span className="text-sm text-gray-400 flex-shrink-0 whitespace-nowrap">{timeLabel}</span>
      </div>
    </button>
  );
}

function AgendaSection({
  date,
  events,
  timezone,
  isToday,
  compact,
}: {
  date: Date;
  events: Event[];
  timezone: string | null;
  isToday: boolean;
  compact: boolean;
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
          compact ? (
            <EventCard
              key={event.id}
              event={event}
              timezone={timezone}
              onClick={() => { if (event.leagueId) navigate(`/leagues/${event.leagueId}`); }}
            />
          ) : (
            <SpaciousAgendaEventCard
              key={event.id}
              event={event}
              timezone={timezone}
              onClick={() => { if (event.leagueId) navigate(`/leagues/${event.leagueId}`); }}
            />
          )
        ))}
      </div>
    </div>
  );
}

export default function CalendarPage() {
  const { data: events, isLoading, error } = useEvents();
  const { timezone, loading: timezoneLoading } = useUISettings();
  const compactView = useCompactView();
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

  // Pre-group filtered events by date string (YYYY-MM-DD) for O(1) per-cell lookup
  const eventsByDate = useMemo(() => {
    const map = new Map<string, Event[]>();
    for (const event of visibleEvents) {
      const dateStr = getDateInTimezone(event.eventDate, timezone);
      const existing = map.get(dateStr);
      if (existing) {
        existing.push(event);
      } else {
        map.set(dateStr, [event]);
      }
    }
    return map;
  }, [visibleEvents, timezone]);

  // Look up events for a specific day from the pre-grouped map
  const getEventsForDay = (date: Date) => {
    return eventsByDate.get(formatDateInputValue(date)) ?? [];
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

    for (const event of visibleEvents) {
      const eventDate = convertToTimezone(event.eventDate, timezone);
      const eventStamp = new Date(eventDate.getFullYear(), eventDate.getMonth(), eventDate.getDate()).getTime();
      if (eventStamp < startStamp || eventStamp > endStamp) continue;

      const key = formatDateInputValue(eventDate);
      const existing = grouped.get(key);

      if (existing) {
        existing.events.push(event);
      } else {
        grouped.set(key, {
          date: new Date(eventDate.getFullYear(), eventDate.getMonth(), eventDate.getDate()),
          events: [event],
        });
      }
    }

    return Array.from(grouped.values());
  }, [currentDate, timezone, visibleEvents]);

  const headerLabel = useMemo(() => {
    if (!currentDate) return '';
    if (currentView === 'week') return formatWeekLabel(weekDays);
    if (currentView === 'agenda') {
      const { start, end } = getAgendaRange(currentDate);
      return `${start.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} - ${end.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}`;
    }
    return formatMonthLabel(currentDate);
  }, [currentDate, currentView, weekDays]);

  const isOnToday = useCallback(() => {
    if (!currentDate) return false;
    if (currentView === 'month') {
      return currentDate.getMonth() === today.getMonth() && currentDate.getFullYear() === today.getFullYear();
    }

    if (currentView === 'week') {
      return weekDays.some(day => isToday(day));
    }

    const { start, end } = getAgendaRange(currentDate);
    return today >= startOfWeek(start, firstDayOfWeek) && today <= endOfWeek(end, firstDayOfWeek);
  }, [currentDate, currentView, firstDayOfWeek, isToday, today, weekDays]);

  if (isLoading || timezoneLoading || settingsLoading || !currentDate) {
    return <PageLoadingState label="Loading calendar..." />;
  }

  if (error) {
    return (
      <PageErrorState title="Error loading events" message={(error as Error).message} />
    );
  }

  return (
    <PageShell>
      <div className="mx-auto">
        {/* Header */}
        <div className="mb-2 md:mb-3">
          <div className="mb-2 flex flex-col justify-between gap-2 xl:flex-row xl:items-start">
            <div className="min-w-0">
              <h1 className="text-2xl font-bold text-white md:text-3xl">Calendar</h1>
            </div>

            <div className="overflow-x-auto xl:max-w-[calc(100%-16rem)]">
              <div className="flex min-w-max flex-wrap items-center justify-start gap-2 xl:justify-end">
                {/* Calendar Navigation */}
                <div className={TOOLBAR_GROUP_CLASS}>
                  {/* Today Button */}
                  <button
                    onClick={() => setCurrentDate(today)}
                    className={`${TOOLBAR_BUTTON_BASE_CLASS} ${
                      isOnToday()
                        ? 'text-gray-400 hover:bg-gray-800 hover:text-white'
                        : TOOLBAR_BUTTON_ACTIVE_CLASS
                    }`}
                    title="Go to current date"
                    disabled={isOnToday()}
                  >
                    Today
                  </button>

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
                  isToday={isToday(group.date)}
                  compact={compactView}
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
                <CheckCircleIcon className="h-3 w-3 text-green-500" />
                <span>Downloaded</span>
              </div>
              <div className="flex items-center gap-2">
                <XCircleIcon className="h-3 w-3 text-gray-500" />
                <span>Missed</span>
              </div>
              <div className="flex items-center gap-2">
                <TvIcon className="h-3 w-3 text-green-400" />
                <span>TV Schedule Available</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="rounded bg-red-500 px-1 py-0.5 text-[9px] font-bold text-white animate-pulse">LIVE</span>
                <span>Live Now</span>
              </div>
            </div>

            {/* Sport Colors */}
            <div className="flex flex-wrap items-center gap-2 text-sm text-gray-400 lg:justify-end" data-testid="calendar-sport-legend">
              {uniqueSports
                .filter(sport => sport in SPORT_COLORS)
                .map(sport => {
                  const colors = SPORT_COLORS[sport as SportColorKey];
                  return (
                    <div key={sport} className="flex items-center gap-2">
                      <div data-testid={`calendar-sport-legend-${sport}`} className={`h-3 w-3 rounded ${colors.accent}`}></div>
                      <span>{sport}</span>
                    </div>
                  );
                })}
            </div>
          </div>
        </div>
      </div>
    </PageShell>
  );
}
