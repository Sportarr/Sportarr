// Timezone utility for converting UTC dates to user's configured timezone

/**
 * Normalize a date string to ensure it's treated as UTC.
 * JavaScript's Date constructor treats strings without 'Z' suffix as local time,
 * but our backend sends UTC dates without the 'Z'. This function ensures
 * consistent UTC parsing.
 *
 * @param dateString - Date string from API (may or may not have Z suffix)
 * @returns Date object representing the UTC time
 */
export function parseAsUtc(dateString: string): Date {
  if (!dateString) return new Date();

  // If it already has timezone info (Z or +/-offset), parse as-is
  if (dateString.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(dateString)) {
    return new Date(dateString);
  }

  // If it's just a date (YYYY-MM-DD), append T00:00:00Z to treat as midnight UTC
  if (/^\d{4}-\d{2}-\d{2}$/.test(dateString)) {
    return new Date(dateString + 'T00:00:00Z');
  }

  // If it has time but no timezone (YYYY-MM-DDTHH:MM:SS), append Z to treat as UTC
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?(\.\d+)?$/.test(dateString)) {
    return new Date(dateString + 'Z');
  }

  // Fallback: parse as-is (may be treated as local time by browser)
  return new Date(dateString);
}

function getZonedDateParts(date: Date, timezone: string) {
  const formatter = new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
    timeZone: timezone,
  });
  const parts = formatter.formatToParts(date);

  const getPart = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find(part => part.type === type)?.value || '0';

  return {
    year: parseInt(getPart('year'), 10),
    month: parseInt(getPart('month'), 10) - 1, // JS months are 0-indexed
    day: parseInt(getPart('day'), 10),
    hour: parseInt(getPart('hour'), 10),
    minute: parseInt(getPart('minute'), 10),
    second: parseInt(getPart('second'), 10),
  };
}

/**
 * Convert a UTC date string to the user's configured timezone
 * @param utcDateString - UTC date string (ISO format)
 * @param timezone - User's configured timezone ID (e.g., 'America/New_York')
 * @returns Date object adjusted to the user's timezone
 */
export function convertToTimezone(utcDateString: string, timezone: string | null | undefined): Date {
  const utcDate = parseAsUtc(utcDateString);

  // If no timezone specified, return the date as-is (browser local time)
  if (!timezone) {
    return utcDate;
  }

  try {
    // Get the date components in the target timezone
    const { year, month, day, hour, minute, second } = getZonedDateParts(utcDate, timezone);

    // Create a new date from the parts
    return new Date(year, month, day, hour, minute, second);
  } catch (error) {
    console.error('Failed to convert timezone:', error);
    return utcDate;
  }
}

/**
 * Get the current date+time in the user's configured timezone as a local Date.
 * Useful for comparisons against convertToTimezone() results.
 */
export function getNowInTimezone(timezone: string | null | undefined): Date {
  const now = new Date();

  if (!timezone) return now;

  try {
    const { year, month, day, hour, minute, second } = getZonedDateParts(now, timezone);
    return new Date(year, month, day, hour, minute, second);
  } catch {
    return now;
  }
}

// Get "today" in the user's configured timezone
export function getTodayInTimezone(timezone: string | null | undefined): Date {
  const now = new Date();

  if (!timezone) {
    return new Date(now.getFullYear(), now.getMonth(), now.getDate());
  }

  try {
    // If timezone is set, get the current date in that timezone
    const { year, month, day } = getZonedDateParts(now, timezone);
    return new Date(year, month, day);
  } catch (error) {
    console.error('Failed to get today in timezone:', error);
    return new Date(now.getFullYear(), now.getMonth(), now.getDate());
  }
}

/**
 * Get the date portion (YYYY-MM-DD) of a UTC date in the user's timezone
 * @param utcDateString - UTC date string
 * @param timezone - User's configured timezone ID
 */
export function getDateInTimezone(utcDateString: string, timezone: string | null | undefined): string {
  const converted = convertToTimezone(utcDateString, timezone);
  const year = converted.getFullYear();
  const month = String(converted.getMonth() + 1).padStart(2, '0');
  const day = String(converted.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/**
 * Format time in user's timezone
 * @param utcDateString - UTC date string
 * @param timezone - User's configured timezone ID
 * @param options - Intl.DateTimeFormat options
 */
export function formatTimeInTimezone(
  utcDateString: string,
  timezone: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' }
): string {
  const utcDate = parseAsUtc(utcDateString);

  if (!timezone) {
    return utcDate.toLocaleTimeString([], options);
  }

  try {
    return utcDate.toLocaleTimeString([], { ...options, timeZone: timezone });
  } catch (error) {
    console.error('Failed to format time in timezone:', error);
    return utcDate.toLocaleTimeString([], options);
  }
}

/**
 * Format date in user's timezone
 * @param utcDateString - UTC date string
 * @param timezone - User's configured timezone ID
 * @param options - Intl.DateTimeFormat options
 */
export function formatDateInTimezone(
  utcDateString: string,
  timezone: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { weekday: 'short', month: 'short', day: 'numeric' }
): string {
  const utcDate = parseAsUtc(utcDateString);

  if (!timezone) {
    return utcDate.toLocaleDateString([], options);
  }

  try {
    return utcDate.toLocaleDateString([], { ...options, timeZone: timezone });
  } catch (error) {
    console.error('Failed to format date in timezone:', error);
    return utcDate.toLocaleDateString([], options);
  }
}

/**
 * Pick the right calendar date for user-facing display from an event.
 *
 * Returns broadcastDate when set (the broadcaster's branding date,
 * e.g. "Sunday Night Football" is dated to the Sunday in ET even when
 * the UTC scheduled_start rolls over to Monday), else falls back to
 * the UTC eventDate. Use this everywhere a user reads "when did this
 * happen" labels; use the raw eventDate for sort keys and "is past"
 * comparisons since those need an instant in time, not a calendar
 * date.
 *
 * NOTE: callers that format the result through `formatDateInTimezone`
 * with the user's UI timezone will silently shift broadcastDate by
 * the user's offset (broadcastDate is parsed as midnight UTC, then
 * re-rendered in user's TZ, which loses the broadcaster-anchored
 * calendar date). Use `formatEventDate(evt, userTimezone, options)`
 * instead — it skips the TZ shift when broadcastDate is present so
 * "Wednesday Aug 28" stays "Wednesday Aug 28" everywhere.
 */
export function eventDisplayDate(evt: { broadcastDate?: string | null; eventDate: string }): string {
  return evt.broadcastDate || evt.eventDate;
}


/**
 * Render the event's user-facing date label.
 *
 * Rule:
 *   broadcastDate present  ->  format as the broadcaster's calendar
 *                              date with NO TZ shift (treat the date
 *                              as a naked calendar value). The user's
 *                              UI timezone is ignored on purpose: a
 *                              broadcaster's "Wednesday Night AEW"
 *                              is Wednesday wherever the viewer lives.
 *   broadcastDate missing  ->  fall back to UTC eventDate rendered in
 *                              the user's UI timezone (existing
 *                              behavior; relevant only when an event's
 *                              league hasn't been mapped to a TZ).
 */
export function formatEventDate(
  evt: { broadcastDate?: string | null; eventDate: string },
  userTimezone: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { weekday: 'short', month: 'short', day: 'numeric' }
): string {
  if (evt.broadcastDate) {
    // Parse as midnight UTC and render in UTC so the calendar date
    // round-trips byte-for-byte (no offset is ever subtracted).
    const utcMidnight = parseAsUtc(evt.broadcastDate);
    return utcMidnight.toLocaleDateString([], { ...options, timeZone: 'UTC' });
  }
  return formatDateInTimezone(evt.eventDate, userTimezone, options);
}

/**
 * Format a UTC date string as a human-readable relative time ("Just now", "5m ago", "2h ago", "3d ago")
 * or an absolute date for older timestamps. Correctly handles backend UTC dates that may lack
 * the Z suffix, preventing phantom timezone offsets in the displayed time.
 */
export function formatRelativeDate(dateString: string): string {
  const date = parseAsUtc(dateString);
  const now = new Date();
  const diff = now.getTime() - date.getTime();
  const minutes = Math.floor(diff / 60000);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (minutes < 1) return 'Just now';
  if (minutes < 60) return `${minutes}m ago`;
  if (hours < 24) return `${hours}h ago`;
  if (days < 7) return `${days}d ago`;
  return date.toLocaleDateString();
}
