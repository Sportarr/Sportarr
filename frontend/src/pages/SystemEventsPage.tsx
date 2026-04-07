import React, { useState, useEffect } from 'react';
import { toast } from 'sonner';
import { TrashIcon, InformationCircleIcon } from '@heroicons/react/24/outline';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';
import { apiGet, apiPost } from '../utils/api';
import { parseAsUtc } from '../utils/timezone';
import { useCompactView } from '../hooks/useCompactView';
import { TABLE_ROW_HOVER, TABLE_CELL_LABEL, TABLE_CELL_DATA } from '../utils/designTokens';

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
  const compactView = useCompactView();

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

      const response = await apiGet(url);
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
      const response = await apiPost('/api/system/event/cleanup?days=30', {});
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
    const date = parseAsUtc(dateString);
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
    <PageShell>
      <PageHeader
        title="System Events"
        subtitle="Audit trail of system operations and user actions"
        actions={
          <button
            onClick={handleCleanup}
            className="flex items-center gap-2 rounded bg-red-900/30 px-4 py-2 text-red-400 hover:bg-red-900/50"
          >
            <TrashIcon className="w-4 h-4" />
            Cleanup Old Events
          </button>
        }
      />

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

      </div>

      {/* Error Display */}
      {error && (
        <div className="mb-6 p-4 bg-red-900/20 border border-red-800 rounded-lg">
          <p className="text-red-400">Error: {error}</p>
        </div>
      )}

      {/* Events List */}
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
      ) : compactView ? (
        <div className="bg-gray-800 rounded-lg border border-gray-700 overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="text-xs text-gray-400 uppercase text-left border-b border-gray-700">
                <th className="px-3 py-1.5">Time</th>
                <th className="px-2 py-1.5">Type</th>
                <th className="px-2 py-1.5">Category</th>
                <th className="px-3 py-1.5">Message</th>
                <th className="px-2 py-1.5">User</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-700">
              {events.map((event) => (
                <tr key={event.id} className={TABLE_ROW_HOVER}>
                  <td className={`${TABLE_CELL_LABEL} text-gray-400 whitespace-nowrap`}>
                    {formatDate(event.timestamp)}
                  </td>
                  <td className={TABLE_CELL_DATA}>
                    <span className={`px-2 py-0.5 text-xs rounded ${typeColors[event.type]}`}>
                      {eventTypes[event.type]}
                    </span>
                  </td>
                  <td className={TABLE_CELL_DATA}>
                    <span className="px-2 py-0.5 bg-gray-700 text-gray-300 text-xs rounded">
                      {eventCategories[event.category]}
                    </span>
                  </td>
                  <td className={`${TABLE_CELL_LABEL} text-white`}>
                    <div>{event.message}</div>
                    {event.details && (
                      <div className="text-xs text-gray-400 mt-0.5">{event.details}</div>
                    )}
                    {event.relatedEntityType && event.relatedEntityId && (
                      <div className="text-xs text-gray-500 mt-0.5">
                        {event.relatedEntityType} #{event.relatedEntityId}
                      </div>
                    )}
                  </td>
                  <td className={`${TABLE_CELL_DATA} text-gray-400 text-xs`}>
                    {event.user ?? '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {events.map((event) => (
            <div
              key={event.id}
              className="bg-gray-800 border border-gray-700 rounded-lg p-4 hover:bg-gray-750 transition-colors"
            >
              <div className="flex items-start gap-3">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-3 mb-2 flex-wrap">
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
                      <span className="text-sm text-gray-500">by {event.user}</span>
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
    </PageShell>
  );
};

export default SystemEventsPage;
