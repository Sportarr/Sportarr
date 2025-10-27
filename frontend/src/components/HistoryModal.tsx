import { Fragment, useState, useEffect } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  ClockIcon,
  CheckCircleIcon,
  XCircleIcon,
  ArrowDownTrayIcon,
  MagnifyingGlassIcon,
} from '@heroicons/react/24/outline';

interface HistoryModalProps {
  isOpen: boolean;
  onClose: () => void;
  historyType: 'organization' | 'event' | 'fightcard';
  title: string;
  historyParams: {
    organizationName?: string;
    eventId?: number;
    fightCardId?: number;
  };
}

interface HistoryRecord {
  id: number;
  eventType: 'grabbed' | 'downloaded' | 'failed' | 'deleted' | 'renamed' | 'searched';
  eventTitle: string;
  sourceTitle: string;
  quality: string | null;
  date: string;
  data?: {
    indexer?: string;
    downloadClient?: string;
    message?: string;
  };
}

export default function HistoryModal({
  isOpen,
  onClose,
  historyType,
  title,
  historyParams,
}: HistoryModalProps) {
  const [isLoading, setIsLoading] = useState(false);
  const [history, setHistory] = useState<HistoryRecord[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      fetchHistory();
    }
  }, [isOpen]);

  const fetchHistory = async () => {
    setIsLoading(true);
    setError(null);
    setHistory([]);

    try {
      let endpoint = '';
      if (historyType === 'event' && historyParams.eventId) {
        endpoint = `/api/event/${historyParams.eventId}/history`;
      } else if (historyType === 'fightcard' && historyParams.fightCardId) {
        endpoint = `/api/fightcard/${historyParams.fightCardId}/history`;
      } else if (historyType === 'organization' && historyParams.organizationName) {
        endpoint = `/api/organization/${encodeURIComponent(historyParams.organizationName)}/history`;
      }

      const response = await fetch(endpoint);
      if (!response.ok) {
        throw new Error('Failed to fetch history');
      }

      const data = await response.json();
      setHistory(data || []);
    } catch (error) {
      console.error('Failed to fetch history:', error);
      setError('Failed to load history. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  const getHistoryTitle = () => {
    switch (historyType) {
      case 'organization':
        return `History: ${title}`;
      case 'event':
        return `History: ${title}`;
      case 'fightcard':
        return `History: ${title}`;
    }
  };

  const getEventIcon = (eventType: HistoryRecord['eventType']) => {
    switch (eventType) {
      case 'grabbed':
        return <ArrowDownTrayIcon className="w-5 h-5 text-blue-400" />;
      case 'downloaded':
        return <CheckCircleIcon className="w-5 h-5 text-green-400" />;
      case 'failed':
        return <XCircleIcon className="w-5 h-5 text-red-400" />;
      case 'searched':
        return <MagnifyingGlassIcon className="w-5 h-5 text-purple-400" />;
      default:
        return <ClockIcon className="w-5 h-5 text-gray-400" />;
    }
  };

  const getEventColor = (eventType: HistoryRecord['eventType']) => {
    switch (eventType) {
      case 'grabbed':
        return 'border-blue-600/30 bg-blue-900/10';
      case 'downloaded':
        return 'border-green-600/30 bg-green-900/10';
      case 'failed':
        return 'border-red-600/30 bg-red-900/10';
      case 'searched':
        return 'border-purple-600/30 bg-purple-900/10';
      default:
        return 'border-gray-600/30 bg-gray-900/10';
    }
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins} minute${diffMins !== 1 ? 's' : ''} ago`;
    if (diffHours < 24) return `${diffHours} hour${diffHours !== 1 ? 's' : ''} ago`;
    if (diffDays < 7) return `${diffDays} day${diffDays !== 1 ? 's' : ''} ago`;

    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: date.getFullYear() !== now.getFullYear() ? 'numeric' : undefined,
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  return (
    <Transition appear show={isOpen} as={Fragment}>
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/80" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-4xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header */}
                <div className="relative bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900 border-b border-red-900/30 p-6">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-2xl font-bold text-white mb-1">{getHistoryTitle()}</h2>
                      <p className="text-gray-400 text-sm">Download and activity history</p>
                    </div>
                    <button
                      onClick={onClose}
                      className="p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors"
                    >
                      <XMarkIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>
                </div>

                {/* Content */}
                <div className="p-6 max-h-[70vh] overflow-y-auto">
                  <div className="space-y-4">
                    {/* Error Message */}
                    {error && (
                      <div className="bg-red-900/20 border border-red-600/50 rounded-lg p-4">
                        <p className="text-red-400 text-sm">{error}</p>
                      </div>
                    )}

                    {/* Loading State */}
                    {isLoading ? (
                      <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                        <p className="text-gray-400">Loading history...</p>
                      </div>
                    ) : history.length > 0 ? (
                      <div className="space-y-3">
                        {history.map((record) => (
                          <div
                            key={record.id}
                            className={`rounded-lg p-4 border ${getEventColor(
                              record.eventType
                            )} hover:border-red-600/50 transition-colors`}
                          >
                            <div className="flex items-start gap-4">
                              {/* Event Icon */}
                              <div className="flex-shrink-0 mt-1">{getEventIcon(record.eventType)}</div>

                              {/* Event Details */}
                              <div className="flex-1 min-w-0">
                                <div className="flex items-start justify-between gap-4 mb-2">
                                  <div className="flex-1">
                                    <h4 className="text-white font-medium mb-1">{record.eventTitle}</h4>
                                    <p className="text-gray-400 text-sm truncate">{record.sourceTitle}</p>
                                  </div>
                                  <div className="text-right flex-shrink-0">
                                    <p className="text-gray-400 text-xs">{formatDate(record.date)}</p>
                                    {record.quality && (
                                      <span className="inline-block mt-1 px-2 py-0.5 bg-blue-900/30 text-blue-400 text-xs rounded">
                                        {record.quality}
                                      </span>
                                    )}
                                  </div>
                                </div>

                                {/* Additional Data */}
                                {record.data && (
                                  <div className="flex flex-wrap gap-3 text-xs text-gray-500">
                                    {record.data.indexer && (
                                      <span>
                                        Indexer: <span className="text-gray-400">{record.data.indexer}</span>
                                      </span>
                                    )}
                                    {record.data.downloadClient && (
                                      <span>
                                        Client:{' '}
                                        <span className="text-gray-400">{record.data.downloadClient}</span>
                                      </span>
                                    )}
                                    {record.data.message && (
                                      <span className="text-yellow-400">{record.data.message}</span>
                                    )}
                                  </div>
                                )}

                                {/* Event Type Badge */}
                                <div className="mt-2">
                                  <span className="inline-block px-2 py-0.5 bg-gray-800/50 text-gray-300 text-xs rounded capitalize">
                                    {record.eventType}
                                  </span>
                                </div>
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                        <ClockIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                        <p className="text-gray-400 mb-2">No history available</p>
                        <p className="text-gray-500 text-sm">
                          Download and activity history will appear here
                        </p>
                      </div>
                    )}
                  </div>
                </div>

                {/* Footer */}
                <div className="px-6 py-4 bg-gray-900/50 border-t border-red-900/30 flex justify-end">
                  <button
                    onClick={onClose}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Close
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
