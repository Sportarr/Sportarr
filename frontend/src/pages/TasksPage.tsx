import { useState } from 'react';
import { useTasks, useQueueTask, useCancelTask, type AppTask } from '../api/hooks';
import {
  PlayIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClockIcon,
  StopIcon,
  ExclamationTriangleIcon
} from '@heroicons/react/24/outline';

export default function TasksPage() {
  const { data: tasks, isLoading, error } = useTasks(100);
  const queueTask = useQueueTask();
  const cancelTask = useCancelTask();
  const [selectedTask, setSelectedTask] = useState<AppTask | null>(null);

  const formatDate = (dateString: string | null) => {
    if (!dateString) return '-';
    return new Date(dateString).toLocaleString();
  };

  const formatDuration = (duration: string | null) => {
    if (!duration) return '-';
    // Duration is in format like "00:00:05.1234567"
    const parts = duration.split(':');
    if (parts.length === 3) {
      const hours = parseInt(parts[0]);
      const minutes = parseInt(parts[1]);
      const seconds = parseFloat(parts[2]);

      if (hours > 0) {
        return `${hours}h ${minutes}m ${Math.floor(seconds)}s`;
      } else if (minutes > 0) {
        return `${minutes}m ${Math.floor(seconds)}s`;
      } else {
        return `${seconds.toFixed(1)}s`;
      }
    }
    return duration;
  };

  const getStatusColor = (status: AppTask['status']) => {
    switch (status) {
      case 'Queued':
        return 'text-blue-400';
      case 'Running':
      case 'Aborting':
        return 'text-yellow-400';
      case 'Completed':
        return 'text-green-400';
      case 'Failed':
        return 'text-red-400';
      case 'Cancelled':
        return 'text-gray-400';
      default:
        return 'text-gray-400';
    }
  };

  const getStatusIcon = (status: AppTask['status']) => {
    switch (status) {
      case 'Queued':
        return <ClockIcon className="w-5 h-5" />;
      case 'Running':
        return <PlayIcon className="w-5 h-5" />;
      case 'Aborting':
        return <StopIcon className="w-5 h-5" />;
      case 'Completed':
        return <CheckCircleIcon className="w-5 h-5" />;
      case 'Failed':
        return <XCircleIcon className="w-5 h-5" />;
      case 'Cancelled':
        return <StopIcon className="w-5 h-5" />;
      default:
        return <ClockIcon className="w-5 h-5" />;
    }
  };

  const handleCancelTask = async (id: number) => {
    if (confirm('Are you sure you want to cancel this task?')) {
      try {
        await cancelTask.mutateAsync(id);
      } catch (err) {
        console.error('Failed to cancel task:', err);
        alert('Failed to cancel task');
      }
    }
  };

  const handleTestTask = async () => {
    try {
      await queueTask.mutateAsync({
        name: 'Test Task',
        commandName: 'TestTask',
        priority: 0
      });
    } catch (err) {
      console.error('Failed to queue test task:', err);
      alert('Failed to queue test task');
    }
  };

  if (isLoading) {
    return (
      <div className="p-8">
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="bg-red-900 border border-red-700 text-red-100 px-4 py-3 rounded">
          <p className="font-bold">Error loading tasks</p>
          <p className="text-sm">{(error as Error).message}</p>
        </div>
      </div>
    );
  }

  const runningTasks = tasks?.filter(t => t.status === 'Running' || t.status === 'Aborting') || [];
  const queuedTasks = tasks?.filter(t => t.status === 'Queued') || [];
  const completedTasks = tasks?.filter(t => t.status === 'Completed' || t.status === 'Failed' || t.status === 'Cancelled') || [];

  return (
    <div className="p-8">
      <div className="max-w-7xl mx-auto">
        <div className="mb-8 flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold text-white mb-2">Tasks</h1>
            <p className="text-gray-400">View and manage background tasks</p>
          </div>
          <button
            onClick={handleTestTask}
            disabled={queueTask.isPending}
            className="px-4 py-2 bg-red-600 text-white rounded hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {queueTask.isPending ? 'Queuing...' : 'Queue Test Task'}
          </button>
        </div>

        {/* Running Tasks */}
        {runningTasks.length > 0 && (
          <div className="mb-6">
            <h2 className="text-xl font-semibold text-white mb-3">Running</h2>
            <div className="space-y-3">
              {runningTasks.map((task) => (
                <div
                  key={task.id}
                  className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6 shadow-xl"
                >
                  <div className="flex items-start justify-between mb-4">
                    <div className="flex items-start gap-3 flex-1">
                      <div className={`mt-1 ${getStatusColor(task.status)}`}>
                        {getStatusIcon(task.status)}
                      </div>
                      <div className="flex-1 min-w-0">
                        <h3 className="text-lg font-semibold text-white">{task.name}</h3>
                        <p className="text-sm text-gray-400 mt-1">{task.commandName}</p>
                        {task.message && (
                          <p className="text-sm text-gray-300 mt-2">{task.message}</p>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center gap-3">
                      <span className={`px-3 py-1 rounded text-sm font-medium ${getStatusColor(task.status)}`}>
                        {task.status}
                      </span>
                      {(task.status === 'Running' || task.status === 'Queued') && (
                        <button
                          onClick={() => handleCancelTask(task.id)}
                          disabled={cancelTask.isPending}
                          className="p-2 text-red-400 hover:text-red-300 hover:bg-red-900/20 rounded transition-colors disabled:opacity-50"
                          title="Cancel task"
                        >
                          <StopIcon className="w-5 h-5" />
                        </button>
                      )}
                    </div>
                  </div>
                  {task.progress !== null && (
                    <div className="mt-4">
                      <div className="flex justify-between text-sm text-gray-400 mb-2">
                        <span>Progress</span>
                        <span>{task.progress}%</span>
                      </div>
                      <div className="w-full bg-gray-700 rounded-full h-2">
                        <div
                          className="bg-red-600 h-2 rounded-full transition-all duration-300"
                          style={{ width: `${task.progress}%` }}
                        ></div>
                      </div>
                    </div>
                  )}
                  <div className="mt-4 grid grid-cols-2 gap-4 text-sm">
                    <div>
                      <span className="text-gray-400">Started:</span>
                      <span className="text-white ml-2">{formatDate(task.started)}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">Priority:</span>
                      <span className="text-white ml-2">{task.priority}</span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Queued Tasks */}
        {queuedTasks.length > 0 && (
          <div className="mb-6">
            <h2 className="text-xl font-semibold text-white mb-3">Queued ({queuedTasks.length})</h2>
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg shadow-xl overflow-hidden">
              <div className="divide-y divide-red-900/20">
                {queuedTasks.map((task) => (
                  <div
                    key={task.id}
                    className="px-6 py-4 hover:bg-red-900/10 transition-colors cursor-pointer"
                    onClick={() => setSelectedTask(task)}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-3 flex-1">
                        <div className={getStatusColor(task.status)}>
                          {getStatusIcon(task.status)}
                        </div>
                        <div>
                          <p className="text-white font-medium">{task.name}</p>
                          <p className="text-sm text-gray-400">{task.commandName}</p>
                        </div>
                      </div>
                      <div className="flex items-center gap-3">
                        <span className="text-sm text-gray-400">
                          Queued: {formatDate(task.queued)}
                        </span>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            handleCancelTask(task.id);
                          }}
                          disabled={cancelTask.isPending}
                          className="p-2 text-red-400 hover:text-red-300 hover:bg-red-900/20 rounded transition-colors disabled:opacity-50"
                          title="Cancel task"
                        >
                          <StopIcon className="w-5 h-5" />
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* Completed Tasks */}
        <div>
          <h2 className="text-xl font-semibold text-white mb-3">History</h2>
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg shadow-xl overflow-hidden">
            {completedTasks.length === 0 ? (
              <div className="px-6 py-12 text-center text-gray-400">
                No completed tasks yet
              </div>
            ) : (
              <div className="divide-y divide-red-900/20">
                {completedTasks.map((task) => (
                  <div
                    key={task.id}
                    className="px-6 py-4 hover:bg-red-900/10 transition-colors cursor-pointer"
                    onClick={() => setSelectedTask(task)}
                  >
                    <div className="flex items-start justify-between">
                      <div className="flex items-start gap-3 flex-1">
                        <div className={`mt-1 ${getStatusColor(task.status)}`}>
                          {getStatusIcon(task.status)}
                        </div>
                        <div className="flex-1 min-w-0">
                          <p className="text-white font-medium">{task.name}</p>
                          <p className="text-sm text-gray-400 mt-1">{task.commandName}</p>
                          {task.message && (
                            <p className="text-sm text-gray-300 mt-1">{task.message}</p>
                          )}
                          {task.status === 'Failed' && task.exception && (
                            <div className="mt-2 p-2 bg-red-900/20 border border-red-900/30 rounded">
                              <div className="flex items-start gap-2">
                                <ExclamationTriangleIcon className="w-5 h-5 text-red-400 flex-shrink-0 mt-0.5" />
                                <pre className="text-xs text-red-300 font-mono whitespace-pre-wrap break-all">
                                  {task.exception}
                                </pre>
                              </div>
                            </div>
                          )}
                        </div>
                      </div>
                      <div className="flex flex-col items-end gap-2">
                        <span className={`px-3 py-1 rounded text-sm font-medium ${getStatusColor(task.status)}`}>
                          {task.status}
                        </span>
                        <div className="text-sm text-gray-400 text-right">
                          <div>Duration: {formatDuration(task.duration)}</div>
                          <div className="mt-1">Ended: {formatDate(task.ended)}</div>
                        </div>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
