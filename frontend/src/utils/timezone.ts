// Timezone utility for converting UTC dates to user's configured timezone

/**
 * Convert a UTC date string to the user's configured timezone
 * @param utcDateString - UTC date string (ISO format)
 * @param timezone - User's configured timezone ID (e.g., 'America/New_York')
 * @returns Date object adjusted to the user's timezone
 */
export function convertToTimezone(utcDateString: string, timezone: string | null | undefined): Date {
  const utcDate = new Date(utcDateString);

  // If no timezone specified, return the date as-is (browser local time)
  if (!timezone) {
    return utcDate;
  }

  try {
    // Get the date components in the target timezone
    const options: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
      timeZone: timezone,
    };

    const formatter = new Intl.DateTimeFormat('en-US', options);
    const parts = formatter.formatToParts(utcDate);

    const getPart = (type: Intl.DateTimeFormatPartTypes) =>
      parts.find(p => p.type === type)?.value || '0';

    // Create a new date from the parts
    const year = parseInt(getPart('year'));
    const month = parseInt(getPart('month')) - 1; // JS months are 0-indexed
    const day = parseInt(getPart('day'));
    const hour = parseInt(getPart('hour'));
    const minute = parseInt(getPart('minute'));
    const second = parseInt(getPart('second'));

    return new Date(year, month, day, hour, minute, second);
  } catch (error) {
    console.error('Failed to convert timezone:', error);
    return utcDate;
  }
}

/**
 * Check if two dates are on the same day in the given timezone
 * @param date1 - First date (UTC)
 * @param date2 - Second date (UTC)
 * @param timezone - User's configured timezone ID
 */
export function isSameDayInTimezone(date1: Date, date2: Date, timezone: string | null | undefined): boolean {
  const converted1 = convertToTimezone(date1.toISOString(), timezone);
  const converted2 = convertToTimezone(date2.toISOString(), timezone);

  return converted1.getFullYear() === converted2.getFullYear() &&
         converted1.getMonth() === converted2.getMonth() &&
         converted1.getDate() === converted2.getDate();
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
  const utcDate = new Date(utcDateString);

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
  const utcDate = new Date(utcDateString);

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
