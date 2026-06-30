import { useEffect, useState, useRef, memo } from 'react';
import { useTasks } from '../api/hooks';
import { useQuery } from '@tanstack/react-query';
import apiClient from '../api/client';
import { getRefetchIntervalWithBackoff } from '../utils/queryBackoff';
import {
  CheckCircleIcon,
  XCircleIcon,
  ArrowPathIcon,
  MagnifyingGlassIcon,
  QueueListIcon,
  ArrowDownTrayIcon,
  DocumentCheckIcon,
} from '@heroicons/react/24/outline';

interface Task {
  id: number;
  name: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled' | 'Aborting';
  progress: number | null;
  message: string | null;
  started: string | null;
  ended: string | null;
}

interface ActiveSearchStatus {
  searchQuery: string;
  eventTitle: string | null;
  part: string | null;
  totalIndexers: number;
  activeIndexers: number;
  completedIndexers: number;
  releasesFound: number;
  startedAt: string;
  isComplete: boolean;
}

interface SearchQueueStatus {
  pendingCount: number;
  activeCount: number;
  maxConcurrent: number;
  pendingSearches: SearchQueueItem[];
  activeSearches: SearchQueueItem[];
  recentlyCompleted: SearchQueueItem[];
}

interface SearchQueueItem {
  id: string;
  eventId: number;
  eventTitle: string;
  part: string | null;
  status: 'Queued' | 'Searching' | 'Completed' | 'NoResults' | 'Failed' | 'Cancelled';
  message: string;
  queuedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  releasesFound: number;
  success: boolean;
  selectedRelease: string | null;
  quality: string | null;
}

// Download queue item (from /api/queue)
interface DownloadQueueItem {
  id: number;
  eventId: number;
  event?: { id: number; title: string };
  title: string;
  status: number; // 0=Queued, 1=Downloading, 2=Paused, 3=Completed, 4=Failed, 5=Warning, 6=Importing, 7=Imported
  quality?: string;
  progress: number;
  added: string;
  importedAt?: string;
}

// Hook to fetch active search status (polls frequently for real-time updates)
// Uses notifyOnChangeProps to minimize re-renders - only re-render when data actually changes
const useActiveSearchStatus = () => {
  return useQuery({
    queryKey: ['activeSearchStatus'],
    queryFn: async () => {
      const { data } = await apiClient.get<ActiveSearchStatus | null>('/search/active');
      return data;
    },
    refetchInterval: (query) => getRefetchIntervalWithBackoff(3000, query.state.fetchFailureCount), // Poll every 3 seconds
    notifyOnChangeProps: ['data'], // Only re-render when data changes, not on every refetch
  });
};

// Hook to fetch search queue status
const useSearchQueueStatus = () => {
  return useQuery({
    queryKey: ['searchQueueStatus'],
    queryFn: async () => {
      const { data } = await apiClient.get<SearchQueueStatus>('/search/queue');
      return data;
    },
    refetchInterval: (query) => getRefetchIntervalWithBackoff(3000, query.state.fetchFailureCount), // Poll every 3 seconds
    notifyOnChangeProps: ['data'], // Only re-render when data changes
  });
};

// Use shared download queue hook - no need for separate cache since we're not showing progress %
// This reduces API calls and re-renders, fixing navigation blocking during downloads
const useFooterDownloadQueue = () => {
  return useQuery({
    queryKey: ['downloadQueue'],
    queryFn: async () => {
      const { data } = await apiClient.get<DownloadQueueItem[]>('/queue');
      return data;
    },
    refetchInterval: (query) => getRefetchIntervalWithBackoff(5000, query.state.fetchFailureCount), // Poll every 5 seconds - only need to detect status changes, not progress
    notifyOnChangeProps: ['data'], // Only re-render when data changes
    // Use same structuralSharing as LeagueDetailPage to ignore progress-only changes
    structuralSharing: (oldData, newData) => {
      if (!oldData || !newData) return newData;
      if (!Array.isArray(oldData) || !Array.isArray(newData)) return newData;

      // Check if only progress changed (no status changes, no new items, no removed items)
      if (oldData.length !== newData.length) return newData;

      let onlyProgressChanged = true;
      for (let i = 0; i < oldData.length; i++) {
        const oldItem = oldData[i];
        const newItem = newData.find((n: DownloadQueueItem) => n.id === oldItem.id);
        if (!newItem) {
          onlyProgressChanged = false;
          break;
        }
        // If anything other than progress changed, use the new data
        if (oldItem.status !== newItem.status ||
            oldItem.eventId !== newItem.eventId ||
            oldItem.title !== newItem.title) {
          onlyProgressChanged = false;
          break;
        }
      }

      // If only progress changed, return old reference to prevent re-render
      if (onlyProgressChanged) {
        return oldData;
      }

      return newData;
    },
  });
};

// Status notification for downloads
interface DownloadNotification {
  id: number;
  title: string;
  quality?: string;
  status: 'grabbed' | 'downloading' | 'importing' | 'imported';
  timestamp: number;
}

/**
 * Sonarr-style fixed footer status bar
 * Shows all status information (search progress, tasks, queue) at bottom-left of screen
 */
function FooterStatusBar() {
  // Wrap hooks in try-catch error boundary pattern
  // Using optional chaining and defaults to prevent any errors from breaking the app
  const activeSearchQuery = useActiveSearchStatus();
  const searchQueueQuery = useSearchQueueStatus();
  const downloadQueueQuery = useFooterDownloadQueue();
  const tasksQuery = useTasks(10);

  const activeSearch = activeSearchQuery.data;
  const searchQueue = searchQueueQuery.data;
  const downloadQueue = downloadQueueQuery.data;
  const tasks = tasksQuery.data;

  const [currentTask, setCurrentTask] = useState<Task | null>(null);
  const [showCompleted, setShowCompleted] = useState(false);
  const [completedTask, setCompletedTask] = useState<Task | null>(null);
  const [downloadNotification, setDownloadNotification] = useState<DownloadNotification | null>(null);
  const [initialLoad, setInitialLoad] = useState(true);
  const seenTaskIds = useRef(new Set<number>());
  const seenSearchIds = useRef(new Set<string>());
  const seenDownloadStates = useRef(new Map<number, number>()); // id -> last seen status
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const downloadTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Cleanup timeouts on unmount
  useEffect(() => {
    return () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
      if (downloadTimeoutRef.current) {
        clearTimeout(downloadTimeoutRef.current);
      }
    };
  }, []);

  // Track download queue and surface the most active item in the footer.
  //
  // Status meanings: 0=Queued/Grabbed, 1=Downloading, 6=Importing, 7=Imported,
  // 8=ImportPending (import failed and will retry).
  //
  // Previous version only updated on status *transitions*. That broke when an
  // item got stuck in the import-retry loop (status oscillates 6→8→4→8 forever)
  // because the change detector only fires on transitions *into* status 6, not
  // on 8→6 retries, and never fires for 4 (Failed) at all. The first event to
  // hit status 6 became permanent footer text — a new "imported" notification
  // would briefly overwrite it, but after the 5-second auto-clear it was either
  // empty or back to the stuck importer. New events that go 1→6→7 between polls
  // can also be missed entirely if the 6 phase falls between the 3-second
  // queue refresh ticks.
  //
  // The fix: each tick, *recompute* the displayed notification from the current
  // queue state. Pick the most active item (priority: Importing > Downloading >
  // Grabbed) and show it. We still preserve the "Successfully imported X" toast
  // for 5 seconds when an item transitions to status 7 — that's a discrete
  // event the user wants to see — but the live "Importing X" / "Downloading X"
  // label always reflects current reality.
  useEffect(() => {
    if (!downloadQueue) return;

    // Detect 6→7 transitions so we can show the "Imported X" toast briefly.
    // Iterate before we replace the seenDownloadStates map.
    let importedTransitionItem: typeof downloadQueue[number] | null = null;
    for (const item of downloadQueue) {
      const prevStatus = seenDownloadStates.current.get(item.id);
      if (item.status === 7 && prevStatus !== undefined && prevStatus !== 7) {
        importedTransitionItem = item;
        break; // first one is fine
      }
    }

    // Refresh the seen-states map from the current snapshot so the next tick's
    // transition detection has accurate "previous" values.
    const nextSeen = new Map<number, number>();
    for (const item of downloadQueue) {
      nextSeen.set(item.id, item.status);
    }
    seenDownloadStates.current = nextSeen;

    // If we just observed an import-success transition, show that toast and
    // schedule it to clear after 5s. Don't return — the toast may overlay an
    // active item, which is fine; the toast wins for the 5s window.
    if (importedTransitionItem) {
      setDownloadNotification({
        id: importedTransitionItem.id,
        title: importedTransitionItem.event?.title || importedTransitionItem.title,
        quality: importedTransitionItem.quality,
        status: 'imported',
        timestamp: Date.now(),
      });
      if (downloadTimeoutRef.current) {
        clearTimeout(downloadTimeoutRef.current);
      }
      downloadTimeoutRef.current = setTimeout(() => {
        setDownloadNotification(null);
      }, 5000);
      return;
    }

    // Pick the most active item currently in the queue. Priority:
    // 1. Importing (6) — most recently entered Importing state wins on ties.
    //    We approximate "most recent" by Id descending since the queue is
    //    ordered Added-DESC by the API, so highest id ≈ most recently added.
    // 2. Downloading (1) — same tiebreaker.
    // 3. Grabbed (0)    — same tiebreaker.
    //
    // The Id-descending tiebreaker is what fixes the "stuck on TUFLF" bug:
    // older stuck items don't dominate the footer just because they were added
    // first.
    const sortedQueue = [...downloadQueue].sort((a, b) => b.id - a.id);
    const importing = sortedQueue.find(i => i.status === 6);
    const downloading = sortedQueue.find(i => i.status === 1);
    const grabbed = sortedQueue.find(i => i.status === 0);
    const activeItem = importing ?? downloading ?? grabbed ?? null;

    if (!activeItem) {
      // Nothing active — clear any stale notification (unless it's the
      // "imported" toast still in its display window).
      if (downloadNotification && downloadNotification.status !== 'imported') {
        setDownloadNotification(null);
      }
      return;
    }

    const notificationStatus: DownloadNotification['status'] =
      activeItem.status === 6 ? 'importing' :
      activeItem.status === 1 ? 'downloading' :
      'grabbed';

    // Avoid pointless re-renders: only update if the displayed item or its
    // status actually changed. The 5s "imported" toast set above doesn't
    // reach this branch (we returned early).
    if (downloadNotification &&
        downloadNotification.id === activeItem.id &&
        downloadNotification.status === notificationStatus) {
      return;
    }

    setDownloadNotification({
      id: activeItem.id,
      title: activeItem.event?.title || activeItem.title,
      quality: activeItem.quality,
      status: notificationStatus,
      timestamp: Date.now(),
    });
  }, [downloadQueue, downloadNotification]);

  // Track tasks
  useEffect(() => {
    if (!tasks || tasks.length === 0) {
      setCurrentTask(null);
      return;
    }

    // On initial load, mark all current tasks as "seen"
    if (initialLoad) {
      tasks.forEach((t) => {
        if (t.id) seenTaskIds.current.add(t.id);
      });
      setInitialLoad(false);
    }

    // Find currently running or queued task
    const activeTask = tasks.find(
      (t) => t.status === 'Running' || t.status === 'Queued' || t.status === 'Aborting'
    );

    if (activeTask) {
      setCurrentTask(activeTask);
      setShowCompleted(false);
      if (activeTask.id && !seenTaskIds.current.has(activeTask.id)) {
        seenTaskIds.current.add(activeTask.id);
      }
    } else {
      // Check for recently completed task
      const recentlyCompleted = tasks.find((t) => {
        if (t.status !== 'Completed' && t.status !== 'Failed') return false;
        if (!t.ended || !t.id) return false;
        if (seenTaskIds.current.has(t.id)) return false;

        const endedTime = new Date(t.ended).getTime();
        const now = Date.now();
        return now - endedTime < 5000; // Show for 5 seconds
      });

      if (recentlyCompleted && recentlyCompleted.id !== completedTask?.id) {
        if (recentlyCompleted.id) seenTaskIds.current.add(recentlyCompleted.id);
        setCompletedTask(recentlyCompleted);
        setCurrentTask(recentlyCompleted);
        setShowCompleted(true);

        if (timeoutRef.current) {
          clearTimeout(timeoutRef.current);
        }

        timeoutRef.current = setTimeout(() => {
          setShowCompleted(false);
          setCurrentTask(null);
        }, 5000);
      } else if (!showCompleted) {
        setCurrentTask(null);
      }
    }
  }, [tasks, completedTask?.id, showCompleted, initialLoad]);

  // Track seen search completions
  useEffect(() => {
    if (searchQueue?.recentlyCompleted) {
      searchQueue.recentlyCompleted.forEach((s: SearchQueueItem) => {
        if (s.completedAt) {
          const completedTime = new Date(s.completedAt).getTime();
          if (Date.now() - completedTime > 5000) {
            seenSearchIds.current.add(s.id);
          }
        }
      });
    }
  }, [searchQueue?.recentlyCompleted]);

  // Determine what to show
  const hasActiveSearch = activeSearch && !activeSearch.isComplete;
  const hasQueuedSearches = searchQueue && (searchQueue.pendingCount > 0 || searchQueue.activeCount > 0);
  const hasRecentSearches =
    searchQueue &&
    searchQueue.recentlyCompleted?.some((s: SearchQueueItem) => {
      if (seenSearchIds.current.has(s.id)) return false;
      if (!s.completedAt) return false;
      const completedTime = new Date(s.completedAt).getTime();
      return Date.now() - completedTime < 5000;
    });
  const hasDownloadNotification = downloadNotification !== null;

  // Don't render if nothing to show
  if (!hasActiveSearch && !hasQueuedSearches && !hasRecentSearches && !currentTask && !hasDownloadNotification) return null;

  const progress = currentTask?.progress ?? 0;
  const isRunning = currentTask?.status === 'Running';
  const isQueued = currentTask?.status === 'Queued';
  const isCompleted = currentTask?.status === 'Completed';
  const isFailed = currentTask?.status === 'Failed';

  return (
    <div className="px-3 py-2 space-y-2 border-t border-red-900/30">
      {/* Active indexer search status - Sonarr style */}
      {hasActiveSearch && (
        <div className="bg-black/40 border border-gray-700/50 rounded px-3 py-2 flex items-center gap-2">
          <MagnifyingGlassIcon className="w-4 h-4 text-blue-400 animate-pulse flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <div className="text-xs text-white font-medium truncate">
              {activeSearch.eventTitle || activeSearch.searchQuery}
              {activeSearch.part && ` (${activeSearch.part})`}
            </div>
            <div className="text-xs text-gray-400">
              {activeSearch.activeIndexers} indexer{activeSearch.activeIndexers !== 1 ? 's' : ''}
              {activeSearch.releasesFound > 0 && (
                <span className="text-green-400 ml-1">
                  • {activeSearch.releasesFound} found
                </span>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Queue search activity (when searches are queued/running through SearchQueueService) */}
      {!hasActiveSearch && hasQueuedSearches && searchQueue!.activeSearches.length > 0 && (
        <div className="bg-black/40 border border-gray-700/50 rounded px-3 py-2 flex items-center gap-2">
          <MagnifyingGlassIcon className="w-4 h-4 text-blue-400 animate-pulse flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <div className="text-xs text-white font-medium truncate">
              {searchQueue!.activeSearches[0].eventTitle}
              {searchQueue!.activeSearches[0].part && (
                <span className="text-gray-400"> ({searchQueue!.activeSearches[0].part})</span>
              )}
            </div>
            <div className="text-xs text-gray-400 truncate">
              {searchQueue!.activeSearches[0].message}
            </div>
          </div>
        </div>
      )}

      {/* Recently completed search notification */}
      {!hasActiveSearch && !currentTask && hasRecentSearches && searchQueue?.recentlyCompleted && (
        <>
          {searchQueue.recentlyCompleted
            .filter((s: SearchQueueItem) => {
              if (!s.completedAt) return false;
              const completedTime = new Date(s.completedAt).getTime();
              return Date.now() - completedTime < 5000 && !seenSearchIds.current.has(s.id);
            })
            .slice(0, 1)
            .map((search: SearchQueueItem) => (
              <div key={search.id} className="bg-black/40 border border-gray-700/50 rounded px-3 py-2 flex items-center gap-2">
                {search.success ? (
                  <CheckCircleIcon className="w-4 h-4 text-green-400 flex-shrink-0" />
                ) : (
                  <XCircleIcon className="w-4 h-4 text-yellow-400 flex-shrink-0" />
                )}
                <div className="flex-1 min-w-0">
                  <div className="text-xs text-white font-medium truncate">
                    {search.eventTitle}
                    {search.part && (
                      <span className="text-gray-400"> ({search.part})</span>
                    )}
                  </div>
                  <div className={`text-xs truncate ${search.success ? 'text-green-400' : 'text-yellow-400'}`}>
                    {search.message}
                  </div>
                </div>
              </div>
            ))}
        </>
      )}

      {/* Download status notification (grabbed, downloading, importing, imported) */}
      {hasDownloadNotification && downloadNotification && (
        <div className="bg-black/40 border border-gray-700/50 rounded px-3 py-2 flex items-center gap-2">
          {downloadNotification.status === 'grabbed' && (
            <ArrowDownTrayIcon className="w-4 h-4 text-blue-400 flex-shrink-0" />
          )}
          {downloadNotification.status === 'downloading' && (
            <ArrowDownTrayIcon className="w-4 h-4 text-blue-400 animate-pulse flex-shrink-0" />
          )}
          {downloadNotification.status === 'importing' && (
            <ArrowPathIcon className="w-4 h-4 text-yellow-400 animate-spin flex-shrink-0" />
          )}
          {downloadNotification.status === 'imported' && (
            <DocumentCheckIcon className="w-4 h-4 text-green-400 flex-shrink-0" />
          )}
          <div className="flex-1 min-w-0">
            <div className="text-xs text-white font-medium truncate">
              {downloadNotification.title}
            </div>
            <div className={`text-xs truncate ${
              downloadNotification.status === 'imported' ? 'text-green-400' :
              downloadNotification.status === 'importing' ? 'text-yellow-400' :
              'text-blue-400'
            }`}>
              {downloadNotification.status === 'grabbed' && 'Release grabbed - sent to download client'}
              {downloadNotification.status === 'downloading' && 'Downloading...'}
              {downloadNotification.status === 'importing' && 'Importing to library...'}
              {downloadNotification.status === 'imported' && 'Successfully imported'}
              {downloadNotification.quality && (
                <span className="text-gray-400 ml-1">({downloadNotification.quality})</span>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Current task (non-search tasks like RSS sync, backup, etc.) */}
      {currentTask && !hasActiveSearch && (
        <div className="bg-black/40 border border-gray-700/50 rounded px-3 py-2">
          <div className="flex items-center gap-2">
            {(isRunning || isQueued) && (
              <ArrowPathIcon className="w-4 h-4 text-blue-400 animate-spin flex-shrink-0" />
            )}
            {isCompleted && (
              <CheckCircleIcon className="w-4 h-4 text-green-400 flex-shrink-0" />
            )}
            {isFailed && <XCircleIcon className="w-4 h-4 text-red-400 flex-shrink-0" />}

            <div className="flex-1 min-w-0">
              <div className="text-xs text-white font-medium truncate">
                {currentTask.name}
              </div>
              {currentTask.message && (
                <div className="text-xs text-gray-400 truncate">
                  {currentTask.message}
                </div>
              )}
            </div>

            {isRunning && (
              <div className="text-xs text-gray-400 flex-shrink-0">
                {Math.round(progress)}%
              </div>
            )}
          </div>

          {isRunning && (
            <div className="mt-1.5 w-full bg-gray-700 rounded-full h-1 overflow-hidden">
              <div
                className="h-full bg-gradient-to-r from-red-600 to-red-500 transition-all duration-300 ease-out"
                style={{ width: `${Math.min(100, Math.max(0, progress))}%` }}
              />
            </div>
          )}

          {(isCompleted || isFailed) && (
            <div className={`text-xs mt-1 ${isFailed ? 'text-red-400' : 'text-green-400'}`}>
              {isFailed ? 'Task failed' : 'Task completed'}
            </div>
          )}
        </div>
      )}

      {/* Queue count indicator */}
      {hasQueuedSearches && searchQueue!.pendingCount > 0 && (
        <div className="bg-black/40 border border-gray-700/50 rounded px-3 py-1.5 flex items-center gap-2">
          <QueueListIcon className="w-3.5 h-3.5 text-gray-400 flex-shrink-0" />
          <div className="text-xs text-gray-400">
            {searchQueue!.pendingCount} search{searchQueue!.pendingCount !== 1 ? 'es' : ''} queued
          </div>
        </div>
      )}
    </div>
  );
}

// Wrap in memo to prevent any parent re-renders from affecting this component
// This component manages its own state via React Query hooks
export default memo(FooterStatusBar);
