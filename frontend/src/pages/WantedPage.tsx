import React, { useState, useEffect } from 'react';
import { toast } from 'sonner';
import { MagnifyingGlassIcon, ExclamationCircleIcon, ClockIcon } from '@heroicons/react/24/outline';

type TabType = 'missing' | 'cutoff-unmet';

interface Event {
  id: number;
  title: string;
  organization: string;
  eventDate: string;
  venue?: string;
  location?: string;
  monitored: boolean;
  hasFile: boolean;
  filePath?: string;
  quality?: string;
  images: string[];
  fights: Fight[];
  added: string;
  lastUpdate?: string;
}

interface Fight {
  id: number;
  fighter1: string;
  fighter2: string;
  weightClass?: string;
  isMainEvent: boolean;
}

interface WantedResponse {
  events: Event[];
  page: number;
  pageSize: number;
  totalRecords: number;
}

const WantedPage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<TabType>('missing');
  const [missingEvents, setMissingEvents] = useState<Event[]>([]);
  const [cutoffUnmetEvents, setCutoffUnmetEvents] = useState<Event[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const pageSize = 20;

  const totalPages = Math.ceil(totalRecords / pageSize);

  useEffect(() => {
    fetchWantedEvents();
  }, [activeTab, currentPage]);

  const fetchWantedEvents = async () => {
    setLoading(true);
    setError(null);
    try {
      const endpoint = activeTab === 'missing'
        ? `/api/wanted/missing?page=${currentPage}&pageSize=${pageSize}`
        : `/api/wanted/cutoff-unmet?page=${currentPage}&pageSize=${pageSize}`;

      const response = await fetch(endpoint);
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

  const handleSearch = async (eventId: number) => {
    try {
      const response = await fetch(`/api/event/${eventId}/search`, { method: 'POST' });
      if (!response.ok) throw new Error('Failed to trigger search');
      toast.success('Search Started', {
        description: 'Searching indexers for this event.',
      });
    } catch (err) {
      toast.error('Search Failed', {
        description: err instanceof Error ? err.message : 'Failed to trigger search.',
      });
    }
  };

  const handleToggleMonitored = async (event: Event) => {
    try {
      const response = await fetch(`/api/event/${event.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...event, monitored: !event.monitored })
      });
      if (!response.ok) throw new Error('Failed to update event');
      fetchWantedEvents();
    } catch (err) {
      toast.error('Update Failed', {
        description: err instanceof Error ? err.message : 'Failed to update event.',
      });
    }
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const formatRelativeTime = (dateString: string) => {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = date.getTime() - now.getTime();
    const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));

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
    const mainEvent = event.fights.find(f => f.isMainEvent);
    const isPastEvent = new Date(event.eventDate) < new Date();

    return (
      <div
        key={event.id}
        className="bg-gray-800 rounded-lg p-4 hover:bg-gray-750 transition-colors border border-gray-700"
      >
        <div className="flex items-start justify-between">
          <div className="flex-1">
            <div className="flex items-center gap-3 mb-2">
              <h3 className="text-lg font-semibold text-white">{event.title}</h3>
              <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded">
                {event.organization}
              </span>
              {isPastEvent && (
                <span className="px-2 py-1 bg-red-900/30 text-red-400 text-xs rounded flex items-center gap-1">
                  <ClockIcon className="w-3 h-3" />
                  Past Event
                </span>
              )}
            </div>

            {mainEvent && (
              <p className="text-sm text-gray-400 mb-2">
                Main Event: {mainEvent.fighter1} vs {mainEvent.fighter2}
                {mainEvent.weightClass && ` (${mainEvent.weightClass})`}
              </p>
            )}

            <div className="flex items-center gap-4 text-sm text-gray-500">
              <span>{formatDate(event.eventDate)}</span>
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
            <button
              onClick={() => handleSearch(event.id)}
              className="p-2 bg-blue-900/30 text-blue-400 rounded hover:bg-blue-900/50 transition-colors"
              title="Search for event"
            >
              <MagnifyingGlassIcon className="w-5 h-5" />
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
          Showing {((currentPage - 1) * pageSize) + 1} to {Math.min(currentPage * pageSize, totalRecords)} of {totalRecords} events
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
            disabled={currentPage === 1}
            className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Previous
          </button>
          <span className="px-3 py-1 bg-gray-800 text-white rounded">
            Page {currentPage} of {totalPages}
          </span>
          <button
            onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
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
    <div className="p-6">
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
                <strong className="text-white">Missing Events:</strong> These are monitored events that don't have any files yet.
                Click the search icon to manually search for them, or wait for the RSS sync to find releases automatically.
              </>
            ) : (
              <>
                <strong className="text-white">Cutoff Unmet:</strong> These events have files, but the quality is below your configured cutoff.
                Fightarr will continue searching for better quality releases to upgrade them.
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
    </div>
  );
};

export default WantedPage;
