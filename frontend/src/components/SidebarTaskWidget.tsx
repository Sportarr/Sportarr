import { useEffect, useState } from 'react';
import { useTasks } from '../api/hooks';
import {
  CheckCircleIcon,
  XCircleIcon,
  ArrowPathIcon
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

export default function SidebarTaskWidget() {
  const { data: tasks } = useTasks(10);
  const [currentTask, setCurrentTask] = useState<Task | null>(null);
  const [showCompleted, setShowCompleted] = useState(false);
  const [completedTask, setCompletedTask] = useState<Task | null>(null);
  const [mountTime] = useState(Date.now()); // Track when component mounted

  useEffect(() => {
    if (!tasks || tasks.length === 0) {
      setCurrentTask(null);
      return;
    }

    // Find currently running or queued task
    const activeTask = tasks.find(t =>
      t.status === 'Running' || t.status === 'Queued' || t.status === 'Aborting'
    );

    if (activeTask) {
      setCurrentTask(activeTask);
      setShowCompleted(false);
    } else {
      // Check for recently completed task (within last 3 seconds AND after component mounted)
      const recentlyCompleted = tasks.find(t => {
        if (t.status !== 'Completed' && t.status !== 'Failed') return false;
        if (!t.ended) return false;

        const endedTime = new Date(t.ended).getTime();
        const now = Date.now();

        // Only show if completed after mount time (prevents showing on refresh)
        return (now - endedTime) < 3000 && endedTime > mountTime;
      });

      if (recentlyCompleted && recentlyCompleted.id !== completedTask?.id) {
        setCompletedTask(recentlyCompleted);
        setCurrentTask(recentlyCompleted);
        setShowCompleted(true);

        // Auto-hide after 3 seconds
        setTimeout(() => {
          setShowCompleted(false);
          setCurrentTask(null);
        }, 3000);
      } else if (!showCompleted) {
        setCurrentTask(null);
      }
    }
  }, [tasks, completedTask?.id, showCompleted, mountTime]);

  // Don't render if no active task
  if (!currentTask) return null;

  const progress = currentTask.progress ?? 0;
  const isRunning = currentTask.status === 'Running';
  const isQueued = currentTask.status === 'Queued';
  const isCompleted = currentTask.status === 'Completed';
  const isFailed = currentTask.status === 'Failed';

  return (
    <div className="mx-4 mb-3 p-3 bg-gray-800/50 border border-red-900/30 rounded-lg">
      {/* Task header with icon and name */}
      <div className="flex items-center gap-2 mb-2">
        {(isRunning || isQueued) && (
          <ArrowPathIcon className="w-4 h-4 text-blue-400 animate-spin flex-shrink-0" />
        )}
        {isCompleted && (
          <CheckCircleIcon className="w-4 h-4 text-green-400 flex-shrink-0" />
        )}
        {isFailed && (
          <XCircleIcon className="w-4 h-4 text-red-400 flex-shrink-0" />
        )}

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

      {/* Progress bar - only show for running tasks */}
      {isRunning && (
        <div className="w-full bg-gray-700 rounded-full h-1.5 overflow-hidden">
          <div
            className="h-full bg-gradient-to-r from-red-600 to-red-500 transition-all duration-300 ease-out"
            style={{ width: `${Math.min(100, Math.max(0, progress))}%` }}
          />
        </div>
      )}

      {/* Status message for completed/failed */}
      {(isCompleted || isFailed) && (
        <div className={`text-xs ${isFailed ? 'text-red-400' : 'text-green-400'}`}>
          {isFailed ? 'Task failed' : 'Task completed'}
        </div>
      )}
    </div>
  );
}
