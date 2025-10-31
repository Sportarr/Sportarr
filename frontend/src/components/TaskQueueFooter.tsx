import { useState, useEffect } from 'react';
import { XMarkIcon, CheckCircleIcon, ExclamationCircleIcon, ArrowPathIcon } from '@heroicons/react/24/outline';
import { apiGet } from '../utils/api';

interface Task {
  id: number;
  name: string;
  commandName: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled' | 'Aborting';
  progress: number;
  message: string | null;
  queued: string;
  started: string | null;
  ended: string | null;
  duration: string | null;
}

export default function TaskQueueFooter() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [expanded, setExpanded] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchTasks();
    const interval = setInterval(fetchTasks, 2000); // Poll every 2 seconds
    return () => clearInterval(interval);
  }, []);

  const fetchTasks = async () => {
    try {
      const response = await apiGet('/api/task?pageSize=10');
      if (response.ok) {
        const data = await response.json();
        setTasks(data);
        setLoading(false);
      }
    } catch (error) {
      console.error('Failed to fetch tasks:', error);
      setLoading(false);
    }
  };

  // Filter to show only active/recent tasks
  const activeTasks = tasks.filter(t =>
    t.status === 'Running' ||
    t.status === 'Queued' ||
    t.status === 'Aborting' ||
    (t.status === 'Completed' && new Date(t.ended!).getTime() > Date.now() - 10000) || // Show completed for 10s
    (t.status === 'Failed' && new Date(t.ended!).getTime() > Date.now() - 30000) // Show failed for 30s
  );

  const hasActiveTasks = activeTasks.length > 0;

  if (!hasActiveTasks && !loading) {
    return null; // Don't show footer if no active tasks
  }

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'Completed':
        return <CheckCircleIcon className="w-5 h-5 text-green-500" />;
      case 'Failed':
        return <ExclamationCircleIcon className="w-5 h-5 text-red-500" />;
      case 'Running':
      case 'Queued':
      case 'Aborting':
        return <ArrowPathIcon className="w-5 h-5 text-blue-400 animate-spin" />;
      default:
        return <XMarkIcon className="w-5 h-5 text-gray-500" />;
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Completed':
        return 'text-green-400';
      case 'Failed':
        return 'text-red-400';
      case 'Running':
        return 'text-blue-400';
      case 'Queued':
        return 'text-yellow-400';
      case 'Aborting':
      case 'Cancelled':
        return 'text-orange-400';
      default:
        return 'text-gray-400';
    }
  };

  const formatDuration = (started: string | null, ended: string | null) => {
    if (!started) return '';

    const startTime = new Date(started).getTime();
    const endTime = ended ? new Date(ended).getTime() : Date.now();
    const duration = Math.floor((endTime - startTime) / 1000);

    if (duration < 60) return `${duration}s`;
    const minutes = Math.floor(duration / 60);
    const seconds = duration % 60;
    return `${minutes}m ${seconds}s`;
  };

  // Get the current/most recent task
  const currentTask = activeTasks[0];

  return (
    <>
      {/* Compact footer bar */}
      <div className="fixed bottom-0 left-0 right-0 bg-gradient-to-r from-gray-900 via-black to-gray-900 border-t border-red-900/30 z-40">
        <div className="max-w-screen-2xl mx-auto px-4 py-2">
          <div className="flex items-center justify-between">
            {/* Left side - Current task */}
            <div className="flex items-center space-x-3 flex-1 min-w-0">
              {currentTask && (
                <>
                  <div className="flex-shrink-0">
                    {getStatusIcon(currentTask.status)}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center space-x-2">
                      <p className="text-sm font-medium text-white truncate">
                        {currentTask.name}
                      </p>
                      <span className={`text-xs font-medium ${getStatusColor(currentTask.status)}`}>
                        {currentTask.status}
                      </span>
                    </div>
                    {currentTask.message && (
                      <p className="text-xs text-gray-400 truncate">
                        {currentTask.message}
                      </p>
                    )}
                  </div>
                </>
              )}
            </div>

            {/* Middle - Progress bar for running tasks */}
            {currentTask && currentTask.status === 'Running' && (
              <div className="flex-shrink-0 w-48 mx-4">
                <div className="flex items-center space-x-2">
                  <div className="flex-1 bg-gray-800 rounded-full h-2 overflow-hidden">
                    <div
                      className="bg-gradient-to-r from-red-600 to-red-500 h-full transition-all duration-300"
                      style={{ width: `${currentTask.progress}%` }}
                    />
                  </div>
                  <span className="text-xs text-gray-400 w-10 text-right">
                    {currentTask.progress}%
                  </span>
                </div>
              </div>
            )}

            {/* Right side - Count and expand button */}
            <div className="flex items-center space-x-3 flex-shrink-0">
              {activeTasks.length > 1 && (
                <span className="text-xs text-gray-400">
                  +{activeTasks.length - 1} more
                </span>
              )}
              <button
                onClick={() => setExpanded(!expanded)}
                className="text-xs text-blue-400 hover:text-blue-300 transition-colors px-2 py-1 hover:bg-gray-800 rounded"
              >
                {expanded ? 'Hide' : 'Show All'}
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Expanded task list */}
      {expanded && (
        <div className="fixed bottom-14 left-0 right-0 max-h-96 overflow-y-auto bg-gradient-to-br from-gray-900 via-black to-gray-900 border-t border-red-900/30 shadow-2xl z-40">
          <div className="max-w-screen-2xl mx-auto">
            <div className="p-4">
              <div className="flex items-center justify-between mb-3">
                <h3 className="text-sm font-semibold text-white">Task Queue</h3>
                <button
                  onClick={() => setExpanded(false)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <XMarkIcon className="w-5 h-5" />
                </button>
              </div>
              <div className="space-y-2">
                {activeTasks.map((task) => (
                  <div
                    key={task.id}
                    className="bg-black/30 border border-gray-800 rounded-lg p-3 hover:border-red-900/50 transition-colors"
                  >
                    <div className="flex items-start justify-between">
                      <div className="flex items-start space-x-3 flex-1 min-w-0">
                        <div className="flex-shrink-0 mt-0.5">
                          {getStatusIcon(task.status)}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center space-x-2">
                            <p className="text-sm font-medium text-white">
                              {task.name}
                            </p>
                            <span className={`text-xs font-medium ${getStatusColor(task.status)}`}>
                              {task.status}
                            </span>
                          </div>
                          {task.message && (
                            <p className="text-xs text-gray-400 mt-1">
                              {task.message}
                            </p>
                          )}
                          {task.status === 'Running' && (
                            <div className="mt-2">
                              <div className="flex items-center space-x-2">
                                <div className="flex-1 bg-gray-800 rounded-full h-1.5 overflow-hidden">
                                  <div
                                    className="bg-gradient-to-r from-red-600 to-red-500 h-full transition-all duration-300"
                                    style={{ width: `${task.progress}%` }}
                                  />
                                </div>
                                <span className="text-xs text-gray-500">
                                  {task.progress}%
                                </span>
                              </div>
                            </div>
                          )}
                        </div>
                      </div>
                      <div className="flex-shrink-0 ml-4 text-right">
                        <p className="text-xs text-gray-500">
                          {formatDuration(task.started, task.ended)}
                        </p>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
