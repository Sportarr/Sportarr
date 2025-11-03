import { useState, useEffect } from 'react';
import {
  ArrowPathIcon,
  TrashIcon,
  XMarkIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClockIcon,
  ExclamationTriangleIcon,
  ArrowDownTrayIcon,
  DocumentCheckIcon,
  NoSymbolIcon
} from '@heroicons/react/24/outline';
import apiClient from '../api/client';

type TabType = 'queue' | 'history' | 'blocklist';

interface Event {
  id: number;
  title: string;
  organization: string;
  eventDate: string;
}

interface DownloadClient {
  id: number;
  name: string;
}

interface QueueItem {
  id: number;
  eventId: number;
  event?: Event;
  title: string;
  downloadId: string;
  downloadClientId?: number;
  downloadClient?: DownloadClient;
  status: number; // 0=Queued, 1=Downloading, 2=Paused, 3=Completed, 4=Failed, 5=Warning, 6=Importing, 7=Imported
  quality?: string;
  size: number;
  downloaded: number;
  progress: number;
  timeRemaining?: string;
  errorMessage?: string;
  added: string;
  completedAt?: string;
  importedAt?: string;
}

interface HistoryItem {
  id: number;
  eventId: number;
  event?: Event;
  downloadQueueItemId?: number;
  downloadQueueItem?: QueueItem;
  sourcePath: string;
  destinationPath: string;
  quality: string;
  size: number;
  decision: number; // 0=Approved, 1=Rejected, 2=AlreadyImported, 3=Upgraded
  warnings: string[];
  errors: string[];
  importedAt: string;
}

interface BlocklistItem {
  id: number;
  eventId?: number;
  event?: Event;
  title: string;
  torrentInfoHash: string;
  indexer?: string;
  reason: number; // 0=FailedDownload, 1=MissingFiles, 2=CorruptedFiles, 3=QualityMismatch, 4=ManualBlock, 5=ImportFailed
  message?: string;
  blockedAt: string;
}

const statusNames = ['Queued', 'Downloading', 'Paused', 'Completed', 'Failed', 'Warning', 'Importing', 'Imported'];
const statusColors = [
  'text-gray-400',      // Queued
  'text-blue-400',      // Downloading
  'text-yellow-400',    // Paused
  'text-green-400',     // Completed
  'text-red-400',       // Failed
  'text-orange-400',    // Warning
  'text-purple-400',    // Importing
  'text-green-500'      // Imported
];

const decisionNames = ['Approved', 'Rejected', 'Already Imported', 'Upgraded'];
const decisionColors = ['text-green-400', 'text-red-400', 'text-yellow-400', 'text-blue-400'];

const blocklistReasonNames = ['Failed Download', 'Missing Files', 'Corrupted Files', 'Quality Mismatch', 'Manual Block', 'Import Failed'];
const blocklistReasonColors = ['text-red-400', 'text-orange-400', 'text-yellow-400', 'text-purple-400', 'text-blue-400', 'text-red-500'];

export default function ActivityPage() {
  const [activeTab, setActiveTab] = useState<TabType>('queue');
  const [queueItems, setQueueItems] = useState<QueueItem[]>([]);
  const [historyItems, setHistoryItems] = useState<HistoryItem[]>([]);
  const [blocklistItems, setBlocklistItems] = useState<BlocklistItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<{ type: 'queue' | 'history' | 'blocklist'; id: number } | null>(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [refreshInterval, setRefreshInterval] = useState<NodeJS.Timeout | null>(null);

  useEffect(() => {
    loadData();

    // Auto-refresh queue every 5 seconds when on queue tab
    if (activeTab === 'queue') {
      const interval = setInterval(loadQueue, 5000);
      setRefreshInterval(interval);
      return () => clearInterval(interval);
    } else {
      if (refreshInterval) {
        clearInterval(refreshInterval);
        setRefreshInterval(null);
      }
    }
  }, [activeTab, page]);

  const loadData = () => {
    if (activeTab === 'queue') {
      loadQueue();
    } else if (activeTab === 'history') {
      loadHistory();
    } else {
      loadBlocklist();
    }
  };

  const loadQueue = async () => {
    try {
      setIsLoading(true);
      const response = await apiClient.get('/queue');
      setQueueItems(response.data);
    } catch (error) {
      console.error('Failed to load queue:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const loadHistory = async () => {
    try {
      setIsLoading(true);
      const response = await apiClient.get(`/history?page=${page}&pageSize=50`);
      setHistoryItems(response.data.history);
      setTotalPages(response.data.totalPages);
    } catch (error) {
      console.error('Failed to load history:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const loadBlocklist = async () => {
    try {
      setIsLoading(true);
      const response = await apiClient.get(`/blocklist?page=${page}&pageSize=50`);
      setBlocklistItems(response.data.blocklist);
      setTotalPages(response.data.totalPages);
    } catch (error) {
      console.error('Failed to load blocklist:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleRefresh = () => {
    loadData();
  };

  const handleDeleteQueue = async (id: number, removeFromClient: boolean) => {
    try {
      await apiClient.delete(`/queue/${id}?removeFromClient=${removeFromClient}`);
      setDeleteConfirm(null);
      loadQueue();
    } catch (error) {
      console.error('Failed to delete queue item:', error);
    }
  };

  const handleDeleteHistory = async (id: number) => {
    try {
      await apiClient.delete(`/history/${id}`);
      setDeleteConfirm(null);
      loadHistory();
    } catch (error) {
      console.error('Failed to delete history item:', error);
    }
  };

  const handleDeleteBlocklist = async (id: number) => {
    try {
      await apiClient.delete(`/blocklist/${id}`);
      setDeleteConfirm(null);
      loadBlocklist();
    } catch (error) {
      console.error('Failed to delete blocklist item:', error);
    }
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    const now = new Date();
    const diff = now.getTime() - date.getTime();
    const minutes = Math.floor(diff / 60000);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (minutes < 1) return 'Just now';
    if (minutes < 60) return `${minutes}m ago`;
    if (hours < 24) return `${hours}h ago`;
    if (days < 7) return `${days}d ago`;
    return date.toLocaleDateString();
  };

  const getStatusIcon = (status: number) => {
    switch (status) {
      case 0: return <ClockIcon className="w-5 h-5" />;
      case 1: return <ArrowDownTrayIcon className="w-5 h-5 animate-bounce" />;
      case 2: return <XCircleIcon className="w-5 h-5" />;
      case 3: return <CheckCircleIcon className="w-5 h-5" />;
      case 4: return <XCircleIcon className="w-5 h-5" />;
      case 5: return <ExclamationTriangleIcon className="w-5 h-5" />;
      case 6: return <DocumentCheckIcon className="w-5 h-5 animate-pulse" />;
      case 7: return <CheckCircleIcon className="w-5 h-5" />;
      default: return <ClockIcon className="w-5 h-5" />;
    }
  };

  const getDecisionIcon = (decision: number) => {
    switch (decision) {
      case 0: return <CheckCircleIcon className="w-5 h-5" />;
      case 1: return <XCircleIcon className="w-5 h-5" />;
      case 2: return <ExclamationTriangleIcon className="w-5 h-5" />;
      case 3: return <ArrowPathIcon className="w-5 h-5" />;
      default: return <CheckCircleIcon className="w-5 h-5" />;
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-black to-red-950/20 p-6">
      <div className="max-w-7xl mx-auto">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-4xl font-bold text-white mb-2">Activity</h1>
            <p className="text-gray-400">Monitor downloads and import history</p>
          </div>
          <button
            onClick={handleRefresh}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <ArrowPathIcon className="w-5 h-5 mr-2" />
            Refresh
          </button>
        </div>

        {/* Tabs */}
        <div className="flex space-x-1 mb-6 bg-gray-900 p-1 rounded-lg inline-flex">
          <button
            onClick={() => { setActiveTab('queue'); setPage(1); }}
            className={`px-6 py-2 rounded-md transition-all ${
              activeTab === 'queue'
                ? 'bg-red-600 text-white'
                : 'text-gray-400 hover:text-white hover:bg-gray-800'
            }`}
          >
            Queue
            {queueItems.length > 0 && (
              <span className="ml-2 px-2 py-0.5 bg-red-700 text-white text-xs rounded-full">
                {queueItems.length}
              </span>
            )}
          </button>
          <button
            onClick={() => { setActiveTab('history'); setPage(1); }}
            className={`px-6 py-2 rounded-md transition-all ${
              activeTab === 'history'
                ? 'bg-red-600 text-white'
                : 'text-gray-400 hover:text-white hover:bg-gray-800'
            }`}
          >
            History
          </button>
          <button
            onClick={() => { setActiveTab('blocklist'); setPage(1); }}
            className={`px-6 py-2 rounded-md transition-all ${
              activeTab === 'blocklist'
                ? 'bg-red-600 text-white'
                : 'text-gray-400 hover:text-white hover:bg-gray-800'
            }`}
          >
            Blocklist
            {blocklistItems.length > 0 && (
              <span className="ml-2 px-2 py-0.5 bg-red-700 text-white text-xs rounded-full">
                {blocklistItems.length}
              </span>
            )}
          </button>
        </div>

        {/* Content */}
        {isLoading ? (
          <div className="text-center py-12">
            <div className="inline-block animate-spin rounded-full h-12 w-12 border-4 border-red-600 border-t-transparent"></div>
            <p className="mt-4 text-gray-400">Loading...</p>
          </div>
        ) : activeTab === 'queue' ? (
          // Queue Tab
          <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-700 rounded-lg overflow-hidden">
            {queueItems.length === 0 ? (
              <div className="p-12 text-center text-gray-400">
                <ArrowDownTrayIcon className="w-16 h-16 mx-auto mb-4 opacity-50" />
                <p className="text-lg">No active downloads</p>
                <p className="text-sm mt-2">Downloads will appear here when events are searched and sent to download clients</p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr className="bg-gray-800 text-gray-300 text-sm">
                      <th className="px-6 py-3 text-left font-medium">Event</th>
                      <th className="px-6 py-3 text-left font-medium">Title</th>
                      <th className="px-6 py-3 text-center font-medium">Quality</th>
                      <th className="px-6 py-3 text-center font-medium">Status</th>
                      <th className="px-6 py-3 text-center font-medium">Progress</th>
                      <th className="px-6 py-3 text-center font-medium">Size</th>
                      <th className="px-6 py-3 text-center font-medium">Client</th>
                      <th className="px-6 py-3 text-center font-medium">Added</th>
                      <th className="px-6 py-3 text-right font-medium">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-700">
                    {queueItems.map((item) => (
                      <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                        <td className="px-6 py-4">
                          <div className="text-white font-medium">{item.event?.title || 'Unknown Event'}</div>
                          <div className="text-sm text-gray-400">{item.event?.organization}</div>
                        </td>
                        <td className="px-6 py-4">
                          <div className="text-gray-300 text-sm max-w-md truncate">{item.title}</div>
                        </td>
                        <td className="px-6 py-4 text-center">
                          <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">
                            {item.quality || 'Unknown'}
                          </span>
                        </td>
                        <td className="px-6 py-4">
                          <div className={`flex items-center justify-center gap-2 ${statusColors[item.status]}`}>
                            {getStatusIcon(item.status)}
                            <span className="text-sm">{statusNames[item.status]}</span>
                          </div>
                          {item.errorMessage && (
                            <div className="text-xs text-red-400 mt-1 text-center">{item.errorMessage}</div>
                          )}
                        </td>
                        <td className="px-6 py-4">
                          <div className="w-full">
                            <div className="flex items-center justify-between text-xs text-gray-400 mb-1">
                              <span>{item.progress.toFixed(1)}%</span>
                              {item.timeRemaining && <span>{item.timeRemaining}</span>}
                            </div>
                            <div className="w-full bg-gray-700 rounded-full h-2">
                              <div
                                className="bg-red-600 h-2 rounded-full transition-all"
                                style={{ width: `${item.progress}%` }}
                              ></div>
                            </div>
                          </div>
                        </td>
                        <td className="px-6 py-4 text-center">
                          <div className="text-gray-300 text-sm">
                            {formatBytes(item.downloaded)} / {formatBytes(item.size)}
                          </div>
                        </td>
                        <td className="px-6 py-4 text-center">
                          <span className="text-gray-400 text-sm">
                            {item.downloadClient?.name || 'Unknown'}
                          </span>
                        </td>
                        <td className="px-6 py-4 text-center">
                          <span className="text-gray-400 text-sm">{formatDate(item.added)}</span>
                        </td>
                        <td className="px-6 py-4">
                          <div className="flex items-center justify-end gap-2">
                            <button
                              onClick={() => setDeleteConfirm({ type: 'queue', id: item.id })}
                              className="p-2 text-red-400 hover:text-red-300 hover:bg-red-900/30 rounded transition-colors"
                              title="Remove"
                            >
                              <TrashIcon className="w-5 h-5" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        ) : activeTab === 'history' ? (
          // History Tab
          <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-700 rounded-lg overflow-hidden">
            {historyItems.length === 0 ? (
              <div className="p-12 text-center text-gray-400">
                <DocumentCheckIcon className="w-16 h-16 mx-auto mb-4 opacity-50" />
                <p className="text-lg">No import history</p>
                <p className="text-sm mt-2">Imported events will appear here once downloads complete</p>
              </div>
            ) : (
              <>
                <div className="overflow-x-auto">
                  <table className="w-full">
                    <thead>
                      <tr className="bg-gray-800 text-gray-300 text-sm">
                        <th className="px-6 py-3 text-left font-medium">Event</th>
                        <th className="px-6 py-3 text-left font-medium">Source Path</th>
                        <th className="px-6 py-3 text-center font-medium">Quality</th>
                        <th className="px-6 py-3 text-center font-medium">Decision</th>
                        <th className="px-6 py-3 text-center font-medium">Size</th>
                        <th className="px-6 py-3 text-center font-medium">Imported</th>
                        <th className="px-6 py-3 text-right font-medium">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-700">
                      {historyItems.map((item) => (
                        <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                          <td className="px-6 py-4">
                            <div className="text-white font-medium">{item.event?.title || 'Unknown Event'}</div>
                            <div className="text-sm text-gray-400">{item.event?.organization}</div>
                          </td>
                          <td className="px-6 py-4">
                            <div className="text-gray-300 text-sm max-w-md truncate" title={item.sourcePath}>
                              {item.sourcePath}
                            </div>
                            {item.warnings.length > 0 && (
                              <div className="text-xs text-yellow-400 mt-1">
                                {item.warnings.length} warning(s)
                              </div>
                            )}
                            {item.errors.length > 0 && (
                              <div className="text-xs text-red-400 mt-1">
                                {item.errors.length} error(s)
                              </div>
                            )}
                          </td>
                          <td className="px-6 py-4 text-center">
                            <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">
                              {item.quality}
                            </span>
                          </td>
                          <td className="px-6 py-4">
                            <div className={`flex items-center justify-center gap-2 ${decisionColors[item.decision]}`}>
                              {getDecisionIcon(item.decision)}
                              <span className="text-sm">{decisionNames[item.decision]}</span>
                            </div>
                          </td>
                          <td className="px-6 py-4 text-center">
                            <span className="text-gray-300 text-sm">{formatBytes(item.size)}</span>
                          </td>
                          <td className="px-6 py-4 text-center">
                            <span className="text-gray-400 text-sm">{formatDate(item.importedAt)}</span>
                          </td>
                          <td className="px-6 py-4">
                            <div className="flex items-center justify-end gap-2">
                              <button
                                onClick={() => setDeleteConfirm({ type: 'history', id: item.id })}
                                className="p-2 text-red-400 hover:text-red-300 hover:bg-red-900/30 rounded transition-colors"
                                title="Delete"
                              >
                                <TrashIcon className="w-5 h-5" />
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                {/* Pagination */}
                {totalPages > 1 && (
                  <div className="px-6 py-4 border-t border-gray-700 flex items-center justify-between">
                    <button
                      onClick={() => setPage(Math.max(1, page - 1))}
                      disabled={page === 1}
                      className="px-4 py-2 bg-gray-800 hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded transition-colors"
                    >
                      Previous
                    </button>
                    <span className="text-gray-400">
                      Page {page} of {totalPages}
                    </span>
                    <button
                      onClick={() => setPage(Math.min(totalPages, page + 1))}
                      disabled={page === totalPages}
                      className="px-4 py-2 bg-gray-800 hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded transition-colors"
                    >
                      Next
                    </button>
                  </div>
                )}
              </>
            )}
          </div>
        ) : (
          // Blocklist Tab
          <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-700 rounded-lg overflow-hidden">
            {blocklistItems.length === 0 ? (
              <div className="p-12 text-center text-gray-400">
                <NoSymbolIcon className="w-16 h-16 mx-auto mb-4 opacity-50" />
                <p className="text-lg">No blocked releases</p>
                <p className="text-sm mt-2">Failed or rejected releases will appear here</p>
              </div>
            ) : (
              <>
                <div className="overflow-x-auto">
                  <table className="w-full">
                    <thead>
                      <tr className="bg-gray-800 text-gray-300 text-sm">
                        <th className="px-6 py-3 text-left font-medium">Event</th>
                        <th className="px-6 py-3 text-left font-medium">Title</th>
                        <th className="px-6 py-3 text-center font-medium">Indexer</th>
                        <th className="px-6 py-3 text-center font-medium">Reason</th>
                        <th className="px-6 py-3 text-center font-medium">Blocked</th>
                        <th className="px-6 py-3 text-right font-medium">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-700">
                      {blocklistItems.map((item) => (
                        <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                          <td className="px-6 py-4">
                            <div className="text-white font-medium">{item.event?.title || 'Unknown Event'}</div>
                            <div className="text-sm text-gray-400">{item.event?.organization}</div>
                          </td>
                          <td className="px-6 py-4">
                            <div className="text-gray-300 text-sm max-w-md truncate">{item.title}</div>
                            {item.message && (
                              <div className="text-xs text-gray-400 mt-1">{item.message}</div>
                            )}
                            <div className="text-xs text-gray-500 mt-1 font-mono">
                              Hash: {item.torrentInfoHash.substring(0, 20)}...
                            </div>
                          </td>
                          <td className="px-6 py-4 text-center">
                            <span className="text-gray-400 text-sm">{item.indexer || 'Unknown'}</span>
                          </td>
                          <td className="px-6 py-4">
                            <div className={`flex items-center justify-center gap-2 ${blocklistReasonColors[item.reason]}`}>
                              <NoSymbolIcon className="w-5 h-5" />
                              <span className="text-sm">{blocklistReasonNames[item.reason]}</span>
                            </div>
                          </td>
                          <td className="px-6 py-4 text-center">
                            <span className="text-gray-400 text-sm">{formatDate(item.blockedAt)}</span>
                          </td>
                          <td className="px-6 py-4">
                            <div className="flex items-center justify-end gap-2">
                              <button
                                onClick={() => setDeleteConfirm({ type: 'blocklist', id: item.id })}
                                className="p-2 text-red-400 hover:text-red-300 hover:bg-red-900/30 rounded transition-colors"
                                title="Delete"
                              >
                                <TrashIcon className="w-5 h-5" />
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                {/* Pagination */}
                {totalPages > 1 && (
                  <div className="px-6 py-4 border-t border-gray-700 flex items-center justify-between">
                    <button
                      onClick={() => setPage(Math.max(1, page - 1))}
                      disabled={page === 1}
                      className="px-4 py-2 bg-gray-800 hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded transition-colors"
                    >
                      Previous
                    </button>
                    <span className="text-gray-400">
                      Page {page} of {totalPages}
                    </span>
                    <button
                      onClick={() => setPage(Math.min(totalPages, page + 1))}
                      disabled={page === totalPages}
                      className="px-4 py-2 bg-gray-800 hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded transition-colors"
                    >
                      Next
                    </button>
                  </div>
                )}
              </>
            )}
          </div>
        )}

        {/* Delete Confirmation Modal */}
        {deleteConfirm && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-md w-full p-6">
              <h3 className="text-xl font-bold text-white mb-4">
                {deleteConfirm.type === 'queue' ? 'Remove from Queue' : deleteConfirm.type === 'history' ? 'Delete History Item' : 'Remove from Blocklist'}
              </h3>
              <p className="text-gray-300 mb-6">
                {deleteConfirm.type === 'queue'
                  ? 'Do you also want to remove this download from the download client?'
                  : deleteConfirm.type === 'history'
                  ? 'Are you sure you want to delete this history item? This action cannot be undone.'
                  : 'Remove this release from the blocklist? It will be allowed in future searches.'}
              </p>
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setDeleteConfirm(null)}
                  className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                {deleteConfirm.type === 'queue' ? (
                  <>
                    <button
                      onClick={() => handleDeleteQueue(deleteConfirm.id, false)}
                      className="px-4 py-2 bg-yellow-600 hover:bg-yellow-700 text-white rounded-lg transition-colors"
                    >
                      Remove from Queue Only
                    </button>
                    <button
                      onClick={() => handleDeleteQueue(deleteConfirm.id, true)}
                      className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                    >
                      Remove from Both
                    </button>
                  </>
                ) : deleteConfirm.type === 'history' ? (
                  <button
                    onClick={() => handleDeleteHistory(deleteConfirm.id)}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Delete
                  </button>
                ) : (
                  <button
                    onClick={() => handleDeleteBlocklist(deleteConfirm.id)}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Remove from Blocklist
                  </button>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
