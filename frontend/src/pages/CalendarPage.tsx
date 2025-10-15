import { useEvents } from '../api/hooks';
import { ChevronLeftIcon, ChevronRightIcon } from '@heroicons/react/24/outline';
import { useState } from 'react';
import type { Event } from '../types';

export default function CalendarPage() {
  const { data: events, isLoading, error } = useEvents();
  const [currentWeekOffset, setCurrentWeekOffset] = useState(0);

  // Get the start of the current week (Sunday)
  const getWeekStart = (offset: number = 0) => {
    const today = new Date();
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

  // Filter events for a specific day
  const getEventsForDay = (date: Date, allEvents: Event[] | undefined) => {
    if (!allEvents) return [];

    const dayStart = new Date(date);
    dayStart.setHours(0, 0, 0, 0);
    const dayEnd = new Date(date);
    dayEnd.setHours(23, 59, 59, 999);

    return allEvents.filter(event => {
      if (!event.monitored) return false; // Only show monitored events
      const eventDate = new Date(event.eventDate);
      return eventDate >= dayStart && eventDate <= dayEnd;
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

  const isToday = (date: Date) => {
    const today = new Date();
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
    <div className="p-8">
      <div className="max-w-7xl mx-auto">
        {/* Header */}
        <div className="mb-6 flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold text-white mb-2">Calendar</h1>
            <p className="text-gray-400">
              View your monitored MMA events for the week
            </p>
          </div>

          {/* Week Navigation */}
          <div className="flex items-center gap-4">
            <button
              onClick={() => setCurrentWeekOffset(currentWeekOffset - 1)}
              className="p-2 hover:bg-red-900/20 rounded-lg transition-colors"
              title="Previous week"
            >
              <ChevronLeftIcon className="w-6 h-6 text-gray-400 hover:text-white" />
            </button>

            <div className="text-center min-w-[200px]">
              <p className="text-lg font-semibold text-white">{formatWeekRange()}</p>
              {currentWeekOffset === 0 && (
                <p className="text-sm text-red-400">Current Week</p>
              )}
            </div>

            <button
              onClick={() => setCurrentWeekOffset(currentWeekOffset + 1)}
              className="p-2 hover:bg-red-900/20 rounded-lg transition-colors"
              title="Next week"
            >
              <ChevronRightIcon className="w-6 h-6 text-gray-400 hover:text-white" />
            </button>
          </div>
        </div>

        {/* Calendar Grid */}
        <div className="grid grid-cols-7 gap-2">
          {weekDays.map((day, index) => {
            const dayEvents = getEventsForDay(day, events);
            const today = isToday(day);

            return (
              <div
                key={day.toISOString()}
                className={`bg-gradient-to-br from-gray-900 to-black border rounded-lg overflow-hidden min-h-[200px] ${
                  today ? 'border-red-600 shadow-lg shadow-red-900/30' : 'border-red-900/30'
                }`}
              >
                {/* Day Header */}
                <div className={`px-3 py-2 border-b ${today ? 'bg-red-950/40 border-red-900/40' : 'bg-gray-800/30 border-red-900/20'}`}>
                  <div className="text-xs text-gray-400 font-medium">
                    {dayNames[index]}
                  </div>
                  <div className={`text-lg font-bold ${today ? 'text-red-400' : 'text-white'}`}>
                    {day.getDate()}
                  </div>
                </div>

                {/* Events for the day */}
                <div className="p-2 space-y-2">
                  {dayEvents.length > 0 ? (
                    dayEvents.map(event => (
                      <div
                        key={event.id}
                        className="bg-red-900/20 hover:bg-red-900/30 border border-red-900/40 rounded p-2 transition-colors cursor-pointer group"
                      >
                        <div className="flex items-start gap-2">
                          {/* Event Thumbnail */}
                          {event.images?.[0] && (
                            <div className="w-8 h-10 bg-gray-950 rounded overflow-hidden flex-shrink-0">
                              <img
                                src={event.images[0].remoteUrl}
                                alt={event.title}
                                className="w-full h-full object-cover"
                              />
                            </div>
                          )}

                          {/* Event Details */}
                          <div className="flex-1 min-w-0">
                            <p className="text-xs font-semibold text-white line-clamp-2 group-hover:text-red-300 transition-colors">
                              {event.title}
                            </p>
                            <p className="text-xs text-red-400 font-medium mt-1">
                              {event.organization}
                            </p>
                            {event.venue && (
                              <p className="text-xs text-gray-500 line-clamp-1 mt-0.5">
                                {event.venue}
                              </p>
                            )}
                            {event.hasFile && (
                              <span className="inline-block mt-1 px-1.5 py-0.5 bg-green-600/20 text-green-400 text-xs rounded">
                                ✓
                              </span>
                            )}
                          </div>
                        </div>
                      </div>
                    ))
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
        <div className="mt-6 flex items-center gap-6 text-sm text-gray-400">
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 bg-red-600 rounded"></div>
            <span>Today</span>
          </div>
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 bg-green-600 rounded"></div>
            <span>Downloaded</span>
          </div>
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 bg-red-900/40 rounded border border-red-900/60"></div>
            <span>Monitored</span>
          </div>
        </div>
      </div>
    </div>
  );
}
