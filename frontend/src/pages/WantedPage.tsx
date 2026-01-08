import React, { useState, useEffect, useCallback } from 'react';
import { toast } from 'sonner';
import {
  MagnifyingGlassIcon,
  ExclamationCircleIcon,
  ClockIcon,
  UserIcon,
  QueueListIcon,
} from '@heroicons/react/24/outline';
import ManualSearchModal from '../components/ManualSearchModal';
import { useSearchQueueStatus } from '../api/hooks';
import { apiGet, apiPost, apiPut } from '../utils/api';
import { formatTimeInTimezone } from '../utils/timezone';
import { useTimezone } from '../hooks/useTimezone';

type TabType = 'missing' | 'cutoff-unmet';

interface Event {
  id: number;
  title: string;
  sport: string;
  leagueId?: number;
  leagueName?: string;
  homeTeamId?: number;
  homeTeamName?: string;
  awayTeamId?: number;
  awayTeamName?: string;
  season?: string;
  round?: string;
  eventDate: string;
  venue?: string;
  location?: string;
  broadcast?: string;
  monitored: boolean;
  hasFile: boolean;
  filePath?: string;
  quality?: string;
  qualityProfileId?: number;
  images: string[];
  added: string;
  lastUpdate?: string;
  homeScore?: string;
  awayScore?: string;
  status?: string;
}

interface WantedResponse {
  events: Event[];
  page: number;
  pageSize: number;
  totalRecords: number;
}

// Track locally queued searches (before server confirms)
interface PendingSearch {
  eventId: number;
  queuedAt: number;
}

const WantedPage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<TabType>('missing');
  const [missingEvents, setMissingEvents] = useState<Event[]>([]);
  const [cutoffUnmetEvents, setCutoffUnmetEvents] = useState<Event[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const { timezone } = useTimezone();
  const pageSize = 20;

  // Manual search modal state
  const [manualSearchModal, setManualSearchModal] = useState<{
    isOpen: boolean;
    eventId: number;
    eventTitle: string;
  }>({
    isOpen: false,
    eventId: 0,
    eventTitle: '',
  });

  // Track locally pending searches (for immediate UI feedback)
  const [pendingSearches, setPendingSearches] = useState<PendingSearch[]>([]);

  // Get server-side search queue status
  const { data: searchQueue } = useSearchQueueStatus();

  const totalPages = Math.ceil(totalRecords / pageSize);

  // Clean up stale pending searches (older than 10 seconds or confirmed by server)
  useEffect(() => {
    const interval = setInterval(() => {
      const now = Date.now();
      setPendingSearches((prev) =>
        prev.filter((p) => {
          // Remove if older than 10 seconds
          if (now - p.queuedAt > 10000) return false;
          // Remove if server has confirmed (in pending or active)
          if (searchQueue) {
            const inPending = searchQueue.pendingSearches?.some((s) => s.eventId === p.eventId);
            const inActive = searchQueue.activeSearches?.some((s) => s.eventId === p.eventId);
            if (inPending || inActive) return false;
          }
          return true;
        })
      );
    }, 1000);
    return () => clearInterval(interval);
  }, [searchQueue]);

  useEffect(() => {
    fetchWantedEvents();
  }, [activeTab, currentPage]);

  const fetchWantedEvents = async () => {
    setLoading(true);
    setError(null);
    try {
      const endpoint =
        activeTab === 'missing'
          ? `/api/wanted/missing?page=${currentPage}&pageSize=${pageSize}`
          : `/api/wanted/cutoff-unmet?page=${currentPage}&pageSize=${pageSize}`;

      const response = await apiGet(endpoint);
      if (!response.ok) throw new Error('Failed to fetch wanted events');
      const data: WantedResponse = await response.json();

      if (activeTab === 'missing') {
        setMissingEvents(data.events);
      } else {
        setCutoffUnmetEvents(data.events);
      }
      setTotalRecords(data.totalRecords);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setLoading(false);
    }
  };

  // Check if an event has a search in progress (local pending, server pending, or active)
  const getSearchStatus = useCallback(
    (eventId: number): 'idle' | 'queued' | 'searching' => {
      // Check local pending first (immediate feedback)
      if (pendingSearches.some((p) => p.eventId === eventId)) {
        return 'queued';
      }
      // Check server-side queue
      if (searchQueue) {
        if (searchQueue.activeSearches?.some((s) => s.eventId === eventId)) {
          return 'searching';
        }
        if (searchQueue.pendingSearches?.some((s) => s.eventId === eventId)) {
          return 'queued';
        }
      }
      return 'idle';
    },
    [pendingSearches, searchQueue]
  );

  const handleSearch = async (eventId: number, eventTitle: string) => {
    const status = getSearchStatus(eventId);

    // If already searching or queued, show feedback but don't re-queue
    if (status === 'searching') {
      toast.info('Search in progress', {
        description: `Already searching for "${eventTitle}"`,
      });
      return;
    }

    if (status === 'queued') {
      toast.info('Search already queued', {
        description: `"${eventTitle}" is waiting in queue`,
      });
      return;
    }

    // Add to local pending immediately for instant UI feedback
    setPendingSearches((prev) => [...prev, { eventId, queuedAt: Date.now() }]);

    try {
      // Use search queue API so search appears in sidebar widget
      const response = await apiPost('/api/search/queue', { eventId });
      if (!response.ok) {
        // Remove from local pending on error
        setPendingSearches((prev) => prev.filter((p) => p.eventId !== eventId));
        throw new Error('Failed to trigger search');
      }
      // Success - local pending will be cleaned up when server confirms
      toast.success('Search Queued', {
        description: 'Search added to queue. Check sidebar for progress.',
      });
    } catch (err) {
      // Remove from local pending on error
      setPendingSearches((prev) => prev.filter((p) => p.eventId !== eventId));
      toast.error('Search Failed', {
        description: err instanceof Error ? err.message : 'Failed to queue search.',
      });
    }
  };

  const handleManualSearch = (eventId: number, eventTitle: string) => {
    setManualSearchModal({
      isOpen: true,
      eventId,
      eventTitle,
    });
  };

  const handleToggleMonitored = async (event: Event) => {
    try {
      const response = await apiPut(`/api/events/${event.id}`, { monitored: !event.monitored });
      if (!response.ok) throw new Error('Failed to update event');
      toast.success('Event Updated', {
        description: `${event.title} is now ${!event.monitored ? 'monitored' : 'unmonitored'}.`,
      });
      fetchWantedEvents();
    } catch (err) {
      toast.error('Update Failed', {
        description: err instanceof Error ? err.message : 'Failed to update event.',
      });
    }
  };

  const formatRelativeTime = (dateString: string) => {
    const date = new Date(dateString + 'Z');
    const now = new Date();

    const dateOnly = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    const todayOnly = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const diffMs = dateOnly.getTime() - todayOnly.getTime();
    const diffDays = diffMs / (1000 * 60 * 60 * 24);

    if (diffDays < 0) {
      return `${Math.abs(diffDays)} days ago`;
    } else if (diffDays === 0) {
      return 'Today';
    } else if (diffDays === 1) {
      return 'Tomorrow';
    } else {
      return `In ${diffDays} days`;
    }
  };

  const renderEventCard = (event: Event) => {
    const isPastEvent = new Date(event.eventDate) < new Date();
    const matchup =
      event.homeTeamName && event.awayTeamName
        ? `${event.homeTeamName} vs ${event.awayTeamName}`
        : null;
    const searchStatus = getSearchStatus(event.id);

    return (
      <div
        key={event.id}
        className="bg-gray-800 rounded-lg p-4 hover:bg-gray-750 transition-colors border border-gray-700"
      >
        <div className="flex items-start justify-between">
          <div className="flex-1">
            <div className="flex items-center gap-3 mb-2">
              <h3 className="text-lg font-semibold text-white">{event.title}</h3>
              {event.leagueName && (
                <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded">
                  {event.leagueName}
                </span>
              )}
              <span className="px-2 py-1 bg-red-600/20 text-red-400 text-xs rounded">
                {event.sport}
              </span>
              {isPastEvent && (
                <span className="px-2 py-1 bg-red-900/30 text-red-400 text-xs rounded flex items-center gap-1">
                  <ClockIcon className="w-3 h-3" />
                  Past Event
                </span>
              )}
              {/* Search status indicator */}
              {searchStatus === 'queued' && (
                <span className="px-2 py-1 bg-yellow-900/30 text-yellow-400 text-xs rounded flex items-center gap-1 animate-pulse">
                  <QueueListIcon className="w-3 h-3" />
                  Queued
                </span>
              )}
              {searchStatus === 'searching' && (
                <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded flex items-center gap-1">
                  <MagnifyingGlassIcon className="w-3 h-3 animate-spin" />
                  Searching...
                </span>
              )}
            </div>

            {matchup && (
              <p className="text-sm text-gray-400 mb-2">
                {matchup}
                {event.round && ` - ${event.round}`}
              </p>
            )}

            <div className="flex items-center gap-4 text-sm text-gray-500">
              <span>{formatTimeInTimezone(event.eventDate, timezone, {
                  year: 'numeric',
                  month: 'short',
                  day: 'numeric',
                  hour: '2-digit',
                  minute: '2-digit'
                })}</span>
              <span className="text-gray-600">•</span>
              <span className={isPastEvent ? 'text-red-400' : 'text-blue-400'}>
                {formatRelativeTime(event.eventDate)}
              </span>
              {event.location && (
                <>
                  <span className="text-gray-600">•</span>
                  <span>{event.location}</span>
                </>
              )}
            </div>

            {activeTab === 'cutoff-unmet' && event.quality && (
              <div className="mt-2">
                <span className="text-xs text-yellow-400">
                  Current Quality: {event.quality} (Below cutoff)
                </span>
              </div>
            )}
          </div>

          <div className="flex items-center gap-2 ml-4">
            <button
              onClick={() => handleToggleMonitored(event)}
              className={`px-3 py-1 rounded text-sm transition-colors ${
                event.monitored
                  ? 'bg-green-900/30 text-green-400 hover:bg-green-900/50'
                  : 'bg-gray-700 text-gray-400 hover:bg-gray-600'
              }`}
            >
              {event.monitored ? 'Monitored' : 'Unmonitored'}
            </button>
            {/* Manual Search Button */}
            <button
              onClick={() => handleManualSearch(event.id, event.title)}
              className="p-2 bg-gray-700 text-gray-300 rounded hover:bg-gray-600 transition-colors"
              title="Manual Search - Browse and select from available releases"
            >
              <UserIcon className="w-5 h-5" />
            </button>
            {/* Auto Search Button */}
            <button
              onClick={() => handleSearch(event.id, event.title)}
              disabled={searchStatus !== 'idle'}
              className={`p-2 rounded transition-colors ${
                searchStatus === 'idle'
                  ? 'bg-blue-900/30 text-blue-400 hover:bg-blue-900/50'
                  : searchStatus === 'queued'
                    ? 'bg-yellow-900/30 text-yellow-400 cursor-not-allowed'
                    : 'bg-blue-900/50 text-blue-300 cursor-not-allowed'
              }`}
              title={
                searchStatus === 'idle'
                  ? 'Auto Search - Queue automatic search for event'
                  : searchStatus === 'queued'
                    ? 'Search queued - waiting to start'
                    : 'Search in progress'
              }
            >
              {searchStatus === 'searching' ? (
                <MagnifyingGlassIcon className="w-5 h-5 animate-spin" />
              ) : searchStatus === 'queued' ? (
                <QueueListIcon className="w-5 h-5" />
              ) : (
                <MagnifyingGlassIcon className="w-5 h-5" />
              )}
            </button>
          </div>
        </div>
      </div>
    );
  };

  const renderPagination = () => {
    if (totalPages <= 1) return null;

    return (
      <div className="flex items-center justify-between mt-6">
        <div className="text-sm text-gray-400">
          Showing {(currentPage - 1) * pageSize + 1} to{' '}
          {Math.min(currentPage * pageSize, totalRecords)} of {totalRecords} events
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
            disabled={currentPage === 1}
            className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Previous
          </button>
          <span className="px-3 py-1 bg-gray-800 text-white rounded">
            Page {currentPage} of {totalPages}
          </span>
          <button
            onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
            disabled={currentPage === totalPages}
            className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Next
          </button>
        </div>
      </div>
    );
  };

  return (
    <div className="p-8">
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-white mb-2">Wanted</h1>
        <p className="text-gray-400">
          Events that are monitored but missing files or below quality cutoff
        </p>
      </div>

      {/* Tabs */}
      <div className="flex gap-4 mb-6 border-b border-gray-700">
        <button
          onClick={() => {
            setActiveTab('missing');
            setCurrentPage(1);
          }}
          className={`px-4 py-2 font-medium transition-colors ${
            activeTab === 'missing'
              ? 'text-blue-400 border-b-2 border-blue-400'
              : 'text-gray-400 hover:text-white'
          }`}
        >
          Missing ({activeTab === 'missing' ? totalRecords : '...'})
        </button>
        <button
          onClick={() => {
            setActiveTab('cutoff-unmet');
            setCurrentPage(1);
          }}
          className={`px-4 py-2 font-medium transition-colors ${
            activeTab === 'cutoff-unmet'
              ? 'text-blue-400 border-b-2 border-blue-400'
              : 'text-gray-400 hover:text-white'
          }`}
        >
          Cutoff Unmet ({activeTab === 'cutoff-unmet' ? totalRecords : '...'})
        </button>
      </div>

      {/* Info Box */}
      <div className="mb-6 p-4 bg-blue-900/20 border border-blue-800 rounded-lg">
        <div className="flex items-start gap-3">
          <ExclamationCircleIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-gray-300">
            {activeTab === 'missing' ? (
              <>
                <strong className="text-white">Missing Events:</strong> These are monitored events
                that don't have any files yet. Click the search icon to automatically search, or
                the person icon to manually browse and select releases.
              </>
            ) : (
              <>
                <strong className="text-white">Cutoff Unmet:</strong> These events have files, but
                the quality is below your configured cutoff. Sportarr will continue searching for
                better quality releases to upgrade them.
              </>
            )}
          </div>
        </div>
      </div>

      {/* Content */}
      {loading ? (
        <div className="flex items-center justify-center py-12">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-400"></div>
          <span className="ml-3 text-gray-400">Loading wanted events...</span>
        </div>
      ) : error ? (
        <div className="bg-red-900/20 border border-red-800 rounded-lg p-4">
          <p className="text-red-400">Error: {error}</p>
        </div>
      ) : (
        <>
          {activeTab === 'missing' && (
            <div className="space-y-3">
              {missingEvents.length === 0 ? (
                <div className="text-center py-12 text-gray-400">
                  <ExclamationCircleIcon className="w-12 h-12 mx-auto mb-3 opacity-50" />
                  <p>No missing events! All monitored events have files.</p>
                </div>
              ) : (
                missingEvents.map(renderEventCard)
              )}
            </div>
          )}

          {activeTab === 'cutoff-unmet' && (
            <div className="space-y-3">
              {cutoffUnmetEvents.length === 0 ? (
                <div className="text-center py-12 text-gray-400">
                  <ExclamationCircleIcon className="w-12 h-12 mx-auto mb-3 opacity-50" />
                  <p>No events below quality cutoff! All files meet your quality standards.</p>
                </div>
              ) : (
                cutoffUnmetEvents.map(renderEventCard)
              )}
            </div>
          )}

          {renderPagination()}
        </>
      )}

      {/* Manual Search Modal */}
      <ManualSearchModal
        isOpen={manualSearchModal.isOpen}
        onClose={() => setManualSearchModal({ ...manualSearchModal, isOpen: false })}
        eventId={manualSearchModal.eventId}
        eventTitle={manualSearchModal.eventTitle}
      />
    </div>
  );
};

export default WantedPage;
