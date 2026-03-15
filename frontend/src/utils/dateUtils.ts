export type FirstDayOfWeek = 'sunday' | 'monday';

export interface CalendarDay {
  date: Date;
  isCurrentMonth: boolean;
}

const WEEKDAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

const getWeekStartOffset = (firstDayOfWeek: FirstDayOfWeek) => {
  return firstDayOfWeek === 'monday' ? 1 : 0;
};

export const addDays = (date: Date, days: number) => {
  const nextDate = new Date(date);
  nextDate.setDate(nextDate.getDate() + days);
  return nextDate;
};

export const addMonths = (date: Date, months: number) => {
  return new Date(date.getFullYear(), date.getMonth() + months, 1);
};

export const startOfMonth = (date: Date) => {
  return new Date(date.getFullYear(), date.getMonth(), 1);
};

const endOfMonth = (date: Date) => {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0);
};

// Get the start of the week (respecting the configured first day of week)
export const startOfWeek = (date: Date, firstDayOfWeek: FirstDayOfWeek) => {
  const weekStartOffset = getWeekStartOffset(firstDayOfWeek);
  const dayOfWeek = date.getDay(); // 0 = Sunday, 6 = Saturday
  return addDays(date, -((dayOfWeek - weekStartOffset + 7) % 7));
};

export const endOfWeek = (date: Date, firstDayOfWeek: FirstDayOfWeek) => {
  return addDays(startOfWeek(date, firstDayOfWeek), 6);
};

export const getWeekdayNames = (firstDayOfWeek: FirstDayOfWeek) => {
  const startIndex = getWeekStartOffset(firstDayOfWeek);
  return Array.from({ length: 7 }, (_, index) => WEEKDAY_NAMES[(startIndex + index) % 7]);
};

export const getCalendarWeeks = (monthDate: Date, firstDayOfWeek: FirstDayOfWeek): CalendarDay[][] => {
  const monthStart = startOfMonth(monthDate);
  const rangeEnd = endOfWeek(endOfMonth(monthDate), firstDayOfWeek);
  const weeks: CalendarDay[][] = [];

  for (let currentDate = startOfWeek(monthStart, firstDayOfWeek); currentDate <= rangeEnd; currentDate = addDays(currentDate, 7)) {
    weeks.push(Array.from({ length: 7 }, (_, index) => {
      const date = addDays(currentDate, index);
      return {
        date,
        isCurrentMonth: date.getMonth() === monthDate.getMonth() && date.getFullYear() === monthDate.getFullYear(),
      };
    }));
  }

  return weeks;
};

// Get array of 7 days for the week (respecting the configured first day of week)
export const getWeekDays = (date: Date, firstDayOfWeek: FirstDayOfWeek) => {
  const weekStart = startOfWeek(date, firstDayOfWeek);
  return Array.from({ length: 7 }, (_, index) => addDays(weekStart, index));
};

export const getAgendaRange = (date: Date) => {
  return {
    start: addDays(date, -1),
    end: addMonths(date, 1),
  };
};

export const formatDateInputValue = (date: Date) => {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
};

export const formatMonthLabel = (date: Date) => {
  return new Intl.DateTimeFormat('en-US', {
    month: 'long',
    year: 'numeric',
  }).format(date);
};

export const formatWeekLabel = (days: Date[]) => {
  const start = days[0];
  const end = days[days.length - 1];

  if (start.getMonth() === end.getMonth() && start.getFullYear() === end.getFullYear()) {
    return `${start.toLocaleDateString('en-US', { month: 'long' })} ${start.getDate()} - ${end.getDate()}, ${end.getFullYear()}`;
  }

  if (start.getFullYear() === end.getFullYear()) {
    return `${start.toLocaleDateString('en-US', { month: 'short' })} ${start.getDate()} - ${end.toLocaleDateString('en-US', { month: 'short' })} ${end.getDate()}, ${end.getFullYear()}`;
  }

  return `${start.toLocaleDateString('en-US', { month: 'short' })} ${start.getDate()}, ${start.getFullYear()} - ${end.toLocaleDateString('en-US', { month: 'short' })} ${end.getDate()}, ${end.getFullYear()}`;
};
