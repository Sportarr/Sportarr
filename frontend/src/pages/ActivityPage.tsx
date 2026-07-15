import React, { useState, useEffect } from 'react';
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
  ChevronUpDownIcon,
  ChevronUpIcon,
  ChevronDownIcon,
  ExclamationCircleIcon,
  EyeIcon
} from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import ManualImportModal from '../components/ManualImportModal';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';
import SegmentedTabs from '../components/SegmentedTabs';
import WantedPage from './WantedPage';
import { useCompactView } from '../hooks/useCompactView';
import { BADGE_BLUE, BADGE_PURPLE, BUTTON_DESTRUCTIVE, BUTTON_ICON_DESTRUCTIVE, BUTTON_ICON_INFO, BUTTON_ICON_SECONDARY, BUTTON_ICON_SUCCESS, BUTTON_ICON_WARNING, BUTTON_INFO, BUTTON_SECONDARY, BUTTON_SUCCESS, BUTTON_WARNING } from '../utils/designTokens';
import { formatRelativeDate } from '../utils/timezone';

type TabType = 'queue' | 'history' | 'blocklist' | 'grabHistory' | 'missing' | 'cutoffUnmet';

interface Event {
  id: number;
  title: string;
  organization: string;
  eventDate: string;
  broadcastDate?: string | null;
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
  statusMessages?: string[]; // Status messages (warnings, errors)
  added: string;
  completedAt?: string;
  importedAt?: string;
  part?: string; // For multi-part events (e.g., "Early Prelims", "Prelims", "Main Card")
  qualityScore?: number;
  customFormatScore?: number;
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
  eventId?: number;  // Nullable - event may have been deleted
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
  part?: string; // For multi-part events (e.g., "Early Prelims", "Prelims", "Main Card")
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
  part?: string; // For multi-part events (e.g., "Early Prelims", "Prelims", "Main Card")
}

interface GrabHistoryItem {
  kind: 'grab' | 'import';
  id: number;
  eventId: number | null;
  eventTitle?: string;
  leagueName?: string;
  title: string;
  indexer: string;
  indexerId?: number;
  protocol: string;
  size: number;
  quality?: string;
  codec?: string;
  source?: string;
  qualityScore: number;
  customFormatScore: number;
  partName?: string;
  grabbedAt: string;
  wasImported: boolean;
  importedAt?: string;
  fileExists: boolean;
  lastRegrabAttempt?: string;
  regrabCount: number;
  hasDownloadUrl: boolean;
  hasTorrentHash: boolean;
}

interface PendingImport {
  id: number;
  downloadClientId: number;
  downloadId: string;
  downloadClient?: DownloadClient;
  title: string;
  filePath: string;
  size: number;
  quality?: string;
  qualityScore: number;
  suggestedEventId?: number;
  suggestedEvent?: Event;
  suggestedPart?: string;
  suggestionConfidence: number;
  detected: string;
  protocol?: string;
  isPack?: boolean;
  fileCount?: number;
  matchedEventsCount?: number;
}

interface PackMatchPreview {
  fileName: string;
  eventId: number;
  eventTitle: string;
  matchConfidence: number;
}

type RemovalMethod = 'removeFromClient' | 'changeCategory' | 'ignoreDownload';
type BlocklistAction = 'none' | 'blocklistAndSearch' | 'blocklistOnly';

interface RemoveQueueDialogItem {
  id: number;
  title: string;
  status: number;
  downloadClient?: DownloadClient;
}

interface RemoveQueueDialog {
  type: 'queue';
  items: RemoveQueueDialogItem[];
}

interface RemoveHistoryDialog {
  type: 'history';
  id: number;
  eventId?: number;
  title: string;
}

interface RemoveBlocklistDialog {
  type: 'blocklist';
  id: number;
  eventId?: number;
  title: string;
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

// Custom format score badge shown next to the quality badge on queue,
// history, and grab history rows. Hidden when the score is 0 so installs
// without custom formats don't get a noise badge on every row.
const cfScoreBadge = (score?: number) => {
  if (!score) return null;
  return (
    <span
      className={`px-1.5 py-0.5 text-xs rounded font-medium whitespace-nowrap ${
        score > 0 ? 'bg-green-900/40 text-green-400' : 'bg-red-900/40 text-red-400'
      }`}
      title="Custom format score"
    >
      CF {score > 0 ? '+' : ''}{score}
    </span>
  );
};

export default function ActivityPage() {
  const [activeTab, setActiveTab] = useState<TabType>('queue');
  const [queueItems, setQueueItems] = useState<QueueItem[]>([]);
  const [pendingImports, setPendingImports] = useState<PendingImport[]>([]);
  const [historyItems, setHistoryItems] = useState<HistoryItem[]>([]);
  const [blocklistItems, setBlocklistItems] = useState<BlocklistItem[]>([]);
  const [grabHistoryItems, setGrabHistoryItems] = useState<GrabHistoryItem[]>([]);
  const [grabHistoryMissingOnly, setGrabHistoryMissingOnly] = useState(false);
  const [regrabbing, setRegrabbing] = useState<number | null>(null);
  const [bulkRegrabbing, setBulkRegrabbing] = useState(false);
  const [selectedPendingImport, setSelectedPendingImport] = useState<PendingImport | null>(null);
  const [packPreviewImport, setPackPreviewImport] = useState<PendingImport | null>(null);
  const [packMatches, setPackMatches] = useState<PackMatchPreview[]>([]);
  const [loadingPackPreview, setLoadingPackPreview] = useState(false);
  const [importingPack, setImportingPack] = useState<number | null>(null);
  const [isLoading, setIsLoading] = useState(true); // Only true for initial load
  const [isInitialLoad, setIsInitialLoad] = useState(true);
  const [removeQueueDialog, setRemoveQueueDialog] = useState<RemoveQueueDialog | null>(null);
  const [removeHistoryDialog, setRemoveHistoryDialog] = useState<RemoveHistoryDialog | null>(null);
  const [removeBlocklistDialog, setRemoveBlocklistDialog] = useState<RemoveBlocklistDialog | null>(null);
  const [selectedBlocklistIds, setSelectedBlocklistIds] = useState<Set<number>>(new Set());
  const [bulkRemoveBlocklistOpen, setBulkRemoveBlocklistOpen] = useState(false);
  const [removalMethod, setRemovalMethod] = useState<RemovalMethod>('removeFromClient');
  const [blocklistAction, setBlocklistAction] = useState<BlocklistAction>('none');
  const [historyBlocklistAction, setHistoryBlocklistAction] = useState<BlocklistAction>('none');
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [refreshInterval, setRefreshInterval] = useState<ReturnType<typeof setInterval> | null>(null);
  const [showTableOptions, setShowTableOptions] = useState(false);
  const [pageSize, setPageSize] = useState(() => {
    const saved = localStorage.getItem('queuePageSize');
    return saved ? parseInt(saved) : 200;
  });
  const [showUnknownEvents, setShowUnknownEvents] = useState(() => {
    const saved = localStorage.getItem('queueShowUnknownEvents');
    return saved ? JSON.parse(saved) : true;
  });

  // Queue sort. The column-header click in compact mode and the spacious-mode
  // sort toolbar both write here. Persisted to localStorage so the user's
  // choice survives reloads. Default is Added DESC (newest first), matching
  // the API's default ORDER BY.
  type QueueSortField = 'event' | 'title' | 'quality' | 'status' | 'progress' | 'size' | 'client' | 'added';
  const [queueSortField, setQueueSortField] = useState<QueueSortField>(() => {
    const saved = localStorage.getItem('queueSortField');
    return (saved as QueueSortField) || 'added';
  });
  const [queueSortDirection, setQueueSortDirection] = useState<'asc' | 'desc'>(() => {
    const saved = localStorage.getItem('queueSortDirection');
    return saved === 'asc' ? 'asc' : 'desc';
  });
  useEffect(() => { localStorage.setItem('queueSortField', queueSortField); }, [queueSortField]);
  useEffect(() => { localStorage.setItem('queueSortDirection', queueSortDirection); }, [queueSortDirection]);

  // Click handler shared by compact column headers and the spacious sort
  // toolbar buttons. Clicking the active field flips direction; clicking a
  // new field resets to ascending.
  const handleSortFieldChange = (field: QueueSortField) => {
    if (field === queueSortField) {
      setQueueSortDirection(d => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setQueueSortField(field);
      setQueueSortDirection('asc');
    }
  };

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
    const defaultVisibility: ColumnVisibility = {
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
      actions: true,
    };
    const saved = localStorage.getItem('queueColumnVisibility');
    if (!saved) return defaultVisibility;

    try {
      const parsed = JSON.parse(saved) as Partial<ColumnVisibility>;
      return {
        ...defaultVisibility,
        ...parsed,
        actions: true,
      };
    } catch {
      return defaultVisibility;
    }
  });

  // Drag and drop state for column reordering
  const [draggedColumn, setDraggedColumn] = useState<keyof ColumnVisibility | null>(null);
  const [isUserScrolling, setIsUserScrolling] = useState(false);
  const scrollTimeoutRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // Multi-select state for queue items and pending imports. Tracked as two
  // separate sets keyed by numeric id, plus a shared last-selected anchor key
  // (kind + id) used to drive shift-click range selection across both kinds.
  const [selectedQueueIds, setSelectedQueueIds] = useState<Set<number>>(new Set());
  const [selectedPendingIds, setSelectedPendingIds] = useState<Set<number>>(new Set());
  const [lastSelectedKey, setLastSelectedKey] = useState<string | null>(null);

  const compactView = useCompactView();

  // Track user scrolling to pause auto-refresh
  useEffect(() => {
    const handleScroll = () => {
      setIsUserScrolling(true);

      // Clear existing timeout
      if (scrollTimeoutRef.current) {
        clearTimeout(scrollTimeoutRef.current);
      }

      // Resume auto-refresh 3 seconds after user stops scrolling
      scrollTimeoutRef.current = setTimeout(() => {
        setIsUserScrolling(false);
      }, 3000);
    };

    window.addEventListener('scroll', handleScroll);
    return () => {
      window.removeEventListener('scroll', handleScroll);
      if (scrollTimeoutRef.current) {
        clearTimeout(scrollTimeoutRef.current);
      }
    };
  }, []);

  // Track previous tab to detect tab changes
  const prevTabRef = React.useRef<TabType>(activeTab);
  const prevPageRef = React.useRef<number>(page);

  useEffect(() => {
    const tabChanged = prevTabRef.current !== activeTab;
    const pageChanged = prevPageRef.current !== page;

    // Show loading spinner on initial load, tab change, or page change
    const shouldShowLoading = isInitialLoad || tabChanged || pageChanged;
    loadData(shouldShowLoading);

    // Update refs
    prevTabRef.current = activeTab;
    prevPageRef.current = page;

    // Auto-refresh queue every 5 seconds when on queue tab (but not while user is scrolling)
    if (activeTab === 'queue') {
      const interval = setInterval(() => {
        if (!isUserScrolling) {
          loadQueue(false); // Silent refresh - no loading spinner
        }
      }, 5000);
      setRefreshInterval(interval);
      return () => clearInterval(interval);
    } else {
      if (refreshInterval) {
        clearInterval(refreshInterval);
        setRefreshInterval(null);
      }
    }
  }, [activeTab, page]);

  const loadData = (showLoading = false) => {
    if (activeTab === 'queue') {
      loadQueue(showLoading);
    } else if (activeTab === 'history') {
      loadHistory(showLoading);
    } else if (activeTab === 'grabHistory') {
      loadGrabHistory(showLoading);
    } else if (activeTab === 'missing' || activeTab === 'cutoffUnmet') {
      // The embedded Wanted views load their own data.
      setIsInitialLoad(false);
    } else {
      loadBlocklist(showLoading);
    }
  };

  const loadQueue = async (showLoading = false) => {
    try {
      if (showLoading) setIsLoading(true);
      const [queueResponse, pendingResponse] = await Promise.all([
        apiClient.get('/queue'),
        apiClient.get('/pending-imports')
      ]);
      setQueueItems(queueResponse.data);
      setPendingImports(pendingResponse.data);
    } catch (error) {
      console.error('Failed to load queue:', error);
    } finally {
      if (showLoading) {
        setIsLoading(false);
        setIsInitialLoad(false);
      }
    }
  };

  const loadHistory = async (showLoading = false) => {
    try {
      if (showLoading) setIsLoading(true);
      const response = await apiClient.get(`/history?page=${page}&pageSize=50`);
      setHistoryItems(response.data.history);
      setTotalPages(response.data.totalPages);
    } catch (error) {
      console.error('Failed to load history:', error);
    } finally {
      if (showLoading) {
        setIsLoading(false);
        setIsInitialLoad(false);
      }
    }
  };

  const loadBlocklist = async (showLoading = false) => {
    try {
      if (showLoading) setIsLoading(true);
      const response = await apiClient.get(`/blocklist?page=${page}&pageSize=50`);
      setBlocklistItems(response.data.blocklist);
      setTotalPages(response.data.totalPages);
    } catch (error) {
      console.error('Failed to load blocklist:', error);
    } finally {
      if (showLoading) {
        setIsLoading(false);
        setIsInitialLoad(false);
      }
    }
  };

  const loadGrabHistory = async (showLoading = false) => {
    try {
      if (showLoading) setIsLoading(true);
      const response = await apiClient.get(`/grab-history?page=${page}&pageSize=50&missingOnly=${grabHistoryMissingOnly}`);
      setGrabHistoryItems(response.data.history);
      setTotalPages(response.data.totalPages);
    } catch (error) {
      console.error('Failed to load grab history:', error);
    } finally {
      if (showLoading) {
        setIsLoading(false);
        setIsInitialLoad(false);
      }
    }
  };

  // Reload grab history when filter changes
  React.useEffect(() => {
    if (activeTab === 'grabHistory') {
      loadGrabHistory(true);
    }
  }, [grabHistoryMissingOnly]);

  const handleRegrab = async (id: number) => {
    try {
      setRegrabbing(id);
      await apiClient.post(`/grab-history/${id}/regrab`);
      loadGrabHistory();
    } catch (error: any) {
      console.error('Failed to re-grab:', error);
      alert(error.response?.data?.error || 'Failed to re-grab release');
    } finally {
      setRegrabbing(null);
    }
  };

  // Delete the event's file(s) from disk. The history row stays so the
  // release can be re-grabbed later; its status flips to Missing.
  const handleDeleteFile = async (item: GrabHistoryItem) => {
    if (!item.eventId) return;
    if (!confirm(`Delete the file(s) for "${item.eventTitle || item.title}" from disk? The entry stays in History so you can re-grab it later.`)) return;
    try {
      await apiClient.delete(`/events/${item.eventId}/files`);
      loadGrabHistory();
    } catch (error: any) {
      console.error('Failed to delete file:', error);
      alert(error.response?.data?.error || 'Failed to delete file');
    }
  };

  const handleBulkRegrab = async () => {
    try {
      setBulkRegrabbing(true);
      const response = await apiClient.post('/grab-history/regrab-missing');
      const data = response.data;
      alert(`Re-grabbed ${data.regrabbed} releases. ${data.failed} failed.`);
      loadGrabHistory();
    } catch (error: any) {
      console.error('Failed to bulk re-grab:', error);
      alert(error.response?.data?.error || 'Failed to re-grab missing releases');
    } finally {
      setBulkRegrabbing(false);
    }
  };

  const [isRefreshing, setIsRefreshing] = useState(false);
  const handleRefresh = async () => {
    // Without this state the click is silent: loadData() defaults to
    // showLoading=false, so no spinner toggles and the button click looks
    // dead even though the data is being refetched. Hold a button-local
    // "refreshing" state so the icon spins and the label flips for the
    // duration of the request, giving the click visible feedback.
    setIsRefreshing(true);
    try {
      if (activeTab === 'queue') {
        await loadQueue(true);
      } else if (activeTab === 'history') {
        await loadHistory(true);
      } else if (activeTab === 'grabHistory') {
        await loadGrabHistory(true);
      } else {
        await loadBlocklist(true);
      }
    } finally {
      setIsRefreshing(false);
    }
  };

  // Pack import handlers
  const handleShowPackPreview = async (pendingImport: PendingImport) => {
    try {
      setPackPreviewImport(pendingImport);
      setLoadingPackPreview(true);
      const response = await apiClient.get(`/pending-imports/${pendingImport.id}/pack-matches`);
      setPackMatches(response.data.matches || []);
    } catch (error: any) {
      console.error('Failed to load pack matches:', error);
      alert(error.response?.data?.error || 'Failed to load pack preview');
      setPackPreviewImport(null);
    } finally {
      setLoadingPackPreview(false);
    }
  };

  const handleImportPack = async (pendingImport: PendingImport) => {
    try {
      setImportingPack(pendingImport.id);
      const response = await apiClient.post(`/pending-imports/${pendingImport.id}/import-pack`);
      const data = response.data;
      alert(`Pack imported: ${data.filesImported} files imported, ${data.filesSkipped} skipped, ${data.filesDeleted} deleted`);
      setPackPreviewImport(null);
      loadQueue();
    } catch (error: any) {
      console.error('Failed to import pack:', error);
      alert(error.response?.data?.error || 'Failed to import pack');
    } finally {
      setImportingPack(null);
    }
  };

  const handleOpenRemoveQueueDialog = (item: QueueItem) => {
    setRemoveQueueDialog({
      type: 'queue',
      items: [{
        id: item.id,
        title: item.title,
        status: item.status,
        downloadClient: item.downloadClient
      }]
    });
    setRemovalMethod('removeFromClient'); // Reset to default
    setBlocklistAction('none'); // Reset to default
  };

  // Open remove dialog for multiple selected items. Pending imports run on a
  // separate code path (no blocklist routing, no per-client removal options),
  // so they're handled in a Promise.all alongside the dialog confirmation
  // rather than rolled into the queue-removal dialog UI.
  const handleOpenBulkRemoveDialog = () => {
    const selectedItems = queueRows
      .filter(item => selectedQueueIds.has(item.id))
      .map(item => ({
        id: item.id,
        title: item.title,
        status: item.status,
        downloadClient: item.downloadClient
      }));

    const selectedPendings = pendingImports.filter(p => selectedPendingIds.has(p.id));

    if (selectedItems.length === 0 && selectedPendings.length === 0) return;

    if (selectedItems.length === 0) {
      // Pending imports only: skip the blocklist/removal-method dialog and
      // remove straight away. Each pending import endpoint handles its own
      // client-side removal; we run them in parallel and refresh once.
      Promise.all(
        selectedPendings.map(p =>
          apiClient.post(`/pending-imports/${p.id}/remove-from-client`).catch(err => {
            console.error('Failed to remove pending import:', err);
          })
        )
      ).then(() => {
        clearRowSelections();
        loadQueue();
      });
      return;
    }

    setRemoveQueueDialog({
      type: 'queue',
      items: selectedItems
    });
    setRemovalMethod('removeFromClient'); // Reset to default
    setBlocklistAction('none'); // Reset to default
  };

  // Bulk Import: walk the selection and call the appropriate per-item import
  // endpoint (force-import for unmonitored rows, retry-import for failed-but-
  // downloaded rows). Disabled when the selection contains pending imports
  // or queue rows whose status doesn't expose an Import action.
  const handleBulkImport = async () => {
    if (!canBulkImport) return;
    const items = selectedQueueItems;
    try {
      await Promise.all(items.map(async item => {
        const isUnmonitored = item.statusMessages?.some(msg => msg.includes('no longer monitored')) ?? false;
        const canImport = isUnmonitored && (item.status === 5 || item.status === 3);
        if (canImport) {
          await apiClient.post(`/queue/${item.id}/import`);
        } else {
          await apiClient.post(`/queue/${item.id}/retry`);
        }
      }));
      clearRowSelections();
      loadQueue();
    } catch (error) {
      console.error('Bulk import failed:', error);
    }
  };

  const handleRemoveQueue = async () => {
    if (!removeQueueDialog || removeQueueDialog.items.length === 0) return;

    try {
      // Remove all items in parallel
      await Promise.all(
        removeQueueDialog.items.map(item =>
          apiClient.delete(`/queue/${item.id}`, {
            params: {
              removalMethod,
              blocklistAction
            }
          })
        )
      );
      // After confirming the queue-row removal dialog, also fan out to any
      // pending imports the user picked in the same selection — the dialog
      // doesn't display them but they were part of the bulk action.
      const pendingsToRemove = pendingImports.filter(p => selectedPendingIds.has(p.id));
      if (pendingsToRemove.length > 0) {
        await Promise.all(pendingsToRemove.map(p =>
          apiClient.post(`/pending-imports/${p.id}/remove-from-client`).catch(err => {
            console.error('Failed to remove pending import:', err);
          })
        ));
      }
      setRemoveQueueDialog(null);
      clearRowSelections();
      loadQueue();
    } catch (error) {
      console.error('Failed to remove queue item(s):', error);
    }
  };

  // Force import for unmonitored event downloads (Sonarr-style)
  const handleForceImport = async (item: QueueItem) => {
    try {
      await apiClient.post(`/queue/${item.id}/import`);
      loadQueue();
    } catch (error) {
      console.error('Failed to force import:', error);
    }
  };

  // Retry import for failed items (download complete but import failed)
  const handleRetryImport = async (item: QueueItem) => {
    try {
      await apiClient.post(`/queue/${item.id}/retry`);
      loadQueue();
    } catch (error: any) {
      console.error('Failed to retry import:', error);
      alert(error.response?.data?.error || 'Failed to retry import');
    }
  };

  // Delete download for unmonitored event (removes from client and queue)
  const handleDeleteUnmonitored = async (item: QueueItem) => {
    try {
      await apiClient.delete(`/queue/${item.id}`, {
        params: {
          removalMethod: 'removeFromClient',
          blocklistAction: 'none'
        }
      });
      loadQueue();
    } catch (error) {
      console.error('Failed to delete unmonitored download:', error);
    }
  };

  const handleOpenRemoveHistoryDialog = (item: HistoryItem) => {
    setRemoveHistoryDialog({
      type: 'history',
      id: item.id,
      eventId: item.eventId,
      title: item.destinationPath.split('/').pop() || item.destinationPath
    });
    setHistoryBlocklistAction('none'); // Reset to default
  };

  const handleDeleteHistory = async () => {
    if (!removeHistoryDialog) return;

    try {
      await apiClient.delete(`/history/${removeHistoryDialog.id}`, {
        params: {
          blocklistAction: historyBlocklistAction
        }
      });
      setRemoveHistoryDialog(null);
      loadHistory();
    } catch (error) {
      console.error('Failed to delete history item:', error);
    }
  };

  const handleOpenRemoveBlocklistDialog = (item: BlocklistItem) => {
    setRemoveBlocklistDialog({
      type: 'blocklist',
      id: item.id,
      eventId: item.eventId,
      title: item.title
    });
  };

  const handleDeleteBlocklist = async () => {
    if (!removeBlocklistDialog) return;

    try {
      await apiClient.delete(`/blocklist/${removeBlocklistDialog.id}`);
      setRemoveBlocklistDialog(null);
      loadBlocklist();
    } catch (error) {
      console.error('Failed to delete blocklist item:', error);
    }
  };

  const toggleBlocklistSelection = (id: number) => {
    setSelectedBlocklistIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const toggleSelectAllBlocklist = () => {
    setSelectedBlocklistIds(prev =>
      prev.size === blocklistItems.length
        ? new Set()
        : new Set(blocklistItems.map(i => i.id))
    );
  };

  const handleIgnorePendingImport = async (id: number) => {
    try {
      // Rejects the pending import: the row is removed and the file's path
      // is blocklisted, so scans and the file watcher stop rediscovering it.
      await apiClient.post(`/pending-imports/${id}/reject`);
      loadQueue();
    } catch (error) {
      console.error('Failed to ignore pending import:', error);
    }
  };

  const handleBulkDeleteBlocklist = async () => {
    try {
      await apiClient.post('/blocklist/bulk/delete', { ids: Array.from(selectedBlocklistIds) });
      setBulkRemoveBlocklistOpen(false);
      setSelectedBlocklistIds(new Set());
      loadBlocklist();
    } catch (error) {
      console.error('Failed to bulk delete blocklist items:', error);
    }
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
  };

  // SABnzbd / qBittorrent both report "0:00:00" / "00:00:00" when an item
  // isn't actually transferring right now (queued behind another download,
  // throttled, paused, no slot available). Showing "00:00:00 left" looks
  // like a buggy zero-ETA, so treat any all-zeros TimeSpan as "no value" and
  // let the UI fall back to a dash / hide the "left" suffix.
  //
  // .NET TimeSpan serializes as "[d.]hh:mm:ss[.fffffff]" — split on : or .
  // and check whether every numeric chunk is zero.
  const isMeaningfulTimeRemaining = (timeRemaining: string | null | undefined) => {
    if (!timeRemaining) return false;
    const trimmed = timeRemaining.trim();
    if (!trimmed) return false;
    return trimmed.split(/[:.]/).some(chunk => /\d/.test(chunk) && parseInt(chunk, 10) > 0);
  };

  // Use shared formatRelativeDate from timezone.ts which correctly parses UTC dates
  // (prevents phantom timezone offset in "Added" column)
  const formatDate = formatRelativeDate;

  const toggleColumn = (column: keyof ColumnVisibility) => {
    if (column === 'actions') return; // actions column is always visible, guarded here and via disabled checkbox

    const newVisibility = {
      ...columnVisibility,
      [column]: !columnVisibility[column],
      actions: true,
    };
    setColumnVisibility(newVisibility);
    localStorage.setItem('queueColumnVisibility', JSON.stringify(newVisibility));
  };

  const updatePageSize = (size: number) => {
    setPageSize(size);
    localStorage.setItem('queuePageSize', size.toString());
  };

  // Per-column width weights for the compact table's table-fixed layout.
  // Numbers are relative — the browser normalizes them so the visible
  // subset always fills 100% of the container, regardless of which
  // columns the user has enabled in View Options. Title / Event get more
  // weight because they show long text; badge / numeric columns get less.
  const COLUMN_WIDTH_WEIGHTS: Record<keyof ColumnVisibility, number> = {
    event: 14,
    title: 18,
    quality: 6,
    protocol: 7,
    indexer: 8,
    status: 9,
    progress: 7,
    size: 7,
    timeLeft: 6,
    client: 9,
    added: 7,
    actions: 7,
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

  // Filter by showUnknownEvents setting, then sort by the user's chosen field,
  // then truncate to the user-configured pageSize. Without the slice, the
  // pageSize input on the View Options modal had no effect and a large queue
  // (1k+ rows) would render every item on a single page. The Added DESC
  // default matches what the API returns, so users who never touch the sort
  // controls see the same ordering as before.
  const queueRowsAll = (showUnknownEvents
    ? queueItems
    : queueItems.filter(item => item.event && item.event.id)
  ).slice().sort((a, b) => {
    const dir = queueSortDirection === 'asc' ? 1 : -1;
    const cmpStr = (x: string | undefined, y: string | undefined) =>
      ((x ?? '').toLowerCase()).localeCompare((y ?? '').toLowerCase());
    const cmpNum = (x: number | undefined, y: number | undefined) =>
      (x ?? 0) - (y ?? 0);

    switch (queueSortField) {
      case 'event':    return dir * cmpStr(a.event?.title, b.event?.title);
      case 'title':    return dir * cmpStr(a.title, b.title);
      case 'quality':  return dir * cmpStr(a.quality, b.quality);
      // Status is a numeric enum but the user thinks of it as a category;
      // sort by enum value (which roughly orders Queued < Downloading <
      // Importing < Imported < Failed) so similar states cluster together.
      case 'status':   return dir * cmpNum(a.status, b.status);
      case 'progress': return dir * cmpNum(a.progress, b.progress);
      case 'size':     return dir * cmpNum(a.size, b.size);
      case 'client':   return dir * cmpStr(a.downloadClient?.name, b.downloadClient?.name);
      case 'added':    return dir * (new Date(a.added).getTime() - new Date(b.added).getTime());
      default:         return 0;
    }
  });

  // Pending imports follow the same sort the queue uses so clicking a
  // column header reorders both lists consistently. Field mapping mirrors
  // the queue comparator: pending imports don't have a queue status enum
  // or a progress percentage, so those fields fall back to sentinel
  // values that still produce a stable order under the same sort.
  const pendingImportsSorted = pendingImports.slice().sort((a, b) => {
    const dir = queueSortDirection === 'asc' ? 1 : -1;
    const cmpStr = (x: string | undefined, y: string | undefined) =>
      ((x ?? '').toLowerCase()).localeCompare((y ?? '').toLowerCase());
    const cmpNum = (x: number | undefined, y: number | undefined) =>
      (x ?? 0) - (y ?? 0);

    switch (queueSortField) {
      case 'event':    return dir * cmpStr(a.suggestedEvent?.title, b.suggestedEvent?.title);
      case 'title':    return dir * cmpStr(a.title, b.title);
      case 'quality':  return dir * cmpStr(a.quality, b.quality);
      case 'status':   return 0;
      case 'progress': return 0;
      case 'size':     return dir * cmpNum(a.size, b.size);
      case 'client':   return dir * cmpStr(a.downloadClient?.name, b.downloadClient?.name);
      case 'added':    return dir * (new Date(a.detected || 0).getTime() - new Date(b.detected || 0).getTime());
      default:         return 0;
    }
  });

  // Page size budgets BOTH pending imports and queue rows. Pending imports
  // get first dibs because they're more actionable (require user mapping)
  // and typically few; whatever budget remains goes to queue rows. Without
  // this, "Page Size 200" with 95 pending imports rendered 295 rows total
  // and the bulk-select count silently exceeded the page size.
  const visiblePendingImports = pendingImportsSorted.slice(0, pageSize);
  const queueRowBudget = Math.max(0, pageSize - visiblePendingImports.length);
  const queueRows = queueRowsAll.slice(0, queueRowBudget);
  const totalAvailable = pendingImports.length + queueRowsAll.length;
  const totalVisible = visiblePendingImports.length + queueRows.length;
  const totalHidden = totalAvailable - totalVisible;

  // Combined render-order list of selectable rows. Pending imports render
  // above regular queue rows in both compact and spacious views, so order
  // here mirrors that. The keys ('p-' / 'q-' prefix + numeric id) drive
  // shift-click range selection and select-all across both kinds.
  const selectableRowKeys: string[] = [
    ...visiblePendingImports.map(p => `p-${p.id}`),
    ...queueRows.map(q => `q-${q.id}`),
  ];

  const totalSelected = selectedQueueIds.size + selectedPendingIds.size;
  const totalSelectable = selectableRowKeys.length;

  // Toggle a single row, with optional shift-click semantics: when shift is
  // held and there's a previous anchor row, every row between the anchor and
  // the click target is forced into the same state as the click target.
  const toggleSelectRow = (kind: 'q' | 'p', id: number, withShift: boolean) => {
    const key = `${kind}-${id}`;
    const currentlySelected = kind === 'q' ? selectedQueueIds.has(id) : selectedPendingIds.has(id);
    const nextSelected = !currentlySelected;

    if (withShift && lastSelectedKey && lastSelectedKey !== key) {
      const anchor = selectableRowKeys.indexOf(lastSelectedKey);
      const target = selectableRowKeys.indexOf(key);
      if (anchor !== -1 && target !== -1) {
        const [lo, hi] = anchor < target ? [anchor, target] : [target, anchor];
        const rangeKeys = selectableRowKeys.slice(lo, hi + 1);
        setSelectedQueueIds(prev => {
          const next = new Set(prev);
          rangeKeys.filter(k => k.startsWith('q-')).forEach(k => {
            const rid = parseInt(k.slice(2));
            if (nextSelected) next.add(rid); else next.delete(rid);
          });
          return next;
        });
        setSelectedPendingIds(prev => {
          const next = new Set(prev);
          rangeKeys.filter(k => k.startsWith('p-')).forEach(k => {
            const rid = parseInt(k.slice(2));
            if (nextSelected) next.add(rid); else next.delete(rid);
          });
          return next;
        });
        setLastSelectedKey(key);
        return;
      }
    }

    if (kind === 'q') {
      setSelectedQueueIds(prev => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id); else next.add(id);
        return next;
      });
    } else {
      setSelectedPendingIds(prev => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id); else next.add(id);
        return next;
      });
    }
    setLastSelectedKey(key);
  };

  const toggleSelectAllQueue = () => {
    if (totalSelected === totalSelectable && totalSelectable > 0) {
      setSelectedQueueIds(new Set());
      setSelectedPendingIds(new Set());
    } else {
      setSelectedQueueIds(new Set(queueRows.map(item => item.id)));
      setSelectedPendingIds(new Set(visiblePendingImports.map(p => p.id)));
    }
  };

  const clearRowSelections = () => {
    setSelectedQueueIds(new Set());
    setSelectedPendingIds(new Set());
    setLastSelectedKey(null);
  };

  const isAllQueueSelected = totalSelectable > 0 && totalSelected === totalSelectable;
  const isSomeQueueSelected = totalSelected > 0 && totalSelected < totalSelectable;

  // Bulk Import is only valid for queue items where a per-item Import action
  // is already exposed (force-import on unmonitored Warning/Completed rows
  // and retry-import on failed-but-downloaded rows). Pending imports require
  // per-item event mapping via the manual import dialog so they can't ride
  // along on a bulk import.
  const isQueueRowImportable = (item: QueueItem): boolean => {
    const isUnmonitored = item.statusMessages?.some(msg => msg.includes('no longer monitored')) ?? false;
    const canImport = isUnmonitored && (item.status === 5 || item.status === 3);
    const canRetryImport = item.status === 4 && item.progress >= 100;
    return canImport || canRetryImport;
  };

  const selectedQueueItems = queueRows.filter(item => selectedQueueIds.has(item.id));
  const canBulkImport =
    totalSelected > 0 &&
    selectedPendingIds.size === 0 &&
    selectedQueueItems.length === selectedQueueIds.size &&
    selectedQueueItems.every(isQueueRowImportable);

  const bulkImportDisabledReason = (() => {
    if (totalSelected === 0) return 'Select rows to import';
    if (selectedPendingIds.size > 0) return 'Pending imports require per-item event mapping; remove them from the selection or open them individually';
    if (!selectedQueueItems.every(isQueueRowImportable)) return 'One or more selected items are still downloading or already imported';
    return '';
  })();

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
          <td key="event" className="px-3 py-1.5 overflow-hidden">
            <div className="text-white text-xs font-medium truncate" title={item.event?.title || 'Unknown Event'}>
              {item.event?.title || 'Unknown Event'}
              {item.part && <span className="text-blue-400 ml-1">({item.part})</span>}
            </div>
            <div className="text-xs text-gray-400 truncate" title={item.event?.organization}>{item.event?.organization}</div>
          </td>
        );
      case 'title':
        return (
          <td key="title" className="px-3 py-1.5 overflow-hidden">
            <div className="text-gray-300 text-xs truncate" title={item.title}>{item.title}</div>
          </td>
        );
      case 'quality':
        return (
          <td key="quality" className="px-2 py-1.5 text-center">
            <div className="flex items-center justify-center gap-1 flex-wrap">
              <span className={BADGE_PURPLE}>{item.quality || 'Unknown'}</span>
              {cfScoreBadge(item.customFormatScore)}
            </div>
          </td>
        );
      case 'protocol':
        return (
          <td key="protocol" className="px-2 py-1.5 text-center">
            <span className={`${BADGE_BLUE} uppercase`}>{item.protocol || 'Unknown'}</span>
          </td>
        );
      case 'indexer':
        return (
          <td key="indexer" className="px-2 py-1.5 text-center overflow-hidden">
            <div className="text-gray-400 text-xs truncate" title={item.indexer || 'Unknown'}>{item.indexer || 'Unknown'}</div>
          </td>
        );
      case 'status':
        return (
          <td key="status" className="px-2 py-1.5 text-center">
            <div className={`flex items-center justify-center gap-1 ${statusColors[item.status]}`}>
              {getStatusIcon(item.status)}
              <span className="text-xs">{statusNames[item.status]}</span>
            </div>
            {item.statusMessages && item.statusMessages.length > 0 && (
              <div className="text-xs text-orange-400 truncate max-w-[120px] mx-auto" title={item.statusMessages.join(' · ')}>
                {item.statusMessages[0]}
              </div>
            )}
            {item.errorMessage && !item.statusMessages?.length && (
              <div className="text-xs text-red-400 truncate max-w-[120px] mx-auto" title={item.errorMessage}>{item.errorMessage}</div>
            )}
          </td>
        );
      case 'progress':
        return (
          <td key="progress" className="px-2 py-1.5 overflow-hidden">
            <div className="flex items-center gap-1.5 w-full">
              <div className="flex-1 bg-gray-700 rounded-full h-1.5 min-w-0">
                <div
                  className="bg-red-600 h-1.5 rounded-full transition-all"
                  style={{ width: `${item.progress}%` }}
                />
              </div>
              <span className="text-xs text-gray-400 text-right flex-shrink-0">{item.progress.toFixed(0)}%</span>
            </div>
          </td>
        );
      case 'size':
        return (
          <td key="size" className="px-2 py-1.5 text-center overflow-hidden">
            <div className="text-gray-300 text-xs truncate" title={`${formatBytes(item.downloaded)} / ${formatBytes(item.size)}`}>
              {formatBytes(item.downloaded)} / {formatBytes(item.size)}
            </div>
          </td>
        );
      case 'timeLeft':
        return (
          <td key="timeLeft" className="px-2 py-1.5 text-center overflow-hidden">
            <div className="text-gray-400 text-xs truncate">{isMeaningfulTimeRemaining(item.timeRemaining) ? item.timeRemaining : '—'}</div>
          </td>
        );
      case 'client':
        return (
          <td key="client" className="px-2 py-1.5 text-center overflow-hidden">
            <div className="text-gray-400 text-xs truncate" title={item.downloadClient?.name || 'Unknown'}>{item.downloadClient?.name || 'Unknown'}</div>
          </td>
        );
      case 'added':
        return (
          <td key="added" className="px-2 py-1.5 text-center overflow-hidden">
            <div className="text-gray-400 text-xs truncate" title={formatDate(item.added)}>{formatDate(item.added)}</div>
          </td>
        );
      case 'actions':
        const isUnmonitored = item.statusMessages?.some(msg => msg.includes('no longer monitored'));
        // Show import button for Warning (5) or Completed (3) status when unmonitored
        const canImport = isUnmonitored && (item.status === 5 || item.status === 3);
        // Show retry import button for Failed (4) items that have completed download (100% progress)
        const canRetryImport = item.status === 4 && item.progress >= 100;
        return (
          <td key="actions" className="px-2 py-1.5">
            <div className="flex items-center justify-end gap-1">
              {/* Show Retry Import button for failed imports (download complete but import failed) */}
              {canRetryImport && (
                <button
                  onClick={() => handleRetryImport(item)}
                  className={BUTTON_ICON_WARNING}
                  title="Retry Import"
                >
                  <ArrowPathIcon className="w-4 h-4" />
                </button>
              )}
              {/* Show Import/Delete buttons for unmonitored downloads (Sonarr-style) */}
              {canImport && (
                <>
                  <button
                    onClick={() => handleForceImport(item)}
                    className={BUTTON_ICON_SUCCESS}
                    title="Import Anyway"
                  >
                    <DocumentCheckIcon className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleDeleteUnmonitored(item)}
                    className={BUTTON_ICON_DESTRUCTIVE}
                    title="Delete Download"
                  >
                    <TrashIcon className="w-4 h-4" />
                  </button>
                </>
              )}
              {/* Regular remove button (only show when not already showing delete button for unmonitored) */}
              {!canImport && (
                <button
                  onClick={() => handleOpenRemoveQueueDialog(item)}
                  className={BUTTON_ICON_DESTRUCTIVE}
                  title="Remove"
                >
                  <TrashIcon className="w-4 h-4" />
                </button>
              )}
            </div>
          </td>
        );
      default:
        return null;
    }
  };

  const getStatusIcon = (status: number) => {
    switch (status) {
      case 0: return <ClockIcon className="w-4 h-4" />;
      case 1: return <ArrowDownTrayIcon className="w-4 h-4 animate-bounce" />;
      case 2: return <XCircleIcon className="w-4 h-4" />;
      case 3: return <CheckCircleIcon className="w-4 h-4" />;
      case 4: return <XCircleIcon className="w-4 h-4" />;
      case 5: return <ExclamationTriangleIcon className="w-4 h-4" />;
      case 6: return <DocumentCheckIcon className="w-4 h-4 animate-pulse" />;
      case 7: return <CheckCircleIcon className="w-4 h-4" />;
      default: return <ClockIcon className="w-4 h-4" />;
    }
  };

  const getDecisionIcon = (decision: number) => {
    switch (decision) {
      case 0: return <CheckCircleIcon className="w-4 h-4" />;
      case 1: return <XCircleIcon className="w-4 h-4" />;
      case 2: return <ExclamationTriangleIcon className="w-4 h-4" />;
      case 3: return <ArrowPathIcon className="w-4 h-4" />;
      default: return <CheckCircleIcon className="w-4 h-4" />;
    }
  };

  // For multi-select, check if ANY item is completed and has post-import category option
  const anyCompleted = removeQueueDialog?.items.some(item => item.status === 3 || item.status === 7);
  const anyHasPostImportCategory = removeQueueDialog?.items.some(
    item => item.downloadClient?.postImportCategory != null && item.downloadClient?.postImportCategory !== ''
  );
  const showChangeCategory = anyCompleted && anyHasPostImportCategory;

  return (
    <PageShell>
        <PageHeader
          title="Activity"
          subtitle="Monitor grabs and import history"
          actions={
            <>
              {activeTab === 'queue' && (
                <button
                  onClick={() => setShowTableOptions(true)}
                  className="flex items-center rounded-lg bg-gray-700 px-3 py-2 text-white transition-colors hover:bg-gray-600 md:px-4"
                  title="View Options"
                >
                  <Cog6ToothIcon className="w-5 h-5" />
                </button>
              )}
              <button
                onClick={handleRefresh}
                disabled={isRefreshing}
                className="flex items-center rounded-lg bg-red-600 px-3 py-2 text-white transition-colors hover:bg-red-700 md:px-4 disabled:opacity-70 disabled:cursor-wait"
              >
                <ArrowPathIcon className={`w-5 h-5 md:mr-2 ${isRefreshing ? 'animate-spin' : ''}`} />
                <span className="hidden md:inline">{isRefreshing ? 'Refreshing...' : 'Refresh'}</span>
              </button>
            </>
          }
        />

        <SegmentedTabs
            items={[
              { key: 'queue', label: 'Queue', badge: (queueItems.length + pendingImports.length) || null },
              { key: 'grabHistory', label: 'History' },
              { key: 'missing', label: 'Missing' },
              { key: 'cutoffUnmet', label: 'Cutoff Unmet' },
              { key: 'blocklist', label: 'Blocklist', badge: blocklistItems.length || null },
            ]}
            value={activeTab}
            onChange={(tab) => {
              setActiveTab(tab);
              setPage(1);
            }}
          />

        {/* Content */}
        {isLoading ? (
          <div className="text-center py-12">
            <div className="inline-block animate-spin rounded-full h-12 w-12 border-4 border-red-600 border-t-transparent"></div>
            <p className="mt-4 text-gray-400">Loading...</p>
          </div>
        ) : activeTab === 'queue' ? (
          // Queue Tab
          <div className="rounded-lg overflow-hidden">
            {queueRows.length === 0 && pendingImports.length === 0 ? (
              <div className="p-12 text-center text-gray-400">
                <ArrowDownTrayIcon className="w-16 h-16 mx-auto mb-4 opacity-50" />
                <p className="text-lg">No active downloads</p>
                <p className="text-sm mt-2">Downloads will appear here when events are searched and sent to download clients</p>
              </div>
            ) : (
              <>
              {/* Bulk Action Bar - Shows when items are selected */}
              {totalSelected > 0 && (
                <div className="px-4 py-3 bg-gray-800 border-b border-gray-700 flex items-center justify-between">
                  <span className="text-gray-300 text-sm">
                    {totalSelected} item{totalSelected !== 1 ? 's' : ''} selected
                  </span>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={clearRowSelections}
                      className="px-3 py-1.5 text-gray-400 hover:text-white text-sm transition-colors"
                    >
                      Clear Selection
                    </button>
                    <button
                      onClick={handleBulkImport}
                      disabled={!canBulkImport}
                      title={canBulkImport ? 'Import selected items' : bulkImportDisabledReason}
                      className={`px-4 py-1.5 text-white text-sm rounded transition-colors flex items-center gap-2 ${
                        canBulkImport
                          ? 'bg-green-600 hover:bg-green-700'
                          : 'bg-gray-700 text-gray-400 cursor-not-allowed'
                      }`}
                    >
                      <DocumentCheckIcon className="w-4 h-4" />
                      Import Selected
                    </button>
                    <button
                      onClick={handleOpenBulkRemoveDialog}
                      className="px-4 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm rounded transition-colors flex items-center gap-2"
                    >
                      <TrashIcon className="w-4 h-4" />
                      Remove Selected
                    </button>
                  </div>
                </div>
              )}
              {compactView ? (
                <div>
                  {/* table-fixed + a colgroup of weighted widths means the
                      table always fills 100% of the available horizontal
                      space without overflowing the viewport. Cells truncate
                      with ellipses inside their assigned column width
                      instead of forcing a horizontal scrollbar. */}
                  <table className="w-full table-fixed">
                    <colgroup>
                      <col style={{ width: '40px' }} />
                      {columnOrder.map(column => {
                        if (!columnVisibility[column]) return null;
                        return <col key={column} style={{ width: `${COLUMN_WIDTH_WEIGHTS[column]}%` }} />;
                      })}
                    </colgroup>
                    <thead>
                      <tr className="bg-gray-800 text-gray-300 text-xs">
                        {/* Select All Checkbox */}
                        <th className="px-3 py-1.5 w-10">
                          <input
                            type="checkbox"
                            checked={isAllQueueSelected}
                            ref={(el) => {
                              if (el) el.indeterminate = isSomeQueueSelected;
                            }}
                            onChange={toggleSelectAllQueue}
                            className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2 cursor-pointer"
                            title={isAllQueueSelected ? 'Deselect all' : 'Select all'}
                          />
                        </th>
                        {columnOrder.map(column => {
                          if (!columnVisibility[column]) return null;
                          const align = column === 'event' || column === 'title' ? 'text-left' : column === 'actions' ? 'text-right' : 'text-center';
                          // Map column key → sort field. Not every column is
                          // sortable: protocol/indexer/timeLeft/actions don't
                          // have a meaningful sort, so they render as plain
                          // labels.
                          const sortFieldForColumn: Partial<Record<keyof ColumnVisibility, QueueSortField>> = {
                            event: 'event', title: 'title', quality: 'quality',
                            status: 'status', progress: 'progress', size: 'size',
                            client: 'client', added: 'added',
                          };
                          const sortField = sortFieldForColumn[column];
                          const isSorted = sortField && sortField === queueSortField;
                          const SortIcon = !sortField
                            ? null
                            : isSorted
                              ? (queueSortDirection === 'asc' ? ChevronUpIcon : ChevronDownIcon)
                              : ChevronUpDownIcon;
                          const justify = align === 'text-left' ? 'justify-start' : align === 'text-right' ? 'justify-end' : 'justify-center';
                          return (
                            <th
                              key={column}
                              className={`${align === 'text-left' ? 'px-3' : 'px-2'} py-1.5 ${align} font-medium ${sortField ? 'cursor-pointer hover:text-white select-none' : ''}`}
                              onClick={sortField ? () => handleSortFieldChange(sortField) : undefined}
                              title={sortField ? `Sort by ${getColumnLabel(column)}` : undefined}
                            >
                              <span className={`inline-flex items-center gap-1 ${justify}`}>
                                {getColumnLabel(column)}
                                {SortIcon && (
                                  <SortIcon className={`w-3 h-3 ${isSorted ? 'text-white' : 'text-gray-500'}`} />
                                )}
                              </span>
                            </th>
                          );
                        })}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-700">
                      {/* Pending Imports - External downloads needing manual mapping */}
                      {visiblePendingImports.map((pendingImport) => (
                        <tr
                          key={`pending-${pendingImport.id}`}
                          className={`${pendingImport.isPack ? 'bg-purple-900/10 hover:bg-purple-900/20 border-l-4 border-purple-500' : 'bg-yellow-900/10 hover:bg-yellow-900/20 border-l-4 border-yellow-500'} transition-colors ${selectedPendingIds.has(pendingImport.id) ? 'ring-1 ring-red-600' : ''}`}
                        >
                          <td className="px-3 py-1.5 w-10">
                            <input
                              type="checkbox"
                              checked={selectedPendingIds.has(pendingImport.id)}
                              onClick={(e) => {
                                e.stopPropagation();
                                toggleSelectRow('p', pendingImport.id, e.shiftKey);
                              }}
                              onChange={() => { /* handled by onClick to read shiftKey */ }}
                              className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2 cursor-pointer"
                            />
                          </td>
                          {columnOrder.map(column => {
                            if (!columnVisibility[column]) return null;
                            switch (column) {
                              case 'event':
                                return (
                                  <td key="event" className="px-3 py-1.5 overflow-hidden">
                                    <div className="text-white text-xs font-medium flex items-center gap-1.5 min-w-0">
                                      {pendingImport.isPack && (
                                        <span className="px-1.5 py-0.5 bg-purple-600 text-white text-xs rounded-full flex-shrink-0">PACK</span>
                                      )}
                                      <span className="truncate min-w-0" title={pendingImport.suggestedEvent?.title}>
                                        {pendingImport.suggestedEvent?.title
                                          ? <>
                                              {pendingImport.suggestedEvent.title}
                                              {!pendingImport.isPack && <span className="text-gray-500 font-normal ml-1">({pendingImport.suggestionConfidence}%)</span>}
                                              {pendingImport.isPack && <span className="text-gray-500 font-normal ml-1">· {pendingImport.fileCount} files, {pendingImport.matchedEventsCount} matched</span>}
                                            </>
                                          : pendingImport.isPack
                                            ? <span className="text-gray-400">{pendingImport.fileCount} files · {pendingImport.matchedEventsCount} matched</span>
                                            : <span className="text-gray-500 italic">No match found</span>
                                        }
                                      </span>
                                    </div>
                                    <div className="text-xs text-gray-500">Manual Import</div>
                                  </td>
                                );
                              case 'title':
                                return (
                                  <td key="title" className="px-3 py-1.5 overflow-hidden">
                                    <div className="text-gray-300 text-xs truncate" title={pendingImport.title}>{pendingImport.title}</div>
                                  </td>
                                );
                              case 'quality':
                                return (
                                  <td key="quality" className="px-2 py-1.5 text-center">
                                    {pendingImport.quality
                                      ? <span className={BADGE_PURPLE}>{pendingImport.quality}</span>
                                      : <span className="text-gray-600 text-xs">—</span>}
                                  </td>
                                );
                              case 'protocol':
                                return (
                                  <td key="protocol" className="px-2 py-1.5 text-center">
                                    {pendingImport.protocol
                                      ? <span className={`${BADGE_BLUE} uppercase`}>{pendingImport.protocol}</span>
                                      : <span className="text-gray-600 text-xs">—</span>}
                                  </td>
                                );
                              case 'indexer':
                                return (
                                  <td key="indexer" className="px-2 py-1.5 text-center">
                                    <span className="text-gray-600 text-xs">—</span>
                                  </td>
                                );
                              case 'status':
                                return (
                                  <td key="status" className="px-2 py-1.5 text-center">
                                    <div className={`flex items-center justify-center gap-1 ${pendingImport.isPack ? 'text-purple-400' : 'text-yellow-400'}`}>
                                      <ExclamationCircleIcon className="w-4 h-4" />
                                      <span className="text-xs">{pendingImport.isPack ? 'Pack Import' : 'Manual Import'}</span>
                                    </div>
                                  </td>
                                );
                              case 'progress':
                                return (
                                  <td key="progress" className="px-2 py-1.5 text-center">
                                    <span className="text-gray-600 text-xs">—</span>
                                  </td>
                                );
                              case 'size':
                                return (
                                  <td key="size" className="px-2 py-1.5 text-center">
                                    <div className="text-gray-300 text-xs whitespace-nowrap">
                                      {pendingImport.size ? formatBytes(pendingImport.size) : '—'}
                                    </div>
                                  </td>
                                );
                              case 'timeLeft':
                                return (
                                  <td key="timeLeft" className="px-2 py-1.5 text-center">
                                    <span className="text-gray-600 text-xs">—</span>
                                  </td>
                                );
                              case 'client':
                                return (
                                  <td key="client" className="px-2 py-1.5 text-center">
                                    <span className="text-gray-400 text-xs">{pendingImport.downloadClient?.name || '—'}</span>
                                  </td>
                                );
                              case 'added':
                                return (
                                  <td key="added" className="px-2 py-1.5 text-center">
                                    <span className="text-gray-400 text-xs">{pendingImport.detected ? formatDate(pendingImport.detected) : '—'}</span>
                                  </td>
                                );
                              case 'actions':
                                return (
                                  <td key="actions" className="px-2 py-1.5">
                                    <div className="flex items-center justify-end gap-1">
                                      {pendingImport.isPack ? (
                                        <>
                                          <button
                                            onClick={() => handleShowPackPreview(pendingImport)}
                                            className={BUTTON_ICON_SECONDARY}
                                            title="Preview which files will be imported"
                                          >
                                            <EyeIcon className="w-4 h-4" />
                                          </button>
                                          <button
                                            onClick={() => handleImportPack(pendingImport)}
                                            disabled={importingPack === pendingImport.id}
                                            className={BUTTON_ICON_INFO}
                                            title="Import all matching files from this pack"
                                          >
                                            {importingPack === pendingImport.id
                                              ? <ArrowPathIcon className="w-4 h-4 animate-spin" />
                                              : <DocumentCheckIcon className="w-4 h-4" />}
                                          </button>
                                        </>
                                      ) : (
                                        <button
                                          onClick={() => setSelectedPendingImport(pendingImport)}
                                          className={BUTTON_ICON_WARNING}
                                          title="Manual Import"
                                        >
                                          <DocumentCheckIcon className="w-4 h-4" />
                                        </button>
                                      )}
                                      <button
                                        onClick={() => handleIgnorePendingImport(pendingImport.id)}
                                        className={BUTTON_ICON_SECONDARY}
                                        title="Ignore this file: it stays on disk but Sportarr stops detecting or suggesting it (undo from the Blocklist tab)"
                                      >
                                        <NoSymbolIcon className="w-4 h-4" />
                                      </button>
                                      <button
                                        onClick={async () => {
                                          try {
                                            // Remove from download client AND pending imports list
                                            await apiClient.post(`/pending-imports/${pendingImport.id}/remove-from-client`);
                                            loadQueue();
                                          } catch (error) {
                                            console.error('Failed to remove pending import from client:', error);
                                          }
                                        }}
                                        className={BUTTON_ICON_DESTRUCTIVE}
                                        title="Remove download from client and delete files"
                                      >
                                        <TrashIcon className="w-4 h-4" />
                                      </button>
                                    </div>
                                  </td>
                                );
                              default:
                                return null;
                            }
                          })}
                        </tr>
                      ))}

                      {/* Regular Queue Items */}
                      {queueRows.map((item) => (
                        <tr
                          key={item.id}
                          className={`hover:bg-gray-800/50 transition-colors ${selectedQueueIds.has(item.id) ? 'bg-red-900/20 ring-1 ring-red-600/40' : ''}`}
                        >
                          {/* Row Checkbox - shift-click extends selection from
                              the previous click anchor across both queue rows
                              and pending imports. */}
                          <td className="px-3 py-1.5 w-10">
                            <input
                              type="checkbox"
                              checked={selectedQueueIds.has(item.id)}
                              onClick={(e) => {
                                e.stopPropagation();
                                toggleSelectRow('q', item.id, e.shiftKey);
                              }}
                              onChange={() => { /* handled by onClick to read shiftKey */ }}
                              className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2 cursor-pointer"
                            />
                          </td>
                          {columnOrder.map(column => {
                            if (!columnVisibility[column]) return null;
                            return renderCell(column, item);
                          })}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                /* Spacious: card grid */
                <div>
                  {/* Mobile-only sort + select-all bar. The full table-style
                      header below would cram its column labels into ~40 px
                      each on a phone viewport (labels overlapping each
                      other), and the cards under it already display every
                      field anyway, so on mobile we collapse the header into
                      a single Sort select + direction toggle + Select All
                      checkbox. The desktop table header continues to be
                      the source of truth above sm. */}
                  <div className="sm:hidden flex items-center gap-2 bg-gray-800 text-gray-300 text-xs px-3 py-2">
                    <input
                      type="checkbox"
                      checked={isAllQueueSelected}
                      ref={(el) => {
                        if (el) el.indeterminate = isSomeQueueSelected;
                      }}
                      onChange={toggleSelectAllQueue}
                      className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2 cursor-pointer flex-shrink-0"
                      title={isAllQueueSelected ? 'Deselect all' : 'Select all'}
                    />
                    <span className="text-gray-400 flex-shrink-0">Sort:</span>
                    <select
                      value={queueSortField}
                      onChange={(e) => setQueueSortField(e.target.value as QueueSortField)}
                      className="flex-1 min-w-0 px-2 py-1 bg-gray-800 border border-gray-700 text-gray-200 rounded focus:outline-none focus:ring-1 focus:ring-red-500"
                    >
                      <option value="event">Event</option>
                      <option value="title">Episode Title</option>
                      <option value="quality">Quality</option>
                      <option value="status">Status</option>
                      <option value="progress">Progress</option>
                      <option value="size">Size</option>
                      <option value="client">Download Client</option>
                      <option value="added">Added</option>
                    </select>
                    <button
                      onClick={() => setQueueSortDirection(d => d === 'asc' ? 'desc' : 'asc')}
                      className="p-1.5 bg-gray-700 hover:bg-gray-600 rounded flex-shrink-0"
                      title={`Sort direction: ${queueSortDirection === 'asc' ? 'ascending' : 'descending'}`}
                    >
                      {queueSortDirection === 'asc'
                        ? <ChevronUpIcon className="w-4 h-4" />
                        : <ChevronDownIcon className="w-4 h-4" />}
                    </button>
                  </div>
                  {/* Desktop header table: same table-fixed + colgroup +
                      thead used by compact mode, but with no tbody. Re-using
                      the table structure (rather than emulating it with a
                      flex bar) means the header is visually identical to
                      compact — full-width gray bar that goes edge-to-edge
                      with column labels distributed by the same column
                      weights. Hidden below sm because the labels don't fit
                      on a phone-width viewport. */}
                  <table className="hidden sm:table w-full table-fixed">
                    <colgroup>
                      <col style={{ width: '40px' }} />
                      {columnOrder.map(column => {
                        if (!columnVisibility[column]) return null;
                        return <col key={column} style={{ width: `${COLUMN_WIDTH_WEIGHTS[column]}%` }} />;
                      })}
                    </colgroup>
                    <thead>
                      <tr className="bg-gray-800 text-gray-300 text-xs">
                        <th className="px-3 py-1.5 w-10">
                          <input
                            type="checkbox"
                            checked={isAllQueueSelected}
                            ref={(el) => {
                              if (el) el.indeterminate = isSomeQueueSelected;
                            }}
                            onChange={toggleSelectAllQueue}
                            className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2 cursor-pointer"
                            title={isAllQueueSelected ? 'Deselect all' : 'Select all'}
                          />
                        </th>
                        {columnOrder.map(column => {
                          if (!columnVisibility[column]) return null;
                          const align = column === 'event' || column === 'title' ? 'text-left' : column === 'actions' ? 'text-right' : 'text-center';
                          const sortFieldForColumn: Partial<Record<keyof ColumnVisibility, QueueSortField>> = {
                            event: 'event', title: 'title', quality: 'quality',
                            status: 'status', progress: 'progress', size: 'size',
                            client: 'client', added: 'added',
                          };
                          const sortField = sortFieldForColumn[column];
                          const isSorted = sortField && sortField === queueSortField;
                          const SortIcon = !sortField
                            ? null
                            : isSorted
                              ? (queueSortDirection === 'asc' ? ChevronUpIcon : ChevronDownIcon)
                              : ChevronUpDownIcon;
                          const justify = align === 'text-left' ? 'justify-start' : align === 'text-right' ? 'justify-end' : 'justify-center';
                          return (
                            <th
                              key={column}
                              className={`${align === 'text-left' ? 'px-3' : 'px-2'} py-1.5 ${align} font-medium ${sortField ? 'cursor-pointer hover:text-white select-none' : ''}`}
                              onClick={sortField ? () => handleSortFieldChange(sortField) : undefined}
                              title={sortField ? `Sort by ${getColumnLabel(column)}` : undefined}
                            >
                              <span className={`inline-flex items-center gap-1 ${justify}`}>
                                {getColumnLabel(column)}
                                {SortIcon && (
                                  <SortIcon className={`w-3 h-3 ${isSorted ? 'text-white' : 'text-gray-500'}`} />
                                )}
                              </span>
                            </th>
                          );
                        })}
                      </tr>
                    </thead>
                  </table>
                  {/* Card list — independent flow below the header so cards
                      don't have to align to the column grid above. */}
                  <div className="space-y-3 mt-3">
                  {/* Pending Import Cards */}
                  {visiblePendingImports.map((pendingImport) => (
                    <div
                      key={`pending-${pendingImport.id}`}
                      className={`bg-gray-800 border rounded-lg p-4 hover:bg-gray-750 transition-colors ${selectedPendingIds.has(pendingImport.id) ? 'border-red-600' : pendingImport.isPack ? 'border-purple-700' : 'border-yellow-700'}`}
                    >
                      <div className="flex items-start justify-between">
                        <input
                          type="checkbox"
                          checked={selectedPendingIds.has(pendingImport.id)}
                          onClick={(e) => {
                            e.stopPropagation();
                            toggleSelectRow('p', pendingImport.id, e.shiftKey);
                          }}
                          onChange={() => { /* handled by onClick to read shiftKey */ }}
                          className="mt-1.5 mr-3 w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2 cursor-pointer flex-shrink-0"
                        />
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-3 mb-2 flex-wrap">
                            <h3 className="text-lg font-semibold text-white">
                              {pendingImport.suggestedEvent?.title || (
                                pendingImport.isPack
                                  ? <span className="text-gray-400 font-normal">{pendingImport.fileCount} files · {pendingImport.matchedEventsCount} matched</span>
                                  : <span className="text-gray-500 italic font-normal">No match found</span>
                              )}
                            </h3>
                            <span className={`px-2 py-1 text-xs rounded ${pendingImport.isPack ? 'bg-purple-600/30 text-purple-300' : 'bg-yellow-700/30 text-yellow-300'}`}>
                              {pendingImport.isPack ? 'Pack' : 'Manual Import'}
                            </span>
                            {!pendingImport.isPack && pendingImport.suggestedEvent && (
                              <span className="px-2 py-1 bg-gray-700 text-gray-400 text-xs rounded">{pendingImport.suggestionConfidence}% match</span>
                            )}
                            {pendingImport.isPack && pendingImport.suggestedEvent && (
                              <span className="text-gray-500 text-xs">{pendingImport.fileCount} files · {pendingImport.matchedEventsCount} matched</span>
                            )}
                            {pendingImport.quality && <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">{pendingImport.quality}</span>}
                            {pendingImport.protocol && <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded uppercase">{pendingImport.protocol}</span>}
                          </div>
                          <p className="text-sm text-gray-400 truncate mb-1">{pendingImport.title}</p>
                          <div className="flex items-center gap-4 text-sm text-gray-500 flex-wrap">
                            {pendingImport.size > 0 && <span>{formatBytes(pendingImport.size)}</span>}
                            {pendingImport.downloadClient?.name && <><span className="text-gray-600">•</span><span>{pendingImport.downloadClient.name}</span></>}
                            {pendingImport.detected && <><span className="text-gray-600">•</span><span>{formatDate(pendingImport.detected)}</span></>}
                          </div>
                        </div>
                        <div className="flex items-center gap-2 ml-4">
                          {pendingImport.isPack ? (
                            <>
                              <button
                                onClick={() => handleShowPackPreview(pendingImport)}
                                className={BUTTON_SECONDARY}
                              >
                                <EyeIcon className="w-4 h-4" />
                                Preview
                              </button>
                              <button
                                onClick={() => handleImportPack(pendingImport)}
                                disabled={importingPack === pendingImport.id}
                                className={BUTTON_INFO}
                              >
                                {importingPack === pendingImport.id
                                  ? <ArrowPathIcon className="w-4 h-4 animate-spin" />
                                  : <DocumentCheckIcon className="w-4 h-4" />}
                                Import Pack
                              </button>
                            </>
                          ) : (
                            <button
                              onClick={() => setSelectedPendingImport(pendingImport)}
                              className={BUTTON_WARNING}
                            >
                              <DocumentCheckIcon className="w-4 h-4" />
                              Import
                            </button>
                          )}
                          <button
                            onClick={() => handleIgnorePendingImport(pendingImport.id)}
                            className={BUTTON_SECONDARY}
                            title="Ignore this file: it stays on disk but Sportarr stops detecting or suggesting it (undo from the Blocklist tab)"
                          >
                            <NoSymbolIcon className="w-4 h-4" />
                            Ignore
                          </button>
                          <button
                            onClick={async () => {
                              try {
                                await apiClient.post(`/pending-imports/${pendingImport.id}/remove-from-client`);
                                loadQueue();
                              } catch (error) {
                                console.error('Failed to remove pending import from client:', error);
                              }
                            }}
                            className={BUTTON_DESTRUCTIVE}
                          >
                            <TrashIcon className="w-4 h-4" />
                            Remove
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}

                  {/* Queue Item Cards */}
                  {queueRows.map((item) => {
                    const isUnmonitored = item.statusMessages?.some(msg => msg.includes('no longer monitored'));
                    const canImportCard = isUnmonitored && (item.status === 5 || item.status === 3);
                    const canRetryImportCard = item.status === 4 && item.progress >= 100;
                    return (
                      <div
                        key={item.id}
                        className={`bg-gray-800 border rounded-lg p-4 hover:bg-gray-750 transition-colors ${selectedQueueIds.has(item.id) ? 'border-red-600' : 'border-gray-700'}`}
                      >
                        <div className="flex items-start justify-between">
                          <div className="flex items-start gap-3 flex-1 min-w-0">
                            <input
                              type="checkbox"
                              checked={selectedQueueIds.has(item.id)}
                              onClick={(e) => {
                                e.stopPropagation();
                                toggleSelectRow('q', item.id, e.shiftKey);
                              }}
                              onChange={() => { /* handled by onClick to read shiftKey */ }}
                              className="mt-1.5 w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2 cursor-pointer flex-shrink-0"
                            />
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center gap-3 mb-2 flex-wrap">
                                <h3 className="text-lg font-semibold text-white">
                                  {item.event?.title || 'Unknown Event'}
                                  {item.part && <span className="text-blue-400 text-sm ml-1">({item.part})</span>}
                                </h3>
                                {item.event?.organization && <span className="px-2 py-1 bg-red-900/30 text-red-400 text-xs rounded">{item.event.organization}</span>}
                                <span className={`flex items-center gap-1 ${statusColors[item.status]}`}>
                                  {getStatusIcon(item.status)}
                                  <span className="text-xs">{statusNames[item.status]}</span>
                                </span>
                                {item.quality && <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">{item.quality}</span>}
                                {cfScoreBadge(item.customFormatScore)}
                                {item.protocol && <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded uppercase">{item.protocol}</span>}
                              </div>
                              <p className="text-sm text-gray-400 truncate mb-2">{item.title}</p>
                              {item.status === 1 && (
                                <div className="flex items-center gap-2 mb-2">
                                  <div className="flex-1 bg-gray-700 rounded-full h-1.5">
                                    <div className="bg-red-600 h-1.5 rounded-full transition-all" style={{ width: `${item.progress}%` }} />
                                  </div>
                                  <span className="text-xs text-gray-400 flex-shrink-0">{item.progress.toFixed(0)}%</span>
                                </div>
                              )}
                              {item.statusMessages && item.statusMessages.length > 0 && (
                                <p className="text-sm text-orange-400 mb-2">{item.statusMessages[0]}</p>
                              )}
                              {item.errorMessage && !item.statusMessages?.length && (
                                <p className="text-sm text-red-400 mb-2">{item.errorMessage}</p>
                              )}
                              <div className="flex items-center gap-4 text-sm text-gray-500 flex-wrap">
                                <span>{formatBytes(item.downloaded)} / {formatBytes(item.size)}</span>
                                {isMeaningfulTimeRemaining(item.timeRemaining) && <><span className="text-gray-600">•</span><span>{item.timeRemaining} left</span></>}
                                {item.indexer && <><span className="text-gray-600">•</span><span>{item.indexer}</span></>}
                                <span className="text-gray-600">•</span>
                                <span>{formatDate(item.added)}</span>
                                {item.downloadClient?.name && <><span className="text-gray-600">•</span><span>{item.downloadClient.name}</span></>}
                              </div>
                            </div>
                          </div>
                          <div className="flex items-center gap-2 ml-4">
                            {canRetryImportCard && (
                              <button onClick={() => handleRetryImport(item)} className={BUTTON_WARNING}>
                                <ArrowPathIcon className="w-4 h-4" />
                                Retry Import
                              </button>
                            )}
                            {canImportCard && (
                              <>
                                <button onClick={() => handleForceImport(item)} className={BUTTON_SUCCESS}>
                                  <DocumentCheckIcon className="w-4 h-4" />
                                  Import
                                </button>
                                <button onClick={() => handleDeleteUnmonitored(item)} className={BUTTON_DESTRUCTIVE}>
                                  <TrashIcon className="w-4 h-4" />
                                  Delete
                                </button>
                              </>
                            )}
                            {!canImportCard && (
                              <button onClick={() => handleOpenRemoveQueueDialog(item)} className={BUTTON_DESTRUCTIVE}>
                                <TrashIcon className="w-4 h-4" />
                                Remove
                              </button>
                            )}
                          </div>
                        </div>
                      </div>
                    );
                  })}
                  </div>
                </div>
              )}
              {totalHidden > 0 && (
                <div className="px-4 py-2 text-xs text-gray-400 text-center bg-gray-900/40 border-t border-gray-800">
                  Showing {totalVisible} of {totalAvailable} items. Increase the Page Size in View Options to show more.
                </div>
              )}
</>
            )}
          </div>
        ) : activeTab === 'history' ? (
          // History Tab
          <div className="rounded-lg overflow-hidden">
            {historyItems.length === 0 ? (
              <div className="p-12 text-center text-gray-400">
                <DocumentCheckIcon className="w-16 h-16 mx-auto mb-4 opacity-50" />
                <p className="text-lg">No import history</p>
                <p className="text-sm mt-2">Imported events will appear here once downloads complete</p>
              </div>
            ) : (
              <>
                {compactView ? (
                  <div className="overflow-x-auto">
                    <table className="w-full">
                      <thead>
                        <tr className="bg-gray-800 text-gray-300 text-xs">
                          <th className="px-3 py-1.5 text-left font-medium">Event</th>
                          <th className="px-3 py-1.5 text-left font-medium">Imported Path</th>
                          <th className="px-2 py-1.5 text-center font-medium">Quality</th>
                          <th className="px-2 py-1.5 text-center font-medium">Decision</th>
                          <th className="px-2 py-1.5 text-center font-medium">Size</th>
                          <th className="px-2 py-1.5 text-center font-medium">Imported</th>
                          <th className="px-2 py-1.5 text-right font-medium">Actions</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-700">
                        {historyItems.map((item) => (
                          <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                            <td className="px-3 py-1.5 min-w-[150px]">
                              <div className={`text-xs font-medium break-words ${item.event ? 'text-white' : 'text-gray-500 italic'}`}>
                                {item.event?.title || 'Unknown Event'}
                                {item.part && <span className="text-blue-400 ml-1">({item.part})</span>}
                              </div>
                              <div className="text-xs text-gray-400 break-words">
                                {item.event?.organization || (item.eventId ? `Event ID: ${item.eventId}` : 'N/A')}
                              </div>
                            </td>
                            <td className="px-3 py-1.5 min-w-[250px]">
                              <div className="text-gray-300 text-xs break-words">{item.destinationPath}</div>
                              {item.warnings.length > 0 && <div className="text-xs text-yellow-400 mt-0.5">{item.warnings.length} warning(s)</div>}
                              {item.errors.length > 0 && <div className="text-xs text-red-400 mt-0.5">{item.errors.length} error(s)</div>}
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <div className="flex items-center justify-center gap-1 flex-wrap">
                                <span className={BADGE_PURPLE}>{item.quality}</span>
                                {cfScoreBadge(item.downloadQueueItem?.customFormatScore)}
                              </div>
                            </td>
                            <td className="px-3 py-1.5">
                              <div className={`flex items-center justify-center gap-1 ${decisionColors[item.decision]}`}>
                                {getDecisionIcon(item.decision)}
                                <span className="text-xs">{decisionNames[item.decision]}</span>
                              </div>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <span className="text-gray-300 text-xs">{formatBytes(item.size)}</span>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <span className="text-gray-400 text-xs">{formatDate(item.importedAt)}</span>
                            </td>
                            <td className="px-2 py-1.5">
                              <div className="flex items-center justify-end">
                                <button
                                  onClick={() => handleOpenRemoveHistoryDialog(item)}
                                  className={BUTTON_ICON_DESTRUCTIVE}
                                  title="Delete"
                                >
                                  <TrashIcon className="w-4 h-4" />
                                </button>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  /* Spacious: card list */
                  <div className="space-y-3">
                    {historyItems.map((item) => (
                      <div key={item.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4 hover:bg-gray-750 transition-colors">
                        <div className="flex items-start justify-between">
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-3 mb-2 flex-wrap">
                              <h3 className={`text-lg font-semibold ${item.event ? 'text-white' : 'text-gray-500 italic'}`}>
                                {item.event?.title || 'Unknown Event'}
                                {item.part && <span className="text-blue-400 text-sm ml-1">({item.part})</span>}
                              </h3>
                              {(item.event?.organization || item.eventId) && (
                                <span className="px-2 py-1 bg-red-900/30 text-red-400 text-xs rounded">
                                  {item.event?.organization || `Event ID: ${item.eventId}`}
                                </span>
                              )}
                              <span className={`flex items-center gap-1 ${decisionColors[item.decision]}`}>
                                {getDecisionIcon(item.decision)}
                                <span className="text-xs">{decisionNames[item.decision]}</span>
                              </span>
                              <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">{item.quality}</span>
                              {cfScoreBadge(item.downloadQueueItem?.customFormatScore)}
                              {item.warnings.length > 0 && <span className="px-2 py-1 bg-yellow-900/30 text-yellow-400 text-xs rounded">{item.warnings.length} warning{item.warnings.length !== 1 ? 's' : ''}</span>}
                              {item.errors.length > 0 && <span className="px-2 py-1 bg-red-900/30 text-red-400 text-xs rounded">{item.errors.length} error{item.errors.length !== 1 ? 's' : ''}</span>}
                            </div>
                            <p className="text-sm text-gray-400 font-mono truncate mb-1">{item.destinationPath}</p>
                            <div className="flex items-center gap-4 text-sm text-gray-500">
                              <span>{formatBytes(item.size)}</span>
                              <span className="text-gray-600">•</span>
                              <span>{formatDate(item.importedAt)}</span>
                            </div>
                          </div>
                          <div className="flex items-center gap-2 ml-4">
                            <button
                              onClick={() => handleOpenRemoveHistoryDialog(item)}
                              className={BUTTON_DESTRUCTIVE}
                            >
                              <TrashIcon className="w-4 h-4" />
                              Delete
                            </button>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                {/* Pagination */}
                {totalPages > 1 && (
                  <div className="px-6 py-4 border-t border-gray-700 flex items-center justify-between">
                    <button
                      onClick={() => setPage(Math.max(1, page - 1))}
                      disabled={page === 1}
                      className={BUTTON_SECONDARY}
                    >
                      Previous
                    </button>
                    <span className="text-gray-400">
                      Page {page} of {totalPages}
                    </span>
                    <button
                      onClick={() => setPage(Math.min(totalPages, page + 1))}
                      disabled={page === totalPages}
                      className={BUTTON_SECONDARY}
                    >
                      Next
                    </button>
                  </div>
                )}
              </>
            )}
          </div>
        ) : activeTab === 'missing' ? (
          // Wanted: monitored events with no file yet (embedded page loads its own data)
          <div className="pt-2">
            <WantedPage key="wanted-missing" embedded fixedTab="missing" />
          </div>
        ) : activeTab === 'cutoffUnmet' ? (
          // Wanted: events whose file is below the quality cutoff
          <div className="pt-2">
            <WantedPage key="wanted-cutoff" embedded fixedTab="cutoff-unmet" />
          </div>
        ) : activeTab === 'blocklist' ? (
          // Blocklist Tab
          <div className="rounded-lg overflow-hidden">
            {blocklistItems.length === 0 ? (
              <div className="p-12 text-center text-gray-400">
                <NoSymbolIcon className="w-16 h-16 mx-auto mb-4 opacity-50" />
                <p className="text-lg">No blocked releases</p>
                <p className="text-sm mt-2">Failed or rejected releases will appear here</p>
              </div>
            ) : (
              <>
                {/* Bulk actions: select all on the current page + remove selected */}
                <div className="flex items-center justify-between px-1 pb-3">
                  <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={blocklistItems.length > 0 && selectedBlocklistIds.size === blocklistItems.length}
                      onChange={toggleSelectAllBlocklist}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600"
                    />
                    Select all on page
                  </label>
                  {selectedBlocklistIds.size > 0 && (
                    <button
                      onClick={() => setBulkRemoveBlocklistOpen(true)}
                      className={BUTTON_DESTRUCTIVE}
                    >
                      <TrashIcon className="w-4 h-4" />
                      Remove Selected ({selectedBlocklistIds.size})
                    </button>
                  )}
                </div>
                {compactView ? (
                  <div className="overflow-x-auto">
                    <table className="w-full">
                      <thead>
                        <tr className="bg-gray-800 text-gray-300 text-xs">
                          <th className="px-2 py-1.5 w-8"></th>
                          <th className="px-3 py-1.5 text-left font-medium">Event</th>
                          <th className="px-3 py-1.5 text-left font-medium">Reason</th>
                          <th className="px-2 py-1.5 text-center font-medium">Indexer</th>
                          <th className="px-2 py-1.5 text-center font-medium">Blocked</th>
                          <th className="px-2 py-1.5 text-right font-medium">Actions</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-700">
                        {blocklistItems.map((item) => (
                          <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                            <td className="px-2 py-1.5 text-center">
                              <input
                                type="checkbox"
                                checked={selectedBlocklistIds.has(item.id)}
                                onChange={() => toggleBlocklistSelection(item.id)}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600"
                              />
                            </td>
                            <td className="px-3 py-1.5 min-w-[150px]">
                              <div className="text-white text-xs font-medium break-words">
                                {item.event?.title || 'Unknown Event'}
                                {item.part && <span className="text-blue-400 ml-1">({item.part})</span>}
                              </div>
                              <div className="text-xs text-gray-400 break-words">{item.event?.organization}</div>
                            </td>
                            <td className="px-3 py-1.5 min-w-[200px]">
                              <div className="text-gray-300 text-xs break-words">{item.title}</div>
                              {item.message && <div className="text-xs text-gray-400 mt-0.5 break-words">{item.message}</div>}
                              {item.torrentInfoHash && (
                                <div className="text-xs text-gray-500 mt-0.5 font-mono">Hash: {item.torrentInfoHash.substring(0, 16)}...</div>
                              )}
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <span className="text-gray-400 text-xs">{item.indexer || 'Unknown'}</span>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <span className="text-gray-400 text-xs">{formatDate(item.blockedAt)}</span>
                            </td>
                            <td className="px-2 py-1.5">
                              <div className="flex items-center justify-end">
                                <button
                                  onClick={() => handleOpenRemoveBlocklistDialog(item)}
                                  className={BUTTON_ICON_DESTRUCTIVE}
                                  title="Remove from Blocklist"
                                >
                                  <TrashIcon className="w-4 h-4" />
                                </button>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  /* Spacious: card list */
                  <div className="space-y-3">
                    {blocklistItems.map((item) => (
                      <div key={item.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4 hover:bg-gray-750 transition-colors">
                        <div className="flex items-start justify-between">
                          <input
                            type="checkbox"
                            checked={selectedBlocklistIds.has(item.id)}
                            onChange={() => toggleBlocklistSelection(item.id)}
                            className="w-5 h-5 mt-1 mr-3 rounded border-gray-600 bg-gray-700 text-red-600 flex-shrink-0"
                          />
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-3 mb-2 flex-wrap">
                              <h3 className="text-lg font-semibold text-white">
                                {item.event?.title || 'Unknown Event'}
                                {item.part && <span className="text-blue-400 text-sm ml-1">({item.part})</span>}
                              </h3>
                              {item.event?.organization && (
                                <span className="px-2 py-1 bg-red-900/30 text-red-400 text-xs rounded">{item.event.organization}</span>
                              )}
                              <span className={`flex items-center gap-1 ${blocklistReasonColors[item.reason]}`}>
                                <NoSymbolIcon className="w-4 h-4" />
                                <span className="text-xs">{blocklistReasonNames[item.reason]}</span>
                              </span>
                            </div>
                            <p className="text-sm text-gray-400 truncate mb-1">{item.title}</p>
                            {item.message && <p className="text-sm text-gray-500 mb-1">{item.message}</p>}
                            <div className="flex items-center gap-4 text-sm text-gray-500 flex-wrap">
                              <span>{item.indexer || 'Unknown indexer'}</span>
                              <span className="text-gray-600">•</span>
                              <span>{formatDate(item.blockedAt)}</span>
                              {item.torrentInfoHash && <><span className="text-gray-600">•</span><span className="font-mono">{item.torrentInfoHash.substring(0, 12)}…</span></>}
                            </div>
                          </div>
                          <div className="flex items-center gap-2 ml-4">
                            <button
                              onClick={() => handleOpenRemoveBlocklistDialog(item)}
                              className={BUTTON_DESTRUCTIVE}
                            >
                              <TrashIcon className="w-4 h-4" />
                              Remove
                            </button>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                {/* Pagination */}
                {totalPages > 1 && (
                  <div className="px-6 py-4 border-t border-gray-700 flex items-center justify-between">
                    <button
                      onClick={() => setPage(Math.max(1, page - 1))}
                      disabled={page === 1}
                      className={BUTTON_SECONDARY}
                    >
                      Previous
                    </button>
                    <span className="text-gray-400">
                      Page {page} of {totalPages}
                    </span>
                    <button
                      onClick={() => setPage(Math.min(totalPages, page + 1))}
                      disabled={page === totalPages}
                      className={BUTTON_SECONDARY}
                    >
                      Next
                    </button>
                  </div>
                )}
              </>
            )}
          </div>
        ) : (
          // Grab History Tab
          <div className="rounded-lg overflow-hidden">
            {/* Filter Bar */}
            <div className="px-4 py-3 bg-gray-800 border-b border-gray-700 flex items-center justify-between">
              <div className="flex items-center gap-4">
                <label className="flex items-center gap-2 text-gray-300 text-sm cursor-pointer">
                  <input
                    type="checkbox"
                    checked={grabHistoryMissingOnly}
                    onChange={(e) => setGrabHistoryMissingOnly(e.target.checked)}
                    className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2"
                  />
                  Show Missing Files Only
                </label>
              </div>
              <button
                onClick={handleBulkRegrab}
                disabled={bulkRegrabbing || grabHistoryItems.filter(i => !i.fileExists && (i.hasDownloadUrl || i.hasTorrentHash)).length === 0}
                className={BUTTON_SUCCESS}
              >
                {bulkRegrabbing ? (
                  <>
                    <ArrowPathIcon className="w-4 h-4 animate-spin" />
                    Re-grabbing...
                  </>
                ) : (
                  <>
                    <ArrowDownTrayIcon className="w-4 h-4" />
                    Re-grab All Missing
                  </>
                )}
              </button>
            </div>

            {grabHistoryItems.length === 0 ? (
              <div className="p-12 text-center text-gray-400">
                <ArrowDownTrayIcon className="w-16 h-16 mx-auto mb-4 opacity-50" />
                <p className="text-lg">No grab history</p>
                <p className="text-sm mt-2">
                  {grabHistoryMissingOnly
                    ? 'No missing files found in grab history'
                    : 'Grabbed releases will appear here once downloads are sent to clients'}
                </p>
              </div>
            ) : (
              <>
                {compactView ? (
                  <div className="overflow-x-auto">
                    <table className="w-full">
                      <thead>
                        <tr className="bg-gray-800 text-gray-300 text-xs">
                          <th className="px-3 py-1.5 text-left font-medium">Event</th>
                          <th className="px-3 py-1.5 text-left font-medium">Release</th>
                          <th className="px-2 py-1.5 text-center font-medium">Quality</th>
                          <th className="px-2 py-1.5 text-center font-medium">Indexer</th>
                          <th className="px-2 py-1.5 text-center font-medium">Protocol</th>
                          <th className="px-2 py-1.5 text-center font-medium">Size</th>
                          <th className="px-2 py-1.5 text-center font-medium">Status</th>
                          <th className="px-2 py-1.5 text-center font-medium">Grabbed</th>
                          <th className="px-2 py-1.5 text-right font-medium">Actions</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-700">
                        {grabHistoryItems.map((item) => (
                          <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                            <td className="px-3 py-1.5 min-w-[150px]">
                              <div className="text-white text-xs font-medium break-words">
                                {item.eventTitle || 'Unknown Event'}
                                {item.partName && <span className="text-blue-400 ml-1">({item.partName})</span>}
                              </div>
                              <div className="text-xs text-gray-400 break-words">{item.leagueName}</div>
                            </td>
                            <td className="px-3 py-1.5 min-w-[200px]">
                              <div className="text-gray-300 text-xs break-words">{item.title}</div>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <div className="flex items-center justify-center gap-1 flex-wrap">
                                <span className="px-1.5 py-0.5 bg-purple-900/30 text-purple-400 text-xs rounded">{item.quality || 'Unknown'}</span>
                                {cfScoreBadge(item.customFormatScore)}
                              </div>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <span className="text-gray-400 text-xs">{item.kind === 'import' ? 'Manual/DVR' : item.indexer}</span>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              {item.protocol ? (
                                <span className="px-1.5 py-0.5 bg-blue-900/30 text-blue-400 text-xs rounded uppercase">{item.protocol}</span>
                              ) : (
                                <span className="px-1.5 py-0.5 bg-gray-700 text-gray-300 text-xs rounded uppercase">Import</span>
                              )}
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <span className="text-gray-300 text-xs">{formatBytes(item.size)}</span>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <div className="flex flex-col items-center gap-1">
                                {item.fileExists ? (
                                  <span className="flex items-center gap-1 text-green-400 text-xs"><CheckCircleIcon className="w-4 h-4" />File Exists</span>
                                ) : item.wasImported ? (
                                  <span className="flex items-center gap-1 text-orange-400 text-xs"><ExclamationTriangleIcon className="w-4 h-4" />Missing</span>
                                ) : (
                                  <span className="flex items-center gap-1 text-gray-400 text-xs"><ClockIcon className="w-4 h-4" />Not Imported</span>
                                )}
                                {item.regrabCount > 0 && <span className="text-xs text-gray-500">Re-grabbed {item.regrabCount}x</span>}
                              </div>
                            </td>
                            <td className="px-2 py-1.5 text-center">
                              <span className="text-gray-400 text-xs">{formatDate(item.grabbedAt)}</span>
                            </td>
                            <td className="px-2 py-1.5">
                              <div className="flex items-center justify-end gap-1">
                                {item.kind !== 'import' && (
                                  <button
                                    onClick={() => handleRegrab(item.id)}
                                    disabled={regrabbing === item.id || (!item.hasDownloadUrl && !item.hasTorrentHash)}
                                    className={BUTTON_ICON_SUCCESS}
                                    title={!item.hasDownloadUrl && !item.hasTorrentHash ? 'No download URL or torrent hash available' : 'Re-grab this release'}
                                  >
                                    {regrabbing === item.id ? <ArrowPathIcon className="w-4 h-4 animate-spin" /> : <ArrowDownTrayIcon className="w-4 h-4" />}
                                  </button>
                                )}
                                {item.fileExists && item.eventId != null && (
                                  <button
                                    onClick={() => handleDeleteFile(item)}
                                    className={BUTTON_ICON_DESTRUCTIVE}
                                    title="Delete file from disk (entry stays for re-grabbing)"
                                  >
                                    <TrashIcon className="w-4 h-4" />
                                  </button>
                                )}
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  /* Spacious: card list */
                  <div className="space-y-3 pt-4">
                    {grabHistoryItems.map((item) => (
                      <div key={item.id} className={`bg-gray-800 border rounded-lg p-4 hover:bg-gray-750 transition-colors ${!item.fileExists && item.wasImported ? 'border-orange-900/50' : 'border-gray-700'}`}>
                        <div className="flex items-start justify-between">
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-3 mb-2 flex-wrap">
                              <h3 className="text-lg font-semibold text-white">
                                {item.eventTitle || 'Unknown Event'}
                                {item.partName && <span className="text-blue-400 text-sm ml-1">({item.partName})</span>}
                              </h3>
                              {item.leagueName && <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded">{item.leagueName}</span>}
                              {item.fileExists ? (
                                <span className="flex items-center gap-1 text-green-400 text-xs"><CheckCircleIcon className="w-3.5 h-3.5" />File Exists</span>
                              ) : item.wasImported ? (
                                <span className="flex items-center gap-1 text-orange-400 text-xs"><ExclamationTriangleIcon className="w-3.5 h-3.5" />Missing</span>
                              ) : (
                                <span className="flex items-center gap-1 text-gray-400 text-xs"><ClockIcon className="w-3.5 h-3.5" />Not Imported</span>
                              )}
                              {item.quality && <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">{item.quality}</span>}
                              {cfScoreBadge(item.customFormatScore)}
                              {item.protocol ? (
                                <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded uppercase">{item.protocol}</span>
                              ) : (
                                <span className="px-2 py-1 bg-gray-700 text-gray-300 text-xs rounded uppercase">Import</span>
                              )}
                            </div>
                            <p className="text-sm text-gray-400 truncate mb-1">
                              {item.kind === 'import' ? item.title.split(/[\\/]/).pop() : item.title}
                            </p>
                            <div className="flex items-center gap-4 text-sm text-gray-500 flex-wrap">
                              <span>{item.kind === 'import' ? 'Manual/DVR import' : item.indexer}</span>
                              <span className="text-gray-600">•</span>
                              <span>{formatBytes(item.size)}</span>
                              <span className="text-gray-600">•</span>
                              <span>{formatDate(item.grabbedAt)}</span>
                              {item.regrabCount > 0 && <><span className="text-gray-600">•</span><span>Re-grabbed {item.regrabCount}x</span></>}
                            </div>
                          </div>
                          <div className="flex items-center gap-2 ml-4">
                            {item.kind !== 'import' && (
                              <button
                                onClick={() => handleRegrab(item.id)}
                                disabled={regrabbing === item.id || (!item.hasDownloadUrl && !item.hasTorrentHash)}
                                className={BUTTON_SUCCESS}
                                title={!item.hasDownloadUrl && !item.hasTorrentHash ? 'No download URL or torrent hash available' : 'Re-grab this release'}
                              >
                                {regrabbing === item.id ? <ArrowPathIcon className="w-4 h-4 animate-spin" /> : <ArrowDownTrayIcon className="w-4 h-4" />}
                                Re-grab
                              </button>
                            )}
                            {item.fileExists && item.eventId != null && (
                              <button
                                onClick={() => handleDeleteFile(item)}
                                className={BUTTON_ICON_DESTRUCTIVE}
                                title="Delete file from disk (entry stays for re-grabbing)"
                              >
                                <TrashIcon className="w-4 h-4" />
                              </button>
                            )}
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                {/* Pagination */}
                {totalPages > 1 && (
                  <div className="px-6 py-4 border-t border-gray-700 flex items-center justify-between">
                    <button
                      onClick={() => setPage(Math.max(1, page - 1))}
                      disabled={page === 1}
                      className={BUTTON_SECONDARY}
                    >
                      Previous
                    </button>
                    <span className="text-gray-400">
                      Page {page} of {totalPages}
                    </span>
                    <button
                      onClick={() => setPage(Math.min(totalPages, page + 1))}
                      disabled={page === totalPages}
                      className={BUTTON_SECONDARY}
                    >
                      Next
                    </button>
                  </div>
                )}
              </>
            )}
          </div>
        )}

        {/* Remove from Queue Dialog (Sonarr-style) - Supports single and bulk removal */}
        {removeQueueDialog && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-2xl w-full p-6">
              <div className="flex items-start justify-between mb-6">
                <h3 className="text-xl font-bold text-white">
                  {removeQueueDialog.items.length === 1
                    ? `Remove - ${removeQueueDialog.items[0].title.length > 60 ? removeQueueDialog.items[0].title.substring(0, 60) + '...' : removeQueueDialog.items[0].title}`
                    : `Remove ${removeQueueDialog.items.length} Selected Downloads`
                  }
                </h3>
                <button
                  onClick={() => setRemoveQueueDialog(null)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              {removeQueueDialog.items.length === 1 ? (
                <p className="text-gray-300 mb-6">
                  Are you sure you want to remove '{removeQueueDialog.items[0].title}' from the queue?
                </p>
              ) : (
                <div className="mb-6">
                  <p className="text-gray-300 mb-3">
                    Are you sure you want to remove the following {removeQueueDialog.items.length} downloads from the queue?
                  </p>
                  <div className="max-h-40 overflow-y-auto bg-gray-800/50 rounded-lg p-3 space-y-1">
                    {removeQueueDialog.items.map(item => (
                      <div key={item.id} className="text-sm text-gray-400 truncate" title={item.title}>
                        {item.title}
                      </div>
                    ))}
                  </div>
                </div>
              )}

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
                  {removalMethod === 'ignoreDownload' && 'Stops Sportarr from processing this download further'}
                </p>
              </div>

              {/* Blocklist Release */}
              <div className="mb-6">
                <label className="block text-gray-300 font-medium mb-2">Blocklist Release{removeQueueDialog.items.length > 1 ? 's' : ''}</label>
                <select
                  value={blocklistAction}
                  onChange={(e) => setBlocklistAction(e.target.value as BlocklistAction)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
                >
                  <option value="none">Do not Blocklist</option>
                  <option value="blocklistAndSearch">Blocklist and Search for Replacement{removeQueueDialog.items.length > 1 ? 's' : ''}</option>
                  <option value="blocklistOnly">Blocklist Only</option>
                </select>
                <p className="text-sm text-gray-400 mt-2">
                  {blocklistAction === 'none' && `The release${removeQueueDialog.items.length > 1 ? 's' : ''} will remain eligible for future RSS and Automatic searches`}
                  {blocklistAction === 'blocklistAndSearch' && `Blocklist release${removeQueueDialog.items.length > 1 ? 's' : ''} and search for replacement${removeQueueDialog.items.length > 1 ? 's' : ''}`}
                  {blocklistAction === 'blocklistOnly' && `Blocklist release${removeQueueDialog.items.length > 1 ? 's' : ''} without searching for replacement${removeQueueDialog.items.length > 1 ? 's' : ''}`}
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
                  Remove{removeQueueDialog.items.length > 1 ? ` ${removeQueueDialog.items.length} Downloads` : ''}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Remove History Item Dialog (Sonarr-style) */}
        {removeHistoryDialog && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-2xl w-full p-6">
              <div className="flex items-start justify-between mb-6">
                <h3 className="text-xl font-bold text-white">
                  Remove - {removeHistoryDialog.title.length > 60 ? removeHistoryDialog.title.substring(0, 60) + '...' : removeHistoryDialog.title}
                </h3>
                <button
                  onClick={() => setRemoveHistoryDialog(null)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <p className="text-gray-300 mb-6">
                Are you sure you want to remove '{removeHistoryDialog.title}' from history?
              </p>

              {/* Blocklist Release */}
              <div className="mb-6">
                <label className="block text-gray-300 font-medium mb-2">Blocklist Release</label>
                <select
                  value={historyBlocklistAction}
                  onChange={(e) => setHistoryBlocklistAction(e.target.value as BlocklistAction)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
                >
                  <option value="none">Do not Blocklist</option>
                  <option value="blocklistAndSearch">Blocklist and Search</option>
                  <option value="blocklistOnly">Blocklist Only</option>
                </select>
                <p className="text-sm text-gray-400 mt-2">
                  {historyBlocklistAction === 'none' && 'The release will remain eligible for future RSS and Automatic searches'}
                  {historyBlocklistAction === 'blocklistAndSearch' && 'Blocklist release and search for a replacement'}
                  {historyBlocklistAction === 'blocklistOnly' && 'Blocklist release without searching for a replacement'}
                </p>
              </div>

              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setRemoveHistoryDialog(null)}
                  className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                >
                  Close
                </button>
                <button
                  onClick={handleDeleteHistory}
                  className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Remove
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Remove Blocklist Item Dialog */}
        {removeBlocklistDialog && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-2xl w-full p-6">
              <div className="flex items-start justify-between mb-6">
                <h3 className="text-xl font-bold text-white">
                  Remove from Blocklist
                </h3>
                <button
                  onClick={() => setRemoveBlocklistDialog(null)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <p className="text-gray-300 mb-6">
                Are you sure you want to remove '{removeBlocklistDialog.title.length > 60 ? removeBlocklistDialog.title.substring(0, 60) + '...' : removeBlocklistDialog.title}' from the blocklist?
              </p>

              <p className="text-sm text-yellow-500 mb-6">
                This release will be allowed in future RSS and Automatic searches.
              </p>

              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setRemoveBlocklistDialog(null)}
                  className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                >
                  Close
                </button>
                <button
                  onClick={handleDeleteBlocklist}
                  className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Remove
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Bulk Remove Blocklist Confirmation */}
        {bulkRemoveBlocklistOpen && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-2xl w-full p-6">
              <div className="flex items-start justify-between mb-6">
                <h3 className="text-xl font-bold text-white">
                  Remove from Blocklist
                </h3>
                <button
                  onClick={() => setBulkRemoveBlocklistOpen(false)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <p className="text-gray-300 mb-6">
                Are you sure you want to remove {selectedBlocklistIds.size} release{selectedBlocklistIds.size === 1 ? '' : 's'} from the blocklist?
              </p>

              <p className="text-sm text-yellow-500 mb-6">
                These releases will be allowed in future RSS and Automatic searches.
              </p>

              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setBulkRemoveBlocklistOpen(false)}
                  className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                >
                  Close
                </button>
                <button
                  onClick={handleBulkDeleteBlocklist}
                  className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Remove {selectedBlocklistIds.size}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* View Options Modal */}
        {showTableOptions && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-lg w-full max-h-[85dvh] overflow-y-auto">
              <div className="sticky top-0 bg-gradient-to-br from-gray-900 to-black border-b border-gray-700 px-6 py-4 flex items-center justify-between">
                <h3 className="text-xl font-bold text-white">View Options</h3>
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
                        Show items without a event in the queue, this could include removed events or anything else in Sportarr's category
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
                          disabled={column === 'actions'}
                          className="w-4 h-4 bg-gray-700 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-2"
                          title={column === 'actions' ? 'Actions column is always shown' : undefined}
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

        {/* Manual Import Modal */}
        {selectedPendingImport && (
          <ManualImportModal
            pendingImport={selectedPendingImport}
            onClose={() => setSelectedPendingImport(null)}
            onSuccess={() => {
              setSelectedPendingImport(null);
              loadQueue(); // Refresh queue to remove imported item
            }}
          />
        )}

        {/* Pack Preview Modal */}
        {packPreviewImport && (
          <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-purple-700 rounded-lg max-w-3xl w-full max-h-[80vh] overflow-hidden flex flex-col">
              <div className="px-6 py-4 border-b border-gray-700 flex items-center justify-between">
                <div>
                  <h3 className="text-xl font-bold text-white flex items-center gap-2">
                    <span className="px-2 py-0.5 bg-purple-600 text-white text-xs rounded-full">PACK</span>
                    Pack Preview
                  </h3>
                  <p className="text-sm text-gray-400 mt-1">{packPreviewImport.title}</p>
                </div>
                <button
                  onClick={() => setPackPreviewImport(null)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <div className="flex-1 overflow-y-auto p-6">
                {loadingPackPreview ? (
                  <div className="text-center py-12">
                    <div className="inline-block animate-spin rounded-full h-8 w-8 border-4 border-purple-600 border-t-transparent"></div>
                    <p className="mt-4 text-gray-400">Scanning pack for matching events...</p>
                  </div>
                ) : packMatches.length === 0 ? (
                  <div className="text-center py-12 text-gray-400">
                    <ExclamationTriangleIcon className="w-12 h-12 mx-auto mb-4 opacity-50" />
                    <p>No matching monitored events found in this pack</p>
                    <p className="text-sm mt-2">Make sure you have events monitored that match the files in this pack</p>
                  </div>
                ) : (
                  <div className="space-y-2">
                    <p className="text-gray-300 mb-4">
                      Found <span className="text-purple-400 font-bold">{packMatches.length}</span> files matching monitored events:
                    </p>
                    <table className="w-full">
                      <thead>
                        <tr className="text-gray-400 text-xs border-b border-gray-700">
                          <th className="text-left py-2 px-2">File</th>
                          <th className="text-left py-2 px-2">Matched Event</th>
                          <th className="text-center py-2 px-2">Confidence</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-800">
                        {packMatches.map((match, idx) => (
                          <tr key={idx} className="hover:bg-gray-800/50">
                            <td className="py-2 px-2 text-sm text-gray-300 break-all">{match.fileName}</td>
                            <td className="py-2 px-2 text-sm text-white">{match.eventTitle}</td>
                            <td className="py-2 px-2 text-center">
                              <span className={`px-2 py-0.5 rounded text-xs ${
                                match.matchConfidence >= 80 ? 'bg-green-900/50 text-green-400' :
                                match.matchConfidence >= 50 ? 'bg-yellow-900/50 text-yellow-400' :
                                'bg-red-900/50 text-red-400'
                              }`}>
                                {match.matchConfidence}%
                              </span>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>

              <div className="px-6 py-4 border-t border-gray-700 flex justify-between items-center">
                <p className="text-sm text-gray-400">
                  Unmatched files will be deleted after import
                </p>
                <div className="flex gap-3">
                  <button
                    onClick={() => setPackPreviewImport(null)}
                    className={BUTTON_SECONDARY}
                  >
                    Close
                  </button>
                  <button
                    onClick={() => handleImportPack(packPreviewImport)}
                    disabled={importingPack === packPreviewImport.id || packMatches.length === 0}
                    className={BUTTON_SUCCESS}
                  >
                    {importingPack === packPreviewImport.id ? (
                      <>
                        <ArrowPathIcon className="w-5 h-5 animate-spin" />
                        Importing...
                      </>
                    ) : (
                      <>
                        <DocumentCheckIcon className="w-5 h-5" />
                        Import {packMatches.length} Files
                      </>
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
    </PageShell>
  );
}
