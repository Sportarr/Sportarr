import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import type { Event } from '../../types';
import CalendarPage from '../CalendarPage';

const event = (id: number, title: string, eventDate: string): Event =>
  ({ id, title, eventDate, sport: 'Soccer', leagueId: 7, monitored: true, hasFile: false, images: [] });

// 31 October is the day before the tapped day: the agenda used to list it first.
const events = [
  event(1, 'Bologna vs Monza', '2026-10-31T19:00:00Z'),
  event(2, 'Chelsea vs Manchester United', '2026-11-01T15:00:00Z'),
];

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), put: vi.fn() } }));
vi.mock('../../api/hooks', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/hooks')>()),
  useCalendarEvents: () => ({ data: events, isLoading: false, error: null }),
}));
vi.mock('../../hooks/useUISettings', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useUISettings')>()),
  useUISettings: () => ({ eventViewMode: 'spacious', timezone: 'UTC', queryBackoffCapMs: 0, theme: null, loading: false }),
}));
vi.mock('../../hooks/useSettings', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useSettings')>()),
  useSettings: <T,>(_key: string, defaultValue: T) => [defaultValue, vi.fn(), false],
}));
// A phone: the month grid shows dots and the whole cell is the tap target.
vi.mock('../../hooks/useMediaQuery', () => ({
  useMediaQuery: (query: string) => query === '(max-width: 639px)',
}));

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-11-02T12:00:00Z'));
  localStorage.clear();
  localStorage.setItem('sportarr.calendarViewChosen', '1');
  localStorage.setItem('sportarr.calendarView', 'month');
});

afterEach(() => {
  vi.useRealTimers();
});

describe('calendar day view', () => {
  it('opens exactly the tapped day from the phone month grid', async () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter><CalendarPage /></MemoryRouter>
      </QueryClientProvider>
    );

    fireEvent.click(await screen.findByTestId('calendar-day-open-2026-11-01'));

    const dayView = screen.getByTestId('calendar-day-view');
    expect(screen.getByText('Sun, Nov 1, 2026')).toBeInTheDocument();
    expect(within(dayView).getByText('Chelsea vs Manchester United')).toBeInTheDocument();
    expect(within(dayView).queryByText('Bologna vs Monza')).not.toBeInTheDocument();
  });
});
