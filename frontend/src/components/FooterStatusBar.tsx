import { useEffect, useState, useRef } from 'react';
import { useTasks } from '../api/hooks';
import { useQuery } from '@tanstack/react-query';
import apiClient from '../api/client';
import {
  CheckCircleIcon,
  XCircleIcon,
  ArrowPathIcon,
  MagnifyingGlassIcon,
  QueueListIcon,
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

// Hook to fetch active search status (polls frequently for real-time updates)
const useActiveSearchStatus = () => {
  return useQuery({
    queryKey: ['activeSearchStatus'],
    queryFn: async () => {
      const { data } = await apiClient.get<ActiveSearchStatus | null>('/search/active');
      return data;
    },
    refetchInterval: 500, // Poll every 500ms for responsive updates
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
    refetchInterval: 1000,
  });
};

/**
 * Sonarr-style fixed footer status bar
 * Shows all status information (search progress, tasks, queue) at bottom-left of screen
 */
export default function FooterStatusBar() {
  const { data: activeSearch } = useActiveSearchStatus();
  const { data: searchQueue } = useSearchQueueStatus();
  const { data: tasks } = useTasks(10);

  const [currentTask, setCurrentTask] = useState<Task | null>(null);
  const [showCompleted, setShowCompleted] = useState(false);
  const [completedTask, setCompletedTask] = useState<Task | null>(null);
  const [initialLoad, setInitialLoad] = useState(true);
  const seenTaskIds = useRef(new Set<number>());
  const seenSearchIds = useRef(new Set<string>());
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Cleanup timeout on unmount
  useEffect(() => {
    return () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
    };
  }, []);

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
    searchQueue.recentlyCompleted.some((s: SearchQueueItem) => {
      if (seenSearchIds.current.has(s.id)) return false;
      if (!s.completedAt) return false;
      const completedTime = new Date(s.completedAt).getTime();
      return Date.now() - completedTime < 5000;
    });

  // Don't render if nothing to show
  if (!hasActiveSearch && !hasQueuedSearches && !hasRecentSearches && !currentTask) return null;

  const progress = currentTask?.progress ?? 0;
  const isRunning = currentTask?.status === 'Running';
  const isQueued = currentTask?.status === 'Queued';
  const isCompleted = currentTask?.status === 'Completed';
  const isFailed = currentTask?.status === 'Failed';

  return (
    <div className="fixed bottom-0 left-0 z-50 p-3 space-y-2" style={{ marginLeft: '256px' }}>
      {/* Active indexer search status - Sonarr style */}
      {hasActiveSearch && (
        <div className="bg-gray-900/95 border border-gray-700 rounded-lg shadow-lg px-4 py-2.5 flex items-center gap-3 min-w-[300px] max-w-[400px]">
          <MagnifyingGlassIcon className="w-5 h-5 text-blue-400 animate-pulse flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <div className="text-sm text-white font-medium truncate">
              Searching indexers for
            </div>
            <div className="text-sm text-gray-300 truncate">
              [{activeSearch.eventTitle || activeSearch.searchQuery}
              {activeSearch.part && ` : ${activeSearch.part}`}].
            </div>
            <div className="text-xs text-gray-400 mt-0.5">
              {activeSearch.activeIndexers} active indexer{activeSearch.activeIndexers !== 1 ? 's' : ''}
              {activeSearch.releasesFound > 0 && (
                <span className="text-green-400 ml-2">
                  {activeSearch.releasesFound} found
                </span>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Queue search activity (when searches are queued/running through SearchQueueService) */}
      {!hasActiveSearch && hasQueuedSearches && searchQueue!.activeSearches.length > 0 && (
        <div className="bg-gray-900/95 border border-gray-700 rounded-lg shadow-lg px-4 py-2.5 flex items-center gap-3 min-w-[300px] max-w-[400px]">
          <MagnifyingGlassIcon className="w-5 h-5 text-blue-400 animate-pulse flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <div className="text-sm text-white font-medium truncate">
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
              <div key={search.id} className="bg-gray-900/95 border border-gray-700 rounded-lg shadow-lg px-4 py-2.5 flex items-center gap-3 min-w-[300px] max-w-[400px]">
                {search.success ? (
                  <CheckCircleIcon className="w-5 h-5 text-green-400 flex-shrink-0" />
                ) : (
                  <XCircleIcon className="w-5 h-5 text-yellow-400 flex-shrink-0" />
                )}
                <div className="flex-1 min-w-0">
                  <div className="text-sm text-white font-medium truncate">
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

      {/* Current task (non-search tasks like RSS sync, backup, etc.) */}
      {currentTask && !hasActiveSearch && (
        <div className="bg-gray-900/95 border border-gray-700 rounded-lg shadow-lg px-4 py-2.5 min-w-[300px] max-w-[400px]">
          <div className="flex items-center gap-3">
            {(isRunning || isQueued) && (
              <ArrowPathIcon className="w-5 h-5 text-blue-400 animate-spin flex-shrink-0" />
            )}
            {isCompleted && (
              <CheckCircleIcon className="w-5 h-5 text-green-400 flex-shrink-0" />
            )}
            {isFailed && <XCircleIcon className="w-5 h-5 text-red-400 flex-shrink-0" />}

            <div className="flex-1 min-w-0">
              <div className="text-sm text-white font-medium truncate">
                {currentTask.name}
              </div>
              {currentTask.message && (
                <div className="text-xs text-gray-400 truncate mt-0.5">
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
            <div className="mt-2 w-full bg-gray-700 rounded-full h-1.5 overflow-hidden">
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
        <div className="bg-gray-900/95 border border-gray-700 rounded-lg shadow-lg px-4 py-2 flex items-center gap-3">
          <QueueListIcon className="w-4 h-4 text-gray-400 flex-shrink-0" />
          <div className="text-xs text-gray-400">
            {searchQueue!.pendingCount} search{searchQueue!.pendingCount !== 1 ? 'es' : ''} queued
          </div>
        </div>
      )}
    </div>
  );
}
