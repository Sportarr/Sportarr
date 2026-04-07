import { toast } from 'sonner';
import { useTasks, useQueueTask, useCancelTask, type AppTask } from '../api/hooks';
import {
  PlayIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClockIcon,
  StopIcon,
  ExclamationTriangleIcon
} from '@heroicons/react/24/outline';
import CompactTableFrame from '../components/CompactTableFrame';
import PageHeader from '../components/PageHeader';
import PageShell, { PageErrorState, PageLoadingState } from '../components/PageShell';
import { useCompactView } from '../hooks/useCompactView';
import { TABLE_ROW_HOVER } from '../utils/designTokens';
import { parseAsUtc } from '../utils/timezone';

export default function TasksPage() {
  const { data: tasks, isLoading, error } = useTasks(100);
  const queueTask = useQueueTask();
  const cancelTask = useCancelTask();
  const compactView = useCompactView();

  const formatDate = (dateString: string | null) => {
    if (!dateString) return '-';
    return parseAsUtc(dateString).toLocaleString();
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
        toast.error('Cancel Failed', {
          description: 'Failed to cancel task. Please try again.',
        });
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
      toast.error('Queue Failed', {
        description: 'Failed to queue test task. Please try again.',
      });
    }
  };

  if (isLoading) {
    return <PageLoadingState label="Loading tasks..." />;
  }

  if (error) {
    return (
      <PageErrorState title="Error loading tasks" message={(error as Error).message} />
    );
  }

  const runningTasks = tasks?.filter(t => t.status === 'Running' || t.status === 'Aborting') || [];
  const queuedTasks = tasks?.filter(t => t.status === 'Queued') || [];
  const completedTasks = tasks?.filter(t => t.status === 'Completed' || t.status === 'Failed' || t.status === 'Cancelled') || [];

  const renderTaskSection = (sectionTitle: string, taskList: AppTask[], showCount = false) => {
    if (taskList.length === 0 && sectionTitle !== 'History') return null;

    return (
      <div className="mb-6">
        <h2 className="text-xl font-semibold text-white mb-3">
          {sectionTitle}{showCount ? ` (${taskList.length})` : ''}
        </h2>
        {compactView ? (
          <CompactTableFrame>
              <thead>
                <tr className="text-xs text-gray-400 uppercase text-left border-b border-gray-700">
                  <th className="px-3 py-1.5">Name</th>
                  <th className="px-2 py-1.5">Command</th>
                  <th className="px-2 py-1.5">Status</th>
                  {sectionTitle === 'Running' && <th className="px-2 py-1.5">Progress</th>}
                  {sectionTitle === 'Running' && <th className="px-2 py-1.5">Started</th>}
                  {sectionTitle === 'Queued' && <th className="px-2 py-1.5">Queued At</th>}
                  {sectionTitle === 'History' && <th className="px-2 py-1.5">Duration</th>}
                  {sectionTitle === 'History' && <th className="px-2 py-1.5">Ended</th>}
                  <th className="px-2 py-1.5 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-700">
                {taskList.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="px-3 py-8 text-center text-gray-400">
                      No completed tasks yet
                    </td>
                  </tr>
                ) : taskList.map((task) => (
                  <tr
                    key={task.id}
                    className={`${TABLE_ROW_HOVER} cursor-pointer`}
                  >
                    <td className="px-3 py-1.5 font-medium text-white">{task.name}</td>
                    <td className="px-2 py-1.5 text-gray-400 text-xs">{task.commandName}</td>
                    <td className="px-2 py-1.5">
                      <span className={`flex items-center gap-1 ${getStatusColor(task.status)}`}>
                        {getStatusIcon(task.status)}
                        <span className="text-xs">{task.status}</span>
                      </span>
                    </td>
                    {sectionTitle === 'Running' && (
                      <td className="px-2 py-1.5 text-xs text-gray-400">
                        {task.progress !== null ? `${task.progress}%` : '—'}
                      </td>
                    )}
                    {sectionTitle === 'Running' && (
                      <td className="px-2 py-1.5 text-xs text-gray-400 whitespace-nowrap">
                        {formatDate(task.started)}
                      </td>
                    )}
                    {sectionTitle === 'Queued' && (
                      <td className="px-2 py-1.5 text-xs text-gray-400 whitespace-nowrap">
                        {formatDate(task.queued)}
                      </td>
                    )}
                    {sectionTitle === 'History' && (
                      <td className="px-2 py-1.5 text-xs text-gray-400">
                        {formatDuration(task.duration)}
                      </td>
                    )}
                    {sectionTitle === 'History' && (
                      <td className="px-2 py-1.5 text-xs text-gray-400 whitespace-nowrap">
                        {formatDate(task.ended)}
                      </td>
                    )}
                    <td className="px-2 py-1.5 text-right" onClick={(e) => e.stopPropagation()}>
                      {(task.status === 'Running' || task.status === 'Queued') && (
                        <button
                          onClick={() => handleCancelTask(task.id)}
                          disabled={cancelTask.isPending}
                          className="p-1 text-red-400 hover:text-red-300 hover:bg-red-900/20 rounded transition-colors disabled:opacity-50"
                          title="Cancel task"
                        >
                          <StopIcon className="w-4 h-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
          </CompactTableFrame>
        ) : (
          <div className="flex flex-col gap-3">
            {taskList.length === 0 ? (
              <div className="bg-gray-800 border border-gray-700 rounded-lg p-6 text-center text-gray-400">
                No completed tasks yet
              </div>
            ) : taskList.map((task) => (
              <div
                key={task.id}
                className="bg-gray-800 border border-gray-700 rounded-lg p-4 hover:bg-gray-750 transition-colors cursor-pointer"
              >
                <div className="flex items-start justify-between">
                  <div className="flex items-start gap-3 flex-1 min-w-0">
                    <div className={`mt-1 ${getStatusColor(task.status)}`}>
                      {getStatusIcon(task.status)}
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 flex-wrap mb-1">
                        <h3 className="text-base font-semibold text-white">{task.name}</h3>
                        <span className={`px-2 py-0.5 text-xs rounded ${getStatusColor(task.status)}`}>
                          {task.status}
                        </span>
                      </div>
                      <p className="text-sm text-gray-400">{task.commandName}</p>
                      {task.message && (
                        <p className="text-sm text-gray-300 mt-1">{task.message}</p>
                      )}
                      {task.status === 'Running' && task.progress !== null && (
                        <div className="mt-2">
                          <div className="flex items-center gap-2">
                            <div className="flex-1 bg-gray-700 rounded-full h-1.5">
                              <div
                                className="bg-red-600 h-1.5 rounded-full transition-all duration-300"
                                style={{ width: `${task.progress}%` }}
                              />
                            </div>
                            <span className="text-xs text-gray-400 flex-shrink-0">{task.progress}%</span>
                          </div>
                        </div>
                      )}
                      {task.status === 'Failed' && task.exception && (
                        <div className="mt-2 p-2 bg-red-900/20 border border-red-900/30 rounded">
                          <div className="flex items-start gap-2">
                            <ExclamationTriangleIcon className="w-4 h-4 text-red-400 flex-shrink-0 mt-0.5" />
                            <pre className="text-xs text-red-300 font-mono whitespace-pre-wrap break-all">
                              {task.exception}
                            </pre>
                          </div>
                        </div>
                      )}
                      <div className="flex items-center gap-3 text-sm text-gray-500 flex-wrap mt-1">
                        {task.started && <span>Started: {formatDate(task.started)}</span>}
                        {task.queued && !task.started && <span>Queued: {formatDate(task.queued)}</span>}
                        {task.ended && <><span className="text-gray-600">•</span><span>Ended: {formatDate(task.ended)}</span></>}
                        {task.duration && <><span className="text-gray-600">•</span><span>Duration: {formatDuration(task.duration)}</span></>}
                        <span className="text-gray-600">•</span>
                        <span>Priority: {task.priority}</span>
                      </div>
                    </div>
                  </div>
                  <div className="ml-4" onClick={(e) => e.stopPropagation()}>
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
              </div>
            ))}
          </div>
        )}
      </div>
    );
  };

  return (
    <PageShell>
      <PageHeader
        title="Tasks"
        subtitle="View and manage background tasks"
        actions={
          <button
            onClick={handleTestTask}
            disabled={queueTask.isPending}
            className="rounded bg-red-600 px-4 py-2 text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {queueTask.isPending ? 'Queuing...' : 'Queue Test Task'}
          </button>
        }
      />

      {runningTasks.length > 0 && renderTaskSection('Running', runningTasks)}
      {queuedTasks.length > 0 && renderTaskSection('Queued', queuedTasks, true)}
      {renderTaskSection('History', completedTasks)}
    </PageShell>
  );
}
