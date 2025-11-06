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
  NoSymbolIcon,
  Cog6ToothIcon,
  Bars3Icon,
  ChevronUpDownIcon
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
  postImportCategory?: string;
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
  protocol?: string; // 'usenet' or 'torrent'
  indexer?: string;
  size: number;
  downloaded: number;
  progress: number;
  timeRemaining?: string;
  errorMessage?: string;
  added: string;
  completedAt?: string;
  importedAt?: string;
}

interface ColumnVisibility {
  event: boolean;
  title: boolean;
  quality: boolean;
  protocol: boolean;
  indexer: boolean;
  status: boolean;
  progress: boolean;
  size: boolean;
  timeLeft: boolean;
  client: boolean;
  added: boolean;
  actions: boolean;
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

type RemovalMethod = 'removeFromClient' | 'changeCategory' | 'ignoreDownload';
type BlocklistAction = 'none' | 'blocklistAndSearch' | 'blocklistOnly';

interface RemoveQueueDialog {
  type: 'queue';
  id: number;
  title: string;
  status: number;
  downloadClient?: DownloadClient;
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
  const [removeQueueDialog, setRemoveQueueDialog] = useState<RemoveQueueDialog | null>(null);
  const [removalMethod, setRemovalMethod] = useState<RemovalMethod>('removeFromClient');
  const [blocklistAction, setBlocklistAction] = useState<BlocklistAction>('none');
  const [deleteConfirm, setDeleteConfirm] = useState<{ type: 'history' | 'blocklist'; id: number } | null>(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [refreshInterval, setRefreshInterval] = useState<NodeJS.Timeout | null>(null);
  const [showTableOptions, setShowTableOptions] = useState(false);
  const [pageSize, setPageSize] = useState(() => {
    const saved = localStorage.getItem('queuePageSize');
    return saved ? parseInt(saved) : 200;
  });
  const [showUnknownEvents, setShowUnknownEvents] = useState(() => {
    const saved = localStorage.getItem('queueShowUnknownEvents');
    return saved ? JSON.parse(saved) : true;
  });

  // Column order - load from localStorage or use default order
  const [columnOrder, setColumnOrder] = useState<(keyof ColumnVisibility)[]>(() => {
    const saved = localStorage.getItem('queueColumnOrder');
    return saved ? JSON.parse(saved) : [
      'event', 'title', 'quality', 'protocol', 'indexer',
      'status', 'progress', 'size', 'timeLeft', 'client', 'added', 'actions'
    ];
  });

  // Column visibility - load from localStorage or use defaults
  const [columnVisibility, setColumnVisibility] = useState<ColumnVisibility>(() => {
    const saved = localStorage.getItem('queueColumnVisibility');
    return saved ? JSON.parse(saved) : {
      event: true,
      title: true,
      quality: true,
      protocol: false,
      indexer: false,
      status: true,
      progress: true,
      size: true,
      timeLeft: false,
      client: true,
      added: true,
      actions: true
    };
  });

  // Drag and drop state for column reordering
  const [draggedColumn, setDraggedColumn] = useState<keyof ColumnVisibility | null>(null);

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

  const handleOpenRemoveQueueDialog = (item: QueueItem) => {
    setRemoveQueueDialog({
      type: 'queue',
      id: item.id,
      title: item.title,
      status: item.status,
      downloadClient: item.downloadClient
    });
    setRemovalMethod('removeFromClient'); // Reset to default
    setBlocklistAction('none'); // Reset to default
  };

  const handleRemoveQueue = async () => {
    if (!removeQueueDialog) return;

    try {
      await apiClient.delete(`/queue/${removeQueueDialog.id}`, {
        params: {
          removalMethod,
          blocklistAction
        }
      });
      setRemoveQueueDialog(null);
      loadQueue();
    } catch (error) {
      console.error('Failed to remove queue item:', error);
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

  const toggleColumn = (column: keyof ColumnVisibility) => {
    const newVisibility = {
      ...columnVisibility,
      [column]: !columnVisibility[column]
    };
    setColumnVisibility(newVisibility);
    localStorage.setItem('queueColumnVisibility', JSON.stringify(newVisibility));
  };

  const updatePageSize = (size: number) => {
    setPageSize(size);
    localStorage.setItem('queuePageSize', size.toString());
  };

  const toggleShowUnknownEvents = () => {
    const newValue = !showUnknownEvents;
    setShowUnknownEvents(newValue);
    localStorage.setItem('queueShowUnknownEvents', JSON.stringify(newValue));
  };

  // Drag and drop handlers for column reordering
  const handleDragStart = (column: keyof ColumnVisibility) => {
    setDraggedColumn(column);
  };

  const handleDragOver = (e: React.DragEvent, column: keyof ColumnVisibility) => {
    e.preventDefault();
    if (!draggedColumn || draggedColumn === column) return;

    const newOrder = [...columnOrder];
    const draggedIndex = newOrder.indexOf(draggedColumn);
    const targetIndex = newOrder.indexOf(column);

    // Remove dragged item and insert at new position
    newOrder.splice(draggedIndex, 1);
    newOrder.splice(targetIndex, 0, draggedColumn);

    setColumnOrder(newOrder);
    localStorage.setItem('queueColumnOrder', JSON.stringify(newOrder));
  };

  const handleDragEnd = () => {
    setDraggedColumn(null);
  };

  // Filter queue items based on showUnknownEvents setting
  const filteredQueueItems = showUnknownEvents
    ? queueItems
    : queueItems.filter(item => item.event && item.event.id);

  // Column label mapping
  const getColumnLabel = (column: keyof ColumnVisibility): string => {
    const labels: Record<keyof ColumnVisibility, string> = {
      event: 'Event',
      title: 'Episode Title',
      quality: 'Quality',
      protocol: 'Protocol',
      indexer: 'Indexer',
      status: 'Status',
      progress: 'Progress',
      size: 'Size',
      timeLeft: 'Time Left',
      client: 'Download Client',
      added: 'Added',
      actions: 'Actions'
    };
    return labels[column];
  };

  // Render cell content based on column type
  const renderCell = (column: keyof ColumnVisibility, item: QueueItem) => {
    switch (column) {
      case 'event':
        return (
          <td key="event" className="px-6 py-4">
            <div className="text-white font-medium">{item.event?.title || 'Unknown Event'}</div>
            <div className="text-sm text-gray-400">{item.event?.organization}</div>
          </td>
        );
      case 'title':
        return (
          <td key="title" className="px-6 py-4">
            <div className="text-gray-300 text-sm max-w-md truncate">{item.title}</div>
          </td>
        );
      case 'quality':
        return (
          <td key="quality" className="px-6 py-4 text-center">
            <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">
              {item.quality || 'Unknown'}
            </span>
          </td>
        );
      case 'protocol':
        return (
          <td key="protocol" className="px-6 py-4 text-center">
            <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded uppercase">
              {item.protocol || 'Unknown'}
            </span>
          </td>
        );
      case 'indexer':
        return (
          <td key="indexer" className="px-6 py-4 text-center">
            <span className="text-gray-400 text-sm">{item.indexer || 'Unknown'}</span>
          </td>
        );
      case 'status':
        return (
          <td key="status" className="px-6 py-4">
            <div className={`flex items-center justify-center gap-2 ${statusColors[item.status]}`}>
              {getStatusIcon(item.status)}
              <span className="text-sm">{statusNames[item.status]}</span>
            </div>
            {item.errorMessage && (
              <div className="text-xs text-red-400 mt-1 text-center">{item.errorMessage}</div>
            )}
          </td>
        );
      case 'progress':
        return (
          <td key="progress" className="px-6 py-4">
            <div className="w-full">
              <div className="flex items-center justify-between text-xs text-gray-400 mb-1">
                <span>{item.progress.toFixed(1)}%</span>
              </div>
              <div className="w-full bg-gray-700 rounded-full h-2">
                <div
                  className="bg-red-600 h-2 rounded-full transition-all"
                  style={{ width: `${item.progress}%` }}
                ></div>
              </div>
            </div>
          </td>
        );
      case 'size':
        return (
          <td key="size" className="px-6 py-4 text-center">
            <div className="text-gray-300 text-sm">
              {formatBytes(item.downloaded)} / {formatBytes(item.size)}
            </div>
          </td>
        );
      case 'timeLeft':
        return (
          <td key="timeLeft" className="px-6 py-4 text-center">
            <span className="text-gray-400 text-sm">{item.timeRemaining || '-'}</span>
          </td>
        );
      case 'client':
        return (
          <td key="client" className="px-6 py-4 text-center">
            <span className="text-gray-400 text-sm">{item.downloadClient?.name || 'Unknown'}</span>
          </td>
        );
      case 'added':
        return (
          <td key="added" className="px-6 py-4 text-center">
            <span className="text-gray-400 text-sm">{formatDate(item.added)}</span>
          </td>
        );
      case 'actions':
        return (
          <td key="actions" className="px-6 py-4">
            <div className="flex items-center justify-end gap-2">
              <button
                onClick={() => handleOpenRemoveQueueDialog(item)}
                className="p-2 text-red-400 hover:text-red-300 hover:bg-red-900/30 rounded transition-colors"
                title="Remove"
              >
                <TrashIcon className="w-5 h-5" />
              </button>
            </div>
          </td>
        );
      default:
        return null;
    }
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

  const isCompleted = removeQueueDialog?.status === 3 || removeQueueDialog?.status === 7;
  const hasPostImportCategory = removeQueueDialog?.downloadClient?.postImportCategory != null &&
                                  removeQueueDialog?.downloadClient?.postImportCategory !== '';
  const showChangeCategory = isCompleted && !hasPostImportCategory;

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-black to-red-950/20 p-6">
      <div className="max-w-7xl mx-auto">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-4xl font-bold text-white mb-2">Activity</h1>
            <p className="text-gray-400">Monitor downloads and import history</p>
          </div>
          <div className="flex gap-2">
            {activeTab === 'queue' && (
              <button
                onClick={() => setShowTableOptions(true)}
                className="flex items-center px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                title="Table Options"
              >
                <Cog6ToothIcon className="w-5 h-5" />
              </button>
            )}
            <button
              onClick={handleRefresh}
              className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
            >
              <ArrowPathIcon className="w-5 h-5 mr-2" />
              Refresh
            </button>
          </div>
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
            {filteredQueueItems.length === 0 ? (
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
                      {columnOrder.map(column => {
                        if (!columnVisibility[column]) return null;
                        const align = column === 'event' || column === 'title' ? 'text-left' : column === 'actions' ? 'text-right' : 'text-center';
                        return (
                          <th key={column} className={`px-6 py-3 ${align} font-medium`}>
                            {getColumnLabel(column)}
                          </th>
                        );
                      })}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-700">
                    {filteredQueueItems.map((item) => (
                      <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                        {columnOrder.map(column => {
                          if (!columnVisibility[column]) return null;
                          return renderCell(column, item);
                        })}
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

        {/* Remove from Queue Dialog (Sonarr-style) */}
        {removeQueueDialog && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-2xl w-full p-6">
              <div className="flex items-start justify-between mb-6">
                <h3 className="text-xl font-bold text-white">
                  Remove - {removeQueueDialog.title.substring(0, 60)}...
                </h3>
                <button
                  onClick={() => setRemoveQueueDialog(null)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <p className="text-gray-300 mb-6">
                Are you sure you want to remove '{removeQueueDialog.title}' from the queue?
              </p>

              {/* Removal Method */}
              <div className="mb-6">
                <label className="block text-gray-300 font-medium mb-2">Removal Method</label>
                <select
                  value={removalMethod}
                  onChange={(e) => setRemovalMethod(e.target.value as RemovalMethod)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
                >
                  <option value="removeFromClient">Remove from Download Client</option>
                  {showChangeCategory && <option value="changeCategory">Change Category</option>}
                  <option value="ignoreDownload">Ignore Download</option>
                </select>
                <p className="text-sm text-yellow-500 mt-2">
                  {removalMethod === 'removeFromClient' && 'Removes download and file(s) from download client'}
                  {removalMethod === 'changeCategory' && 'Changes download to the \'Post-Import Category\' from Download Client'}
                  {removalMethod === 'ignoreDownload' && 'Stops Fightarr from processing this download further'}
                </p>
              </div>

              {/* Blocklist Release */}
              <div className="mb-6">
                <label className="block text-gray-300 font-medium mb-2">Blocklist Release</label>
                <select
                  value={blocklistAction}
                  onChange={(e) => setBlocklistAction(e.target.value as BlocklistAction)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
                >
                  <option value="none">Do not Blocklist</option>
                  <option value="blocklistAndSearch">Blocklist and Search</option>
                  <option value="blocklistOnly">Blocklist Only</option>
                </select>
                <p className="text-sm text-gray-400 mt-2">
                  {blocklistAction === 'none' && 'Remove without blocklisting'}
                  {blocklistAction === 'blocklistAndSearch' && 'Start a search for a replacement after blocklisting'}
                  {blocklistAction === 'blocklistOnly' && 'Blocklist without searching for a replacement'}
                </p>
              </div>

              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setRemoveQueueDialog(null)}
                  className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                >
                  Close
                </button>
                <button
                  onClick={handleRemoveQueue}
                  className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Remove
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Delete Confirmation Modal (for History and Blocklist) */}
        {deleteConfirm && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-md w-full p-6">
              <h3 className="text-xl font-bold text-white mb-4">
                {deleteConfirm.type === 'history' ? 'Delete History Item' : 'Remove from Blocklist'}
              </h3>
              <p className="text-gray-300 mb-6">
                {deleteConfirm.type === 'history'
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
                <button
                  onClick={() => deleteConfirm.type === 'history' ? handleDeleteHistory(deleteConfirm.id) : handleDeleteBlocklist(deleteConfirm.id)}
                  className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  {deleteConfirm.type === 'history' ? 'Delete' : 'Remove from Blocklist'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Table Options Modal - Sonarr Style */}
        {showTableOptions && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-lg w-full max-h-[90vh] overflow-y-auto">
              <div className="sticky top-0 bg-gradient-to-br from-gray-900 to-black border-b border-gray-700 px-6 py-4 flex items-center justify-between">
                <h3 className="text-xl font-bold text-white">Table Options</h3>
                <button
                  onClick={() => setShowTableOptions(false)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <div className="p-6 space-y-6">
                {/* Page Size */}
                <div className="border-b border-gray-700 pb-4">
                  <label className="block text-gray-300 font-medium mb-2">Page Size</label>
                  <input
                    type="number"
                    value={pageSize}
                    onChange={(e) => updatePageSize(parseInt(e.target.value) || 200)}
                    min="10"
                    max="1000"
                    step="10"
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
                  />
                  <p className="text-sm text-gray-400 mt-2">Number of items to show on each page</p>
                </div>

                {/* Show Unknown Events */}
                <div className="border-b border-gray-700 pb-4">
                  <label className="flex items-center gap-3 text-gray-300 hover:text-white cursor-pointer">
                    <input
                      type="checkbox"
                      checked={showUnknownEvents}
                      onChange={toggleShowUnknownEvents}
                      className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2"
                    />
                    <div>
                      <div className="font-medium">Show Unknown Events Items</div>
                      <div className="text-sm text-gray-400">
                        Show items without a event in the queue, this could include removed events or anything else in Fightarr's category
                      </div>
                    </div>
                  </label>
                </div>

                {/* Columns */}
                <div>
                  <label className="block text-gray-300 font-medium mb-3">Columns</label>
                  <p className="text-sm text-gray-400 mb-4">Choose which columns are visible and drag to reorder</p>

                  <div className="space-y-1 bg-gray-800/50 rounded-lg p-3">
                    {columnOrder.map(column => (
                      <div
                        key={column}
                        draggable
                        onDragStart={() => handleDragStart(column)}
                        onDragOver={(e) => handleDragOver(e, column)}
                        onDragEnd={handleDragEnd}
                        className={`flex items-center gap-3 px-3 py-2 rounded cursor-move transition-all ${
                          draggedColumn === column
                            ? 'bg-red-900/30 opacity-50'
                            : 'hover:bg-gray-700/50'
                        } group`}
                      >
                        <input
                          type="checkbox"
                          checked={columnVisibility[column]}
                          onChange={() => toggleColumn(column)}
                          onClick={(e) => e.stopPropagation()}
                          className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2"
                        />
                        <ChevronUpDownIcon className="w-5 h-5 text-gray-500 group-hover:text-gray-400" />
                        <span className="flex-1 text-gray-300 group-hover:text-white">{getColumnLabel(column)}</span>
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              <div className="sticky bottom-0 bg-gradient-to-br from-gray-900 to-black border-t border-gray-700 px-6 py-4 flex justify-end">
                <button
                  onClick={() => setShowTableOptions(false)}
                  className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
