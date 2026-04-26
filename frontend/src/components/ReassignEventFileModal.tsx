import { Fragment, useEffect, useMemo, useState } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { ArrowsRightLeftIcon, MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../api/client';

interface EventSearchResult {
  id: number;
  title: string;
  sport: string | null;
  leagueId: number | null;
  leagueName: string | null;
  eventDate: string;
  hasFile: boolean;
}

interface ReassignEventFileModalProps {
  isOpen: boolean;
  onClose: () => void;
  fileId: number;
  fileName: string;
  currentEventId: number;
  currentEventTitle: string;
  onSuccess?: () => void;
}

export default function ReassignEventFileModal({
  isOpen,
  onClose,
  fileId,
  fileName,
  currentEventId,
  currentEventTitle,
  onSuccess,
}: ReassignEventFileModalProps) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<EventSearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [selected, setSelected] = useState<EventSearchResult | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Reset state every time the modal opens for a new file
  useEffect(() => {
    if (isOpen) {
      setQuery('');
      setResults([]);
      setSelected(null);
      setSubmitting(false);
    }
  }, [isOpen, fileId]);

  // Debounced server-side search. The endpoint returns the most recent events
  // when the query is empty, which doubles as a useful default list.
  useEffect(() => {
    if (!isOpen) return;

    const timer = setTimeout(async () => {
      setSearching(true);
      try {
        const response = await apiClient.get<EventSearchResult[]>('/events/search', {
          params: {
            q: query.trim() || undefined,
            limit: 50,
            excludeEventId: currentEventId,
          },
        });
        setResults(response.data);
      } catch (err) {
        console.error('[Reassign] search failed', err);
      } finally {
        setSearching(false);
      }
    }, 250);

    return () => clearTimeout(timer);
  }, [query, isOpen, currentEventId]);

  const handleConfirm = async () => {
    if (!selected) return;
    setSubmitting(true);
    try {
      await apiClient.post(`/events/${currentEventId}/files/${fileId}/reassign`, {
        eventId: selected.id,
      });
      toast.success(`File reassigned to "${selected.title}"`);
      onSuccess?.();
      onClose();
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } }).response?.data?.error ||
        'Failed to reassign file';
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  const formattedFileName = useMemo(() => {
    if (!fileName) return '';
    const base = fileName.split(/[\\/]/).pop() || fileName;
    return base.length > 80 ? `${base.slice(0, 77)}…` : base;
  }, [fileName]);

  return (
    <Transition appear show={isOpen} as={Fragment}>
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-200"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-150"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/80" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-200"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-150"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-2xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-blue-900/30 text-left shadow-xl transition-all">
                <div className="p-4 md:p-6">
                  <div className="flex items-start gap-3">
                    <div className="flex-shrink-0 w-10 h-10 rounded-full bg-blue-600/20 flex items-center justify-center">
                      <ArrowsRightLeftIcon className="w-5 h-5 text-blue-400" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <Dialog.Title as="h3" className="text-base md:text-lg font-bold text-white mb-1">
                        Reassign file to a different event
                      </Dialog.Title>
                      <p className="text-xs md:text-sm text-gray-400">
                        The file will be physically moved to the new event's folder and the
                        mapping updated. The original file is not deleted.
                      </p>
                      <div className="mt-2 text-xs text-gray-500">
                        <div>
                          <span className="text-gray-400">File:</span>{' '}
                          <span className="text-gray-300 font-mono break-all">{formattedFileName}</span>
                        </div>
                        <div>
                          <span className="text-gray-400">Currently linked to:</span>{' '}
                          <span className="text-gray-300">{currentEventTitle}</span>
                        </div>
                      </div>
                    </div>
                  </div>

                  <div className="mt-4">
                    <label className="block text-xs font-medium text-gray-400 mb-1">
                      Search for the correct event
                    </label>
                    <div className="relative">
                      <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                      <input
                        type="text"
                        value={query}
                        onChange={(e) => setQuery(e.target.value)}
                        placeholder="Search by event title or league..."
                        autoFocus
                        className="w-full pl-9 pr-3 py-2 bg-gray-900 border border-gray-700 rounded text-sm text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
                      />
                    </div>
                  </div>

                  <div className="mt-3 max-h-80 overflow-y-auto border border-gray-800 rounded">
                    {searching && results.length === 0 && (
                      <div className="px-3 py-6 text-center text-sm text-gray-500">Searching…</div>
                    )}
                    {!searching && results.length === 0 && (
                      <div className="px-3 py-6 text-center text-sm text-gray-500">
                        No events found. Try a different search.
                      </div>
                    )}
                    {results.map((evt) => {
                      const date = new Date(evt.eventDate);
                      const dateStr = isNaN(date.getTime())
                        ? evt.eventDate
                        : date.toLocaleDateString();
                      const isSelected = selected?.id === evt.id;
                      return (
                        <button
                          key={evt.id}
                          type="button"
                          onClick={() => setSelected(evt)}
                          className={`w-full text-left px-3 py-2 border-b border-gray-800 last:border-b-0 transition-colors ${
                            isSelected
                              ? 'bg-blue-600/20 border-blue-700/40'
                              : 'hover:bg-gray-800/60'
                          }`}
                        >
                          <div className="flex items-center justify-between gap-3">
                            <div className="min-w-0 flex-1">
                              <div className="text-sm text-white truncate">{evt.title}</div>
                              <div className="text-xs text-gray-500 truncate">
                                {evt.leagueName ?? evt.sport ?? 'Unknown'} · {dateStr}
                              </div>
                            </div>
                            {evt.hasFile && (
                              <span className="text-xs text-yellow-400 flex-shrink-0">
                                Has file
                              </span>
                            )}
                          </div>
                        </button>
                      );
                    })}
                  </div>
                </div>

                <div className="border-t border-blue-900/30 p-3 md:p-4 bg-black/30 flex gap-2 md:gap-3 justify-end">
                  <button
                    onClick={onClose}
                    disabled={submitting}
                    className="px-3 md:px-4 py-1.5 md:py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={handleConfirm}
                    disabled={submitting || !selected}
                    className="px-3 md:px-4 py-1.5 md:py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {submitting ? 'Moving file…' : 'Reassign & move file'}
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
