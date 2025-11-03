import React, { useState, useEffect } from 'react';
import { toast } from 'sonner';
import { TrashIcon, InformationCircleIcon } from '@heroicons/react/24/outline';

interface SystemEvent {
  id: number;
  timestamp: string;
  type: number; // 0=Info, 1=Success, 2=Warning, 3=Error
  category: number;
  message: string;
  details?: string;
  relatedEntityId?: number;
  relatedEntityType?: string;
  user?: string;
}

const SystemEventsPage: React.FC = () => {
  const [events, setEvents] = useState<SystemEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [selectedType, setSelectedType] = useState<string>('');
  const [selectedCategory, setSelectedCategory] = useState<string>('');
  const pageSize = 50;

  const totalPages = Math.ceil(totalRecords / pageSize);

  const eventTypes = ['Info', 'Success', 'Warning', 'Error'];
  const eventCategories = [
    'System', 'Database', 'Download', 'Import', 'Indexer', 'Search',
    'Quality', 'Backup', 'Update', 'Settings', 'Authentication', 'Task',
    'Notification', 'Metadata'
  ];

  const typeColors = [
    'text-blue-400 bg-blue-900/20',    // Info
    'text-green-400 bg-green-900/20',  // Success
    'text-yellow-400 bg-yellow-900/20', // Warning
    'text-red-400 bg-red-900/20'       // Error
  ];

  useEffect(() => {
    fetchEvents();
  }, [currentPage, selectedType, selectedCategory]);

  const fetchEvents = async () => {
    setLoading(true);
    setError(null);
    try {
      let url = `/api/system/event?page=${currentPage}&pageSize=${pageSize}`;
      if (selectedType) url += `&type=${selectedType}`;
      if (selectedCategory) url += `&category=${selectedCategory}`;

      const response = await fetch(url);
      if (!response.ok) throw new Error('Failed to fetch system events');
      const data = await response.json();

      setEvents(data.events);
      setTotalRecords(data.totalRecords);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setLoading(false);
    }
  };

  const handleCleanup = async () => {
    if (!confirm('Delete system events older than 30 days?')) return;

    try {
      const response = await fetch('/api/system/event/cleanup?days=30', { method: 'POST' });
      if (!response.ok) throw new Error('Failed to cleanup events');

      const result = await response.json();
      toast.success('Cleanup Complete', {
        description: result.message,
      });
      fetchEvents();
    } catch (err) {
      toast.error('Cleanup Failed', {
        description: err instanceof Error ? err.message : 'Failed to cleanup events.',
      });
    }
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  };

  return (
    <div className="p-6">
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-white mb-2">System Events</h1>
        <p className="text-gray-400">
          Audit trail of system operations and user actions
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-6 p-4 bg-blue-900/20 border border-blue-800 rounded-lg">
        <div className="flex items-start gap-3">
          <InformationCircleIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-gray-300">
            <strong className="text-white">System Events:</strong> This page shows an audit trail of system operations.
            For detailed application logs, visit the <a href="/system/logs" className="text-blue-400 hover:underline">Log Files</a> page.
          </div>
        </div>
      </div>

      {/* Filters */}
      <div className="mb-6 flex gap-4 items-center">
        <select
          value={selectedType}
          onChange={(e) => { setSelectedType(e.target.value); setCurrentPage(1); }}
          className="px-4 py-2 bg-gray-700 border border-gray-600 rounded text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="">All Types</option>
          {eventTypes.map((type) => (
            <option key={type} value={type}>{type}</option>
          ))}
        </select>

        <select
          value={selectedCategory}
          onChange={(e) => { setSelectedCategory(e.target.value); setCurrentPage(1); }}
          className="px-4 py-2 bg-gray-700 border border-gray-600 rounded text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="">All Categories</option>
          {eventCategories.map((cat) => (
            <option key={cat} value={cat}>{cat}</option>
          ))}
        </select>

        <button
          onClick={handleCleanup}
          className="ml-auto px-4 py-2 bg-red-900/30 text-red-400 rounded hover:bg-red-900/50 flex items-center gap-2"
        >
          <TrashIcon className="w-4 h-4" />
          Cleanup Old Events
        </button>
      </div>

      {/* Error Display */}
      {error && (
        <div className="mb-6 p-4 bg-red-900/20 border border-red-800 rounded-lg">
          <p className="text-red-400">Error: {error}</p>
        </div>
      )}

      {/* Events List */}
      <div className="bg-gray-800 rounded-lg border border-gray-700">
        {loading ? (
          <div className="flex items-center justify-center py-12">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-400"></div>
            <span className="ml-3 text-gray-400">Loading events...</span>
          </div>
        ) : events.length === 0 ? (
          <div className="text-center py-12 text-gray-400">
            <InformationCircleIcon className="w-12 h-12 mx-auto mb-3 opacity-50" />
            <p>No system events found.</p>
          </div>
        ) : (
          <div className="divide-y divide-gray-700">
            {events.map((event) => (
              <div key={event.id} className="p-4 hover:bg-gray-750 transition-colors">
                <div className="flex items-start justify-between">
                  <div className="flex-1">
                    <div className="flex items-center gap-3 mb-2">
                      <span className={`px-2 py-1 text-xs rounded ${typeColors[event.type]}`}>
                        {eventTypes[event.type]}
                      </span>
                      <span className="px-2 py-1 bg-gray-700 text-gray-300 text-xs rounded">
                        {eventCategories[event.category]}
                      </span>
                      <span className="text-sm text-gray-500">
                        {formatDate(event.timestamp)}
                      </span>
                      {event.user && (
                        <span className="text-sm text-gray-500">
                          by {event.user}
                        </span>
                      )}
                    </div>
                    <p className="text-white mb-1">{event.message}</p>
                    {event.details && (
                      <p className="text-sm text-gray-400">{event.details}</p>
                    )}
                    {event.relatedEntityType && event.relatedEntityId && (
                      <p className="text-xs text-gray-500 mt-1">
                        Related: {event.relatedEntityType} #{event.relatedEntityId}
                      </p>
                    )}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
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
      )}
    </div>
  );
};

export default SystemEventsPage;
