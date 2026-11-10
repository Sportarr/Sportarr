import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import CalendarPage from '../CalendarPage';

const calendarEvents = vi.hoisted(() => vi.fn());

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), put: vi.fn() } }));
vi.mock('../../api/hooks', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/hooks')>()),
  useCalendarEvents: (...args: unknown[]) => {
    calendarEvents(...args);
    return { data: [], isLoading: false, error: null };
  },
}));
vi.mock('../../hooks/useUISettings', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useUISettings')>()),
  useUISettings: () => ({ eventViewMode: 'spacious', timezone: 'UTC', queryBackoffCapMs: 0, theme: null, loading: false }),
}));
vi.mock('../../hooks/useSettings', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useSettings')>()),
  useSettings: <T,>(_key: string, defaultValue: T) => [defaultValue, vi.fn(), false],
}));
vi.mock('../../hooks/useMediaQuery', () => ({ useMediaQuery: () => false }));

const renderPage = () => render(
  <QueryClientProvider client={new QueryClient()}>
    <MemoryRouter><CalendarPage /></MemoryRouter>
  </QueryClientProvider>
);

beforeEach(() => {
  localStorage.clear();
  calendarEvents.mockReset();
});

afterEach(() => {
  localStorage.clear();
});

describe('calendar monitoring filter', () => {
  it('starts on Monitored Only when nothing was chosen before', async () => {
    renderPage();

    expect(await screen.findByLabelText('Monitoring filter')).toHaveValue('monitored');
    expect(calendarEvents).toHaveBeenLastCalledWith(expect.anything(), expect.anything(), false);
  });

  it('opens with the filter the user chose last time', async () => {
    const first = renderPage();
    fireEvent.change(await screen.findByLabelText('Monitoring filter'), { target: { value: 'all' } });
    first.unmount();

    renderPage();

    expect(await screen.findByLabelText('Monitoring filter')).toHaveValue('all');
    expect(calendarEvents).toHaveBeenLastCalledWith(expect.anything(), expect.anything(), true);
  });

  it('remembers Monitored Only after the Clear button', async () => {
    localStorage.setItem('sportarr.calendarMonitoring', 'all');
    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: 'Clear' }));

    expect(screen.getByLabelText('Monitoring filter')).toHaveValue('monitored');
    expect(localStorage.getItem('sportarr.calendarMonitoring')).toBe('monitored');
  });

  it('remembers a switch back to Monitored Only', async () => {
    localStorage.setItem('sportarr.calendarMonitoring', 'all');
    const first = renderPage();
    fireEvent.change(await screen.findByLabelText('Monitoring filter'), { target: { value: 'monitored' } });
    first.unmount();

    renderPage();

    expect(await screen.findByLabelText('Monitoring filter')).toHaveValue('monitored');
  });
});
