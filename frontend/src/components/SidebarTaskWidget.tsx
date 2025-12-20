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
  ChevronUpIcon,
  ChevronDownIcon,
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

// Hook to fetch search queue status
const useSearchQueueStatus = () => {
  return useQuery({
    queryKey: ['searchQueueStatus'],
    queryFn: async () => {
      const { data } = await apiClient.get<SearchQueueStatus>('/search/queue');
      return data;
    },
    refetchInterval: 1000, // Poll every 1 second for responsive updates
  });
};

export default function SidebarTaskWidget() {
  const { data: tasks } = useTasks(10);
  const { data: searchQueue } = useSearchQueueStatus();
  const [currentTask, setCurrentTask] = useState<Task | null>(null);
  const [showCompleted, setShowCompleted] = useState(false);
  const [completedTask, setCompletedTask] = useState<Task | null>(null);
  const [expanded, setExpanded] = useState(false);
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

  // Calculate combined activity
  const hasSearchActivity =
    searchQueue && (searchQueue.pendingCount > 0 || searchQueue.activeCount > 0);
  const hasRecentSearches =
    searchQueue &&
    searchQueue.recentlyCompleted.some((s: SearchQueueItem) => {
      if (seenSearchIds.current.has(s.id)) return false;
      const completedTime = new Date(s.completedAt!).getTime();
      return Date.now() - completedTime < 5000;
    });

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

  const totalPending = (searchQueue?.pendingCount ?? 0);
  const totalActive = (searchQueue?.activeCount ?? 0) + (currentTask && (currentTask.status === 'Running' || currentTask.status === 'Queued') ? 1 : 0);

  // Don't render if nothing to show
  if (!currentTask && !hasSearchActivity && !hasRecentSearches) return null;

  const progress = currentTask?.progress ?? 0;
  const isRunning = currentTask?.status === 'Running';
  const isQueued = currentTask?.status === 'Queued';
  const isCompleted = currentTask?.status === 'Completed';
  const isFailed = currentTask?.status === 'Failed';

  // Get current active search if any
  const activeSearch = searchQueue?.activeSearches?.[0];

  return (
    <div className="mx-4 mb-3">
      {/* Main widget container */}
      <div className="bg-gray-800/50 border border-red-900/30 rounded-lg overflow-hidden">
        {/* Header with queue counts */}
        {(totalPending > 0 || totalActive > 0) && (
          <button
            onClick={() => setExpanded(!expanded)}
            className="w-full px-3 py-2 flex items-center justify-between bg-gray-800/80 hover:bg-gray-700/50 transition-colors"
          >
            <div className="flex items-center gap-2">
              <QueueListIcon className="w-4 h-4 text-blue-400" />
              <span className="text-xs font-medium text-gray-200">
                {totalActive > 0 ? `${totalActive} active` : ''}
                {totalActive > 0 && totalPending > 0 ? ', ' : ''}
                {totalPending > 0 ? `${totalPending} queued` : ''}
              </span>
            </div>
            {expanded ? (
              <ChevronDownIcon className="w-4 h-4 text-gray-400" />
            ) : (
              <ChevronUpIcon className="w-4 h-4 text-gray-400" />
            )}
          </button>
        )}

        {/* Current task/search */}
        <div className="p-3">
          {/* Show active search */}
          {activeSearch && (
            <div className="flex items-center gap-2 mb-2">
              <MagnifyingGlassIcon className="w-4 h-4 text-blue-400 animate-pulse flex-shrink-0" />
              <div className="flex-1 min-w-0">
                <div className="text-xs font-medium text-gray-200 truncate">
                  {activeSearch.eventTitle}
                  {activeSearch.part && (
                    <span className="text-gray-400"> ({activeSearch.part})</span>
                  )}
                </div>
                <div className="text-xs text-gray-400 truncate">{activeSearch.message}</div>
              </div>
            </div>
          )}

          {/* Show current task if different from search */}
          {currentTask && !activeSearch && (
            <>
              <div className="flex items-center gap-2 mb-2">
                {(isRunning || isQueued) && (
                  <ArrowPathIcon className="w-4 h-4 text-blue-400 animate-spin flex-shrink-0" />
                )}
                {isCompleted && (
                  <CheckCircleIcon className="w-4 h-4 text-green-400 flex-shrink-0" />
                )}
                {isFailed && <XCircleIcon className="w-4 h-4 text-red-400 flex-shrink-0" />}

                <div className="flex-1 min-w-0">
                  <div className="text-xs font-medium text-gray-200 truncate">
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
                <div className="w-full bg-gray-700 rounded-full h-1.5 overflow-hidden">
                  <div
                    className="h-full bg-gradient-to-r from-red-600 to-red-500 transition-all duration-300 ease-out"
                    style={{ width: `${Math.min(100, Math.max(0, progress))}%` }}
                  />
                </div>
              )}

              {(isCompleted || isFailed) && (
                <div className={`text-xs ${isFailed ? 'text-red-400' : 'text-green-400'}`}>
                  {isFailed ? 'Task failed' : 'Task completed'}
                </div>
              )}
            </>
          )}

          {/* Show recently completed search notification */}
          {!activeSearch && !currentTask && hasRecentSearches && searchQueue?.recentlyCompleted && (
            <>
              {searchQueue.recentlyCompleted
                .filter((s: SearchQueueItem) => {
                  if (!s.completedAt) return false;
                  const completedTime = new Date(s.completedAt).getTime();
                  return Date.now() - completedTime < 5000 && !seenSearchIds.current.has(s.id);
                })
                .slice(0, 1)
                .map((search: SearchQueueItem) => (
                  <div key={search.id} className="flex items-center gap-2">
                    {search.success ? (
                      <CheckCircleIcon className="w-4 h-4 text-green-400 flex-shrink-0" />
                    ) : (
                      <XCircleIcon className="w-4 h-4 text-yellow-400 flex-shrink-0" />
                    )}
                    <div className="flex-1 min-w-0">
                      <div className="text-xs font-medium text-gray-200 truncate">
                        {search.eventTitle}
                        {search.part && (
                          <span className="text-gray-400"> ({search.part})</span>
                        )}
                      </div>
                      <div
                        className={`text-xs truncate ${
                          search.success ? 'text-green-400' : 'text-yellow-400'
                        }`}
                      >
                        {search.message}
                      </div>
                    </div>
                  </div>
                ))}
            </>
          )}
        </div>

        {/* Expanded queue list */}
        {expanded && searchQueue && (
          <div className="border-t border-gray-700 max-h-48 overflow-y-auto">
            {/* Pending searches */}
            {searchQueue.pendingSearches.map((search: SearchQueueItem) => (
              <div
                key={search.id}
                className="px-3 py-2 border-b border-gray-700/50 last:border-b-0"
              >
                <div className="flex items-center gap-2">
                  <QueueListIcon className="w-3 h-3 text-gray-500 flex-shrink-0" />
                  <div className="flex-1 min-w-0">
                    <div className="text-xs text-gray-300 truncate">
                      {search.eventTitle}
                      {search.part && (
                        <span className="text-gray-500"> ({search.part})</span>
                      )}
                    </div>
                  </div>
                  <span className="text-[10px] text-gray-500">Queued</span>
                </div>
              </div>
            ))}

            {/* Active searches */}
            {searchQueue.activeSearches.map((search: SearchQueueItem) => (
              <div
                key={search.id}
                className="px-3 py-2 border-b border-gray-700/50 last:border-b-0 bg-blue-900/10"
              >
                <div className="flex items-center gap-2">
                  <MagnifyingGlassIcon className="w-3 h-3 text-blue-400 animate-pulse flex-shrink-0" />
                  <div className="flex-1 min-w-0">
                    <div className="text-xs text-gray-200 truncate">
                      {search.eventTitle}
                      {search.part && (
                        <span className="text-gray-400"> ({search.part})</span>
                      )}
                    </div>
                  </div>
                  <span className="text-[10px] text-blue-400">Searching</span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
