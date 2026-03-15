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
function parseAsUtc(dateString: string): Date {
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
