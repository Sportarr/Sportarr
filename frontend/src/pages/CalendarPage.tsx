import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { isTerminalStatus } from '../utils/eventStatus';
import { ChevronLeftIcon, ChevronRightIcon, TvIcon, FunnelIcon, CalendarDaysIcon, XCircleIcon, LinkIcon, ClipboardDocumentIcon, ClipboardDocumentCheckIcon, EyeSlashIcon } from '@heroicons/react/24/outline';
import { CheckCircleIcon, EyeIcon as EyeSolidIcon } from '@heroicons/react/24/solid';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import apiClient from '../api/client';
import PageShell, { PageErrorState, PageLoadingState } from '../components/PageShell';
import { useCalendarEvents } from '../api/hooks';
import type { Event } from '../types';
import { useSettings } from '../hooks/useSettings';
import { createRequestUrl } from '../utils/request';
import { applyCalendarMonitorChange } from '../utils/calendarCache';
import { useUISettings } from '../hooks/useUISettings';
import { useCompactView } from '../hooks/useCompactView';
import { useMediaQuery } from '../hooks/useMediaQuery';
import { BUTTON_TOOLBAR_ACTIVE as TOOLBAR_BUTTON_ACTIVE_CLASS, BUTTON_TOOLBAR_BASE as TOOLBAR_BUTTON_BASE_CLASS, BUTTON_TOOLBAR_INACTIVE as TOOLBAR_BUTTON_INACTIVE_CLASS } from '../utils/designTokens';
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

// Sport color mappings.
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

const getLeagueLogoUrl = (event: Event) => event.league?.logoUrl ?? event.leagueLogoUrl;

// Check if an event is currently live based on time
// Events are considered live if current time is within 4 hours after start time
// (most sporting events last 2-4 hours)

/**
 * Copy text to the clipboard, returning whether it worked.
 *
 * navigator.clipboard exists only on a secure origin. A self-hosted install
 * reached over plain http on a LAN address is not one, so the modern call is
 * simply absent there and reaching for it throws. The textarea fallback is the
 * pre-clipboard-API technique and still works everywhere.
 */
const copyToClipboard = async (text: string): Promise<boolean> => {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Fall through to the textarea below.
  }

  try {
    const scratch = document.createElement('textarea');
    scratch.value = text;
    scratch.setAttribute('readonly', '');
    scratch.style.position = 'fixed';
    scratch.style.opacity = '0';
    document.body.appendChild(scratch);
    scratch.select();
    const copied = document.execCommand('copy');
    document.body.removeChild(scratch);
    return copied;
  } catch {
    return false;
  }
};
const isEventLive = (event: Event, timezone: string | null): boolean => {
  // If status is explicitly "Live", it's live
  if (event.status === 'Live') return true;

  // An event that has already finished, or that will never be played, is not
  // live whatever the clock says. Only the time window was checked, so a match
  // that ended, or one cancelled or postponed hours earlier, still carried the
  // red pulsing LIVE card for four hours after its start time.
  // Normalised, because real feeds spell these many ways. The league page
  // already recognises "Match Finished", "Canceled", "AET" and case
  // variants, and comparing raw literals here kept the live pulse on for
  // events that page already showed as over.
  if (isTerminalStatus(event.status)) {
    return false;
  }

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

/**
 * The monitor toggle that rides on an event card. It sits over the card
 * rather than inside it, because the card itself is a button and a button
 * cannot hold another one.
 */
function MonitorToggleOverlay({
  monitored,
  onToggle,
  disabled,
  className,
  iconClass,
}: {
  monitored: boolean;
  onToggle: () => void;
  disabled: boolean;
  className: string;
  iconClass: string;
}) {
  return (
    <button
      type="button"
      onClick={(clickEvent) => {
        clickEvent.stopPropagation();
        onToggle();
      }}
      disabled={disabled}
      aria-pressed={monitored}
      aria-label={monitored ? 'Monitored, click to unmonitor' : 'Unmonitored, click to monitor'}
      title={monitored ? 'Monitored, click to unmonitor' : 'Unmonitored, click to monitor'}
      className={`absolute z-20 rounded bg-black/50 p-1 leading-none opacity-0 transition-opacity hover:bg-black/80 focus-visible:opacity-100 group-hover:opacity-100 disabled:opacity-50 [@media(hover:none)]:opacity-100 focus:outline-none focus:ring-1 focus:ring-red-500 ${className}`}
    >
      {monitored ? (
        <EyeSolidIcon className={`${iconClass} text-white`} />
      ) : (
        <EyeSlashIcon className={`${iconClass} text-gray-300`} />
      )}
    </button>
  );
}

function EventCard({
  event,
  timezone,
  onClick,
  onToggleMonitor,
  toggling = false,
}: {
  event: Event;
  timezone: string | null;
  onClick: () => void;
  onToggleMonitor?: (event: Event) => void;
  toggling?: boolean;
}) {
  const sportColors = getSportColors(event.sport || 'default');
  const isLive = isEventLive(event, timezone);
  const timeLabel = formatTimeInTimezone(event.eventDate, timezone, {
    hour: 'numeric',
    minute: '2-digit',
  });
  const displaySport = getSportDisplayLabel(event.sport);
  const leagueLogoUrl = getLeagueLogoUrl(event);

  return (
    <div className="group relative">
    <button
      type="button"
      onClick={onClick}
      data-testid={`calendar-event-${event.id}`}
      className={`${sportColors.surface} ${isLive ? 'border-red-500 ring-2 ring-red-500/40 animate-pulse' : sportColors.border} ${event.monitored ? 'hover:opacity-95' : 'opacity-60 hover:opacity-75'} relative block w-full overflow-hidden rounded-sm border px-1.5 pb-1 pt-[20.5px] text-left shadow-sm transition-all`}
      title={`${event.title}${event.monitored ? '' : '\nNot monitored'}${event.venue ? `\n${event.venue}` : ''}${event.broadcast ? `\nTV: ${event.broadcast}` : ''}`}
    >
      {/* Top row */}
      <div className="absolute left-0 right-0 top-0 z-10 flex items-center justify-between overflow-hidden">
        <div className="flex min-w-0 items-center gap-0.5">
          {displaySport && (
            <span
              data-testid={`calendar-event-sport-${event.id}`}
              className={`${sportColors.accent} shrink-0 rounded-br-sm px-1.5 py-0.5 text-[8px] font-semibold uppercase tracking-[0.08em] text-white`}
            >
              {displaySport}
            </span>
          )}
          {leagueLogoUrl && (
            <img
              src={leagueLogoUrl}
              alt=""
              aria-hidden="true"
              loading="lazy"
              className="h-4 w-4 shrink-0 object-contain"
            />
          )}
          <div className="flex items-center gap-0.5">
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
        <div className="flex shrink-0 items-center gap-1 pr-1">
          <span className="text-[9px] font-medium text-gray-300">{timeLabel}</span>
          {isLive && (
            <span className="-mr-1 rounded-bl-sm bg-red-500 px-1 py-0.5 text-[9px] font-bold text-white animate-pulse">
              LIVE
            </span>
          )}
        </div>
      </div>

      {/* Title */}
      <p className={`relative z-10 whitespace-normal break-words text-[11px] font-normal leading-tight text-white transition-colors md:text-[12px] ${onToggleMonitor ? 'pr-5' : ''}`}>
        {event.title}
      </p>
    </button>
      {onToggleMonitor && (
        <MonitorToggleOverlay
          monitored={event.monitored}
          onToggle={() => onToggleMonitor(event)}
          disabled={toggling}
          className="bottom-0.5 right-0.5"
          iconClass="h-3.5 w-3.5"
        />
      )}
    </div>
  );
}

function SpaciousAgendaEventCard({
  event,
  timezone,
  onClick,
  onToggleMonitor,
  toggling = false,
}: {
  event: Event;
  timezone: string | null;
  onClick: () => void;
  onToggleMonitor?: (event: Event) => void;
  toggling?: boolean;
}) {
  const sportColors = getSportColors(event.sport || 'default');
  const isLive = isEventLive(event, timezone);
  const timeLabel = formatTimeInTimezone(event.eventDate, timezone, {
    weekday: 'short', month: 'short', day: 'numeric',
    hour: 'numeric', minute: '2-digit',
  });
  const leagueLogoUrl = getLeagueLogoUrl(event);

  return (
    <div className="group relative">
    <button
      type="button"
      onClick={onClick}
      className={`relative w-full overflow-hidden text-left rounded-lg p-4 border transition-all ${sportColors.surface} ${isLive ? 'border-red-500 ring-2 ring-red-500/40 animate-pulse' : sportColors.border} ${event.monitored ? 'hover:opacity-90' : 'opacity-60 hover:opacity-75'}`}
    >
      <div className="relative z-10 flex items-start justify-between gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex flex-wrap items-center gap-2 mb-1">
            <span className={`${sportColors.accent} px-2 py-0.5 text-xs font-semibold text-white rounded`}>
              {getSportDisplayLabel(event.sport)}
            </span>
            {leagueLogoUrl && (
              <img
                src={leagueLogoUrl}
                alt=""
                aria-hidden="true"
                loading="lazy"
                className="h-5 w-5 shrink-0 object-contain"
              />
            )}
            {isLive && (
              <span className="px-2 py-0.5 bg-red-500 text-white text-xs font-bold rounded animate-pulse">LIVE</span>
            )}
            {!event.monitored && !onToggleMonitor && (
              <span className="flex items-center gap-1 text-xs text-gray-400">
                <EyeSlashIcon className="w-3.5 h-3.5 flex-shrink-0" />
                Not monitored
              </span>
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
        <span className={`text-sm text-gray-400 flex-shrink-0 whitespace-nowrap ${onToggleMonitor ? 'pr-7' : ''}`}>{timeLabel}</span>
      </div>
    </button>
      {onToggleMonitor && (
        <MonitorToggleOverlay
          monitored={event.monitored}
          onToggle={() => onToggleMonitor(event)}
          disabled={toggling}
          className="right-3 top-3"
          iconClass="h-5 w-5"
        />
      )}
    </div>
  );
}

function AgendaSection({
  date,
  events,
  timezone,
  isToday,
  compact,
  onToggleMonitor,
  togglingId,
}: {
  date: Date;
  events: Event[];
  timezone: string | null;
  isToday: boolean;
  compact: boolean;
  onToggleMonitor: (event: Event) => void;
  togglingId: number | null;
}) {
  const navigate = useNavigate();

  return (
    <div className={`border-b border-gray-800/80 last:border-b-0 ${isToday ? 'ring-1 ring-inset ring-amber-500' : 'py-3'}`}>
      {isToday ? (
        <>
          <div className="mb-2">
            <span className="inline-block bg-amber-500 px-2 py-1 text-sm font-semibold text-black">
              {date.toLocaleDateString('en-US', {
                weekday: 'long',
                month: 'long',
                day: 'numeric',
                year: 'numeric',
              })}
            </span>
          </div>
          <div className="space-y-2 px-2 pb-2">
            {events.map(event => (
              compact ? (
                <EventCard
                  key={event.id}
                  event={event}
                  timezone={timezone}
                  onClick={() => { if (event.leagueId) navigate(`/leagues/${event.leagueId}?event=${event.id}`); }}
                  onToggleMonitor={onToggleMonitor}
                  toggling={togglingId === event.id}
                />
              ) : (
                <SpaciousAgendaEventCard
                  key={event.id}
                  event={event}
                  timezone={timezone}
                  onClick={() => { if (event.leagueId) navigate(`/leagues/${event.leagueId}?event=${event.id}`); }}
                  onToggleMonitor={onToggleMonitor}
                  toggling={togglingId === event.id}
                />
              )
            ))}
          </div>
        </>
      ) : (
        <>
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
                  onClick={() => { if (event.leagueId) navigate(`/leagues/${event.leagueId}?event=${event.id}`); }}
                  onToggleMonitor={onToggleMonitor}
                  toggling={togglingId === event.id}
                />
              ) : (
                <SpaciousAgendaEventCard
                  key={event.id}
                  event={event}
                  timezone={timezone}
                  onClick={() => { if (event.leagueId) navigate(`/leagues/${event.leagueId}?event=${event.id}`); }}
                  onToggleMonitor={onToggleMonitor}
                  toggling={togglingId === event.id}
                />
              )
            ))}
          </div>
        </>
      )}
    </div>
  );
}

export default function CalendarPage() {
  const { timezone, loading: timezoneLoading } = useUISettings();
  const compactView = useCompactView();
  const [uiSettings, , settingsLoading] = useSettings<CalendarUISettings>('uiSettings', { firstDayOfWeek: 'sunday' });
  const navigate = useNavigate();
  const dateInputRef = useRef<HTMLInputElement>(null);
  const [currentDate, setCurrentDate] = useState<Date | null>(null);
  const [currentView, setCurrentView] = useState<CalendarView>(() => {
    // Restore the view the user last chose. Only a "they chose something" flag
    // was kept, not the choice, so a reload dropped a phone user back to the
    // month grid the flag exists to keep them out of.
    const saved = localStorage.getItem('sportarr.calendarView');
    return saved === 'month' || saved === 'week' || saved === 'agenda' ? saved : 'month';
  });
  const isPhone = useMediaQuery('(max-width: 639px)');

  // Re-render on the minute so the day rolls over and the live badges come and
  // go on their own. Both were worked out once per render, so a calendar left
  // open overnight kept yesterday marked as today and an event that started or
  // finished meanwhile kept whatever badge it had when the page loaded.
  const [minuteTick, setMinuteTick] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => setMinuteTick(tick => tick + 1), 60_000);
    return () => clearInterval(timer);
  }, []);

  // Phones default to the agenda list: the month grid needs a 900px canvas and
  // horizontal scrolling, which is no way to read a calendar on a phone. Only
  // a default - the moment the user picks a view themselves we respect it.
  useEffect(() => {
    if (isPhone && !localStorage.getItem('sportarr.calendarViewChosen')) {
      setCurrentView('agenda');
    }
  }, [isPhone]);
  const [filterSport, setFilterSport] = useState<string>('all');
  const [filterTvOnly, setFilterTvOnly] = useState(false);
  const [showUnmonitored, setShowUnmonitored] = useState(false);
  const [showIcalModal, setShowIcalModal] = useState(false);
  const [icalCopied, setIcalCopied] = useState(false);
  const firstDayOfWeek: FirstDayOfWeek = uiSettings.firstDayOfWeek === 'monday' ? 'monday' : 'sunday';

  useEffect(() => {
    if (!timezoneLoading && !currentDate) {
      setCurrentDate(getTodayInTimezone(timezone));
    }
  }, [timezoneLoading, timezone, currentDate]);

  // Compute date window for the API request based on current view and date.
  // Only fetch events within the visible range (+buffer for partial weeks at edges).
  // React Query caches by [start, end] so navigating back to a previous month is instant.
  const { calStart, calEnd } = useMemo(() => {
    if (!currentDate) return { calStart: null, calEnd: null };
    if (currentView === 'month') {
      // 1 month before + 1 month after to cover partial weeks at month edges
      const s = addMonths(currentDate, -1);
      const e = addMonths(currentDate, 2);
      return { calStart: s.toISOString(), calEnd: e.toISOString() };
    }
    if (currentView === 'week') {
      const s = addDays(currentDate, -14);
      const e = addDays(currentDate, 14);
      return { calStart: s.toISOString(), calEnd: e.toISOString() };
    }
    // agenda
    const s = addDays(currentDate, -1);
    const e = addDays(currentDate, 30);
    return { calStart: s.toISOString(), calEnd: e.toISOString() };
  }, [currentDate, currentView]);

  const { data: events, isLoading, error } = useCalendarEvents(calStart, calEnd, showUnmonitored);
  const queryClient = useQueryClient();

  // Monitor an event from the calendar, the same PUT the league page uses, so
  // the claim that survives an automatic unmonitor is recorded here too.
  const toggleMonitorMutation = useMutation({
    mutationFn: async ({ eventId, monitored }: { eventId: number; monitored: boolean }) => {
      const { data } = await apiClient.put(`/events/${eventId}`, { monitored });
      return data;
    },
    onSuccess: (_updated, { eventId, monitored }) => {
      // Patch every cached range rather than refetching the month.
      applyCalendarMonitorChange(queryClient, eventId, monitored);
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
      toast.success(monitored ? 'Event monitored' : 'Event unmonitored');
    },
    onError: () => {
      toast.error('Failed to update event');
    },
  });

  const pendingMonitorId = toggleMonitorMutation.isPending
    ? (toggleMonitorMutation.variables?.eventId ?? null)
    : null;

  const handleToggleMonitor = useCallback((event: Event) => {
    toggleMonitorMutation.mutate({ eventId: event.id, monitored: !event.monitored });
  }, [toggleMonitorMutation]);

  // Get unique sport categories from events for filter
  const uniqueSports = useMemo(() => {
    if (!events) return [];

    return Array.from(new Set(
      events
        .map(event => getSportCategory(event.sport))
    )) as string[];
  }, [events]);
  // A sport the user picked can vanish from the list when the monitoring
  // filter or the date window changes. The stale value would keep filtering
  // while the select shows nothing selected, leaving an empty calendar with
  // no visible cause.
  useEffect(() => {
    if (events && filterSport !== 'all' && !uniqueSports.includes(filterSport)) {
      setFilterSport('all');
    }
  }, [events, filterSport, uniqueSports]);

  // Get "today" in the user's configured timezone
  const today = useMemo(() => getTodayInTimezone(timezone), [timezone, minuteTick]);
  const filterEvent = useCallback((event: Event) => {
    // The server decides which monitoring states come back. Sport and TV
    // stay client-side so switching them costs no request.
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
    <PageShell className="md:h-full">
      <div className="mx-auto md:flex md:h-full md:min-h-0 md:flex-col">
        {/* Header */}
        <div className="mb-2 md:mb-3">
          <div className="mb-2 flex flex-col justify-between gap-2 xl:flex-row xl:items-start">
            <div className="min-w-0">
              <h1 className="text-2xl font-bold text-white md:text-3xl">Calendar</h1>
            </div>

            <div className="overflow-x-auto xl:max-w-[calc(100%-16rem)]">
              <div className="flex sm:min-w-max flex-wrap items-center justify-start gap-2 xl:justify-end">
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
                      onClick={() => {
                        localStorage.setItem('sportarr.calendarViewChosen', '1');
                        localStorage.setItem('sportarr.calendarView', view);
                        setCurrentView(view);
                      }}
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
                    {(filterSport !== 'all' || filterTvOnly || showUnmonitored) && (
                      <span className="rounded-full bg-red-600 px-1.5 py-0.5 text-xs text-white">
                        {(filterSport !== 'all' ? 1 : 0) + (filterTvOnly ? 1 : 0) + (showUnmonitored ? 1 : 0)}
                      </span>
                    )}
                  </div>

                  {/* Monitoring Filter */}
                  <select
                    value={showUnmonitored ? 'all' : 'monitored'}
                    onChange={(event) => setShowUnmonitored(event.target.value === 'all')}
                    className="rounded-md bg-gray-800 px-3 py-1.5 text-sm text-white transition-all focus:outline-none focus:ring-1 focus:ring-red-600"
                    aria-label="Monitoring filter"
                  >
                    <option value="monitored">Monitored Only</option>
                    <option value="all">All Events</option>
                  </select>

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

                  {(filterSport !== 'all' || filterTvOnly || showUnmonitored) && (
                    <button
                      onClick={() => {
                        setFilterSport('all');
                        setFilterTvOnly(false);
                        setShowUnmonitored(false);
                      }}
                      className={`${TOOLBAR_BUTTON_BASE_CLASS} ${TOOLBAR_BUTTON_INACTIVE_CLASS}`}
                    >
                      Clear
                    </button>
                  )}
                </div>

                {/* iCal Feed Link */}
                <div className={TOOLBAR_GROUP_CLASS}>
                  <button
                    onClick={() => { setShowIcalModal(true); setIcalCopied(false); }}
                    className={`${TOOLBAR_BUTTON_BASE_CLASS} ${TOOLBAR_BUTTON_INACTIVE_CLASS} flex items-center gap-1.5`}
                    title="iCal calendar subscription link"
                  >
                    <LinkIcon className="h-4 w-4" />
                    <span>iCal</span>
                  </button>
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
                  onToggleMonitor={handleToggleMonitor}
                  togglingId={pendingMonitorId}
                />
              ))
            ) : (
              <div className="py-8 text-center text-sm text-gray-500">No events in this agenda range</div>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto md:flex-1 md:min-h-0">
            {/* Phones fit all 7 columns (Google-style dot cells); sm+ keeps the
                wide grid with full event chips. */}
            <table className="w-full sm:min-w-[900px] table-fixed border-collapse md:h-full" data-testid="calendar-table">
              <thead>
                <tr>
                  {weekdayNames.map(dayName => (
                    <th key={dayName} className="border-b border-gray-700/35 px-1 py-1 text-center sm:text-left text-xs font-semibold uppercase sm:tracking-[0.12em] text-gray-500">
                      <span className="sm:hidden">{dayName.charAt(0)}</span>
                      <span className="hidden sm:inline">{dayName}</span>
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
                          className={`relative h-14 sm:h-[132px] md:h-auto align-top border-b border-r border-gray-700/35 ${currentDayIsToday ? "bg-amber-500/5 ring-1 ring-inset ring-amber-500" : ""}`}
                        >
                          {currentDayIsToday && (
                            <div className="absolute left-0 top-0 bg-amber-500 px-1.5 py-0.5 text-xs font-bold leading-tight text-black">
                              {day.date.getDate()}
                            </div>
                          )}
                          <div className="flex h-full flex-col px-1 py-0.5">
                            {/* Day Header */}
                            <div className={`flex items-center justify-between ${currentDayIsToday ? 'mb-1' : 'mb-0.5'}`}>
                              <div className={`text-xs ${currentDayIsToday ? 'invisible font-bold' : 'font-semibold text-gray-300'}`}>
                                {day.date.getDate()}
                              </div>
                              {currentView === 'month' && !day.isCurrentMonth ? (
                                <div className="hidden sm:block text-[10px] uppercase tracking-[0.1em] text-gray-600">
                                  {day.date.toLocaleDateString('en-US', { month: 'short' })}
                                </div>
                              ) : null}
                            </div>

                            {/* Phones: sport-colored dots, whole cell jumps to the
                                agenda (Google month pattern - cells are too small
                                for titled chips). sm+: full event chips. */}
                            {isPhone ? (
                              dayEvents.length > 0 && (
                                <button
                                  type="button"
                                  aria-label={`${dayEvents.length} events on ${day.date.toDateString()}`}
                                  onClick={() => {
                                    localStorage.setItem('sportarr.calendarViewChosen', '1');

                                    localStorage.setItem('sportarr.calendarView', 'agenda');
                                    // Anchor the agenda on the day that was
                                    // tapped. Switching the view alone left it
                                    // anchored wherever it already was, so the
                                    // agenda opened on another date and the
                                    // events just tapped could be far down the
                                    // list or, for a spillover cell from the
                                    // neighbouring month, outside the range
                                    // altogether.
                                    setCurrentDate(day.date);
                                    setCurrentView('agenda');
                                  }}
                                  className="flex flex-wrap items-center gap-1 px-0.5 pt-1"
                                >
                                  {dayEvents.slice(0, 4).map(event => (
                                    <span
                                      key={event.id}
                                      className={`h-1.5 w-1.5 rounded-full ${getSportColors(event.sport || 'default').accent}`}
                                    />
                                  ))}
                                  {dayEvents.length > 4 && (
                                    <span className="text-[9px] leading-none text-gray-400">+{dayEvents.length - 4}</span>
                                  )}
                                </button>
                              )
                            ) : (
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
                                        navigate(`/leagues/${event.leagueId}?event=${event.id}`);
                                      }
                                    }}
                                    onToggleMonitor={handleToggleMonitor}
                                    toggling={pendingMonitorId === event.id}
                                  />
                                ))}
                              </div>
                            )}
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
                <EyeSolidIcon className="h-3 w-3 text-white" />
                <span>Monitored</span>
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

      {/* iCal Subscription Modal */}
      {showIcalModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={() => setShowIcalModal(false)}>
          <div className="mx-4 w-full max-w-lg rounded-xl bg-gray-800 p-6 shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="mb-4 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-white">iCal Calendar Subscription</h2>
              <button onClick={() => setShowIcalModal(false)} className="text-gray-400 hover:text-white">
                <XCircleIcon className="h-6 w-6" />
              </button>
            </div>
            <p className="mb-4 text-sm text-gray-400">
              Subscribe to this URL in Google Calendar, Apple Calendar, or Outlook to sync your Sportarr events.
            </p>
            <div className="mb-4 flex items-center gap-2">
              <input
                type="text"
                readOnly
                value={`${window.location.origin}${createRequestUrl('/api/calendar.ics')}?apikey=${window.Sportarr?.apiKey || ''}`}
                className="flex-1 rounded-lg bg-gray-900 px-3 py-2 text-sm text-gray-300 focus:outline-none"
                onFocus={(e) => e.target.select()}
              />
              <button
                onClick={() => {
                  // The clipboard API is only present on a secure origin, and
                  // a self-hosted install reached over plain http is not one, so
                  // this threw and the copy silently never happened. Fall back to
                  // the old selection trick there, and only report success once
                  // something actually worked.
                  const icalUrl = `${window.location.origin}${createRequestUrl('/api/calendar.ics')}?apikey=${window.Sportarr?.apiKey || ''}`;
                  copyToClipboard(icalUrl).then(copied => {
                    if (!copied) return;
                    setIcalCopied(true);
                    setTimeout(() => setIcalCopied(false), 3000);
                  });
                }}
                className="flex items-center gap-1.5 rounded-lg bg-red-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700"
              >
                {icalCopied ? (
                  <><ClipboardDocumentCheckIcon className="h-4 w-4" /> Copied</>
                ) : (
                  <><ClipboardDocumentIcon className="h-4 w-4" /> Copy</>
                )}
              </button>
            </div>
            <div className="space-y-2 text-xs text-gray-500">
              <p>Optional parameters: pastDays, futureDays, unmonitored, leagueId, asAllDay</p>
              <p>Example: ...calendar.ics?apikey=KEY&amp;pastDays=30&amp;futureDays=90</p>
            </div>
          </div>
        </div>
      )}
    </PageShell>
  );
}
