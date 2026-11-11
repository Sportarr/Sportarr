import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import {
  ArrowLeftIcon,
  ArrowPathIcon,
  ArrowRightIcon,
  Bars3Icon,
  CalendarIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  ExclamationTriangleIcon,
  FolderIcon,
  MagnifyingGlassIcon,
  XMarkIcon,
} from '@heroicons/react/24/outline';
import { formatEventDate } from '../utils/timezone';
import { moveItem } from '../utils/moveItem';
import { apiGet } from '../utils/api';
import { fetchEventParts, useLeagueSeasonEvents } from '../hooks/useLeagueSeasonEvents';
import {
  choosePart,
  pairFilesToEvents,
  sortEventsOldestFirst,
  type BatchFile,
  type BatchPair,
  type BatchPart,
} from '../utils/seasonBatchMatch';

export interface SeasonBatchMatchResult {
  leagueId: number;
  leagueName?: string;
  season: string;
  pairs: BatchPair[];
}

interface SeasonBatchMatchModalProps {
  isOpen: boolean;
  onClose: () => void;
  /** The selected files, in numeric-aware path order. */
  files: BatchFile[];
  onConfirm: (result: SeasonBatchMatchResult) => void | Promise<void>;
}

type Step = 'events' | 'preview';
type Direction = 'up' | 'down';

const EVENT_LIMIT = 500;

const MOVE_BUTTON =
  'p-1.5 rounded text-gray-400 hover:text-white hover:bg-gray-700 transition-colors disabled:opacity-30 disabled:cursor-not-allowed';

/**
 * Match a batch of files to the events of one season. The user picks the
 * league and season once, ticks the events, and the files pair with the
 * events by order. The preview lets the user reorder the files before the
 * matches go back to the import page.
 */
export default function SeasonBatchMatchModal({ isOpen, onClose, files, onConfirm }: SeasonBatchMatchModalProps) {
  const {
    leagues, seasons, events,
    loadingLeagues, loadingSeasons, loadingEvents,
    loadLeagues, loadSeasons, loadEvents, cancelEvents, clearSeasons,
    setEvents,
  } = useLeagueSeasonEvents();

  const [step, setStep] = useState<Step>('events');
  const [leagueId, setLeagueId] = useState<number | null>(null);
  const [season, setSeason] = useState<string | null>(null);
  const [filter, setFilter] = useState('');
  const [ticked, setTicked] = useState<Set<number>>(new Set());
  const [order, setOrder] = useState<BatchFile[]>([]);
  const [eventParts, setEventParts] = useState<Map<number, BatchPart[]> | undefined>(undefined);
  const [partChoices, setPartChoices] = useState<Map<number, number[]>>(new Map());
  const [loadingParts, setLoadingParts] = useState(false);
  const [partsError, setPartsError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [dropIndex, setDropIndex] = useState<number | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const [pendingFocus, setPendingFocus] = useState<{ filePath: string; direction: Direction } | null>(null);
  const moveButtons = useRef(new Map<string, HTMLButtonElement>());
  const partsSeq = useRef(0);

  // Start fresh on every open, with the files in the order of the import page.
  useEffect(() => {
    if (!isOpen) return;
    setStep('events');
    setLeagueId(null);
    setSeason(null);
    setFilter('');
    setTicked(new Set());
    setOrder(files);
    setEventParts(undefined);
    setPartsError(null);
    setConfirming(false);
    setAnnouncement('');
    clearSeasons();
    setEvents([]);
    loadLeagues();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  useEffect(() => {
    setSeason(null);
    setTicked(new Set());
    setEvents([]);
    if (leagueId) loadSeasons(leagueId);
    else clearSeasons();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    setTicked(new Set());
    setFilter('');
    if (leagueId && season) {
      loadEvents(leagueId, season, '', EVENT_LIMIT);
    } else {
      cancelEvents();
      setEvents([]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId, season]);

  // Parts belong to the ticked events. A new set of events needs them again.
  useEffect(() => {
    partsSeq.current += 1;
    setEventParts(undefined);
    setPartChoices(new Map());
    setPartsError(null);
    setLoadingParts(false);
  }, [ticked]);

  // Keep focus on the moved row. At the top or the bottom of the list the
  // pressed button is disabled, so the other move button gets the focus.
  useEffect(() => {
    if (!pendingFocus) return;
    const { filePath, direction } = pendingFocus;
    const other: Direction = direction === 'up' ? 'down' : 'up';
    const pressed = moveButtons.current.get(`${filePath}:${direction}`);
    const target = pressed && !pressed.disabled ? pressed : moveButtons.current.get(`${filePath}:${other}`);
    target?.focus();
    setPendingFocus(null);
  }, [order, pendingFocus]);

  const sortedEvents = useMemo(() => sortEventsOldestFirst(events), [events]);
  const visibleEvents = useMemo(() => {
    const term = filter.trim().toLowerCase();
    if (!term) return sortedEvents;
    return sortedEvents.filter(event => event.title.toLowerCase().includes(term));
  }, [sortedEvents, filter]);
  const tickedEvents = useMemo(() => sortedEvents.filter(event => ticked.has(event.id)), [sortedEvents, ticked]);
  const pairing = useMemo(
    () => pairFilesToEvents(order, tickedEvents, eventParts, partChoices),
    [order, tickedEvents, eventParts, partChoices],
  );

  const league = leagues.find(l => l.id === leagueId);
  const canContinue = !loadingParts && (pairing.ok || pairing.reason === 'partsUnknown');

  const toggleEvent = (id: number) => {
    setTicked(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const tickVisible = (on: boolean) => {
    setTicked(prev => {
      const next = new Set(prev);
      for (const event of visibleEvents) {
        if (on) next.add(event.id);
        else next.delete(event.id);
      }
      return next;
    });
  };

  // Only fighting sports name their parts. One request per sport tells which
  // sports do, so events of other sports need no request of their own.
  const loadTickedParts = async (): Promise<Map<number, BatchPart[]> | null> => {
    const seq = ++partsSeq.current;
    setLoadingParts(true);
    setPartsError(null);
    try {
      const sports = [...new Set(tickedEvents.map(event => event.sport))];
      const named = new Set<string>();
      for (const sport of sports) {
        const response = await apiGet(`/api/library/parts/${encodeURIComponent(sport)}`);
        if (!response.ok) throw new Error('Could not load the parts for this sport.');
        const data = await response.json();
        if (Array.isArray(data.parts) && data.parts.length > 0) named.add(sport);
      }
      const map = new Map<number, BatchPart[]>();
      await Promise.all(tickedEvents.map(async event => {
        map.set(event.id, named.has(event.sport) ? await fetchEventParts(event.id) : []);
      }));
      if (seq !== partsSeq.current) return null;
      setEventParts(map);
      return map;
    } catch (err) {
      if (seq === partsSeq.current) {
        setPartsError(err instanceof Error ? err.message : 'Could not load the event parts.');
      }
      return null;
    } finally {
      if (seq === partsSeq.current) setLoadingParts(false);
    }
  };

  // The preview needs the parts of the ticked events. With one file per
  // event they only fill the part picker, so a failed load does not stop
  // the preview. Each file then takes its part from the file name. With
  // more files per event, the parts decide the pairing.
  const handleNext = async () => {
    if (!pairing.ok && pairing.reason !== 'partsUnknown') return;
    const map = eventParts ?? await loadTickedParts();
    if (pairing.ok && pairing.filesPerEvent === 1) {
      setStep('preview');
      return;
    }
    if (map && pairFilesToEvents(order, tickedEvents, map).ok) setStep('preview');
  };

  const move = (from: number, to: number, direction?: Direction) => {
    const next = moveItem(order, from, to);
    if (next === order) return;
    const moved = order[from];
    setAnnouncement(`${moved.fileName} moved to position ${to + 1} of ${order.length}.`);
    if (direction) setPendingFocus({ filePath: moved.filePath, direction });
    setOrder(next);
  };

  const setMoveButton = (key: string) => (element: HTMLButtonElement | null) => {
    if (element) moveButtons.current.set(key, element);
    else moveButtons.current.delete(key);
  };

  const duplicateTitles = pairing.ok
    ? tickedEvents.filter(event => pairing.duplicateEvents.includes(event.id)).map(event => event.title)
    : [];

  const handleConfirm = async () => {
    if (!pairing.ok || pairing.duplicateEvents.length > 0 || !leagueId || !season) return;
    setConfirming(true);
    try {
      await onConfirm({ leagueId, leagueName: league?.name, season, pairs: pairing.pairs });
      onClose();
    } finally {
      setConfirming(false);
    }
  };

  const summary = pairing.ok
    ? pairing.filesPerEvent === 1
      ? `${order.length} files pair with ${tickedEvents.length} events, one file each.`
      : `${order.length} files pair with ${tickedEvents.length} events. Each event gets ${pairing.filesPerEvent} files as parts.`
    : null;

  return (
    <Transition show={isOpen} as={Fragment}>
      <Dialog onClose={onClose} className="relative z-50">
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-200" enterFrom="opacity-0" enterTo="opacity-100"
          leave="ease-in duration-150" leaveFrom="opacity-100" leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/70" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-hidden">
          <div className="flex min-h-full items-center justify-center p-2 sm:p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-200" enterFrom="opacity-0 scale-95" enterTo="opacity-100 scale-100"
              leave="ease-in duration-150" leaveFrom="opacity-100 scale-100" leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-3xl max-h-[calc(100dvh-1rem)] flex flex-col rounded-xl bg-gray-900 border border-gray-700 shadow-xl">
                <div className="px-4 sm:px-6 py-4 border-b border-gray-700 flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <Dialog.Title className="text-lg font-semibold text-white">Match season</Dialog.Title>
                    <p className="text-sm text-gray-400">
                      {step === 'events'
                        ? `Pick the league, the season and the events for ${order.length} selected file${order.length === 1 ? '' : 's'}.`
                        : pairing.ok && pairing.pairs.some(pair => pair.partOptions.length > 0)
                          ? 'Check the pairs. Move a file up or down to change its event, or pick another part.'
                          : 'Check the pairs. Move a file up or down to change its event.'}
                    </p>
                  </div>
                  <button onClick={onClose} className="text-gray-400 hover:text-white transition-colors" aria-label="Close">
                    <XMarkIcon className="w-6 h-6" />
                  </button>
                </div>

                <div className="flex-1 min-h-0 overflow-y-auto p-4 sm:p-6 space-y-4">
                  {step === 'events' ? (
                    <>
                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                        <div>
                          <label htmlFor="season-match-league" className="text-sm font-medium text-gray-300 mb-1.5 flex items-center gap-2">
                            <FolderIcon className="w-4 h-4" />
                            League
                          </label>
                          <select
                            id="season-match-league"
                            value={leagueId ?? ''}
                            onChange={(e) => setLeagueId(e.target.value ? Number(e.target.value) : null)}
                            disabled={loadingLeagues}
                            className="w-full px-3 py-2.5 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                          >
                            <option value="">Select a league...</option>
                            {leagues.map(l => (
                              <option key={l.id} value={l.id}>{l.name} ({l.sport})</option>
                            ))}
                          </select>
                        </div>
                        <div>
                          <label htmlFor="season-match-season" className="text-sm font-medium text-gray-300 mb-1.5 flex items-center gap-2">
                            <CalendarIcon className="w-4 h-4" />
                            Season
                          </label>
                          <select
                            id="season-match-season"
                            value={season ?? ''}
                            onChange={(e) => setSeason(e.target.value || null)}
                            disabled={!leagueId || loadingSeasons}
                            className="w-full px-3 py-2.5 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                          >
                            <option value="">Select a season...</option>
                            {seasons.map(s => (
                              <option key={s} value={s}>{s}</option>
                            ))}
                          </select>
                        </div>
                      </div>

                      {leagueId && season && (
                        <div>
                          <div className="flex flex-col sm:flex-row sm:items-center gap-2 mb-2">
                            <div className="relative flex-1">
                              <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 pointer-events-none" />
                              <input
                                type="text"
                                value={filter}
                                onChange={(e) => setFilter(e.target.value)}
                                placeholder="Filter events, for example Race or Qualifying"
                                aria-label="Filter events"
                                className="w-full pl-9 pr-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm placeholder-gray-500 focus:outline-none focus:border-red-600"
                              />
                            </div>
                            <div className="flex items-center gap-2">
                              <button
                                type="button"
                                onClick={() => tickVisible(true)}
                                disabled={visibleEvents.length === 0}
                                className="px-3 py-1.5 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors disabled:opacity-50"
                              >
                                Select all
                              </button>
                              <button
                                type="button"
                                onClick={() => tickVisible(false)}
                                disabled={visibleEvents.length === 0}
                                className="px-3 py-1.5 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors disabled:opacity-50"
                              >
                                Select none
                              </button>
                            </div>
                          </div>
                          <p className="text-xs text-gray-500 mb-2">
                            Oldest first. {ticked.size} of {sortedEvents.length} ticked
                            {filter.trim() ? `, ${visibleEvents.length} shown` : ''}.
                          </p>
                          {sortedEvents.length >= EVENT_LIMIT && (
                            <p className="text-xs text-amber-300 mb-2" role="status">
                              The list stops at {EVENT_LIMIT} events. The newest events come first, so older events of this season can be missing.
                            </p>
                          )}

                          {loadingEvents ? (
                            <div className="p-6 text-center text-gray-400">
                              <ArrowPathIcon className="w-5 h-5 animate-spin mx-auto mb-2" />
                              Loading events...
                            </div>
                          ) : sortedEvents.length === 0 ? (
                            <p className="p-4 text-center text-gray-400 bg-gray-800 rounded-lg border border-gray-700">
                              This season has no events yet.
                            </p>
                          ) : (
                            <ul className="max-h-[40vh] overflow-y-auto border border-gray-700 rounded-lg divide-y divide-gray-800" aria-label="Season events">
                              {visibleEvents.map(event => (
                                <li key={event.id}>
                                  <label className="flex items-center gap-3 px-3 py-2.5 cursor-pointer hover:bg-gray-800 transition-colors">
                                    <input
                                      type="checkbox"
                                      checked={ticked.has(event.id)}
                                      onChange={() => toggleEvent(event.id)}
                                      className="w-5 h-5 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500 flex-shrink-0"
                                    />
                                    <span className="flex-1 min-w-0">
                                      <span className="block text-sm text-white break-words sm:truncate">{event.title}</span>
                                      <span className="block text-xs text-gray-400">
                                        {formatEventDate(event, null)}
                                        {event.episodeNumber ? ` • Episode ${event.episodeNumber}` : ''}
                                      </span>
                                    </span>
                                    {event.hasFile && (
                                      <span className="text-xs bg-yellow-900/50 text-yellow-400 px-2 py-0.5 rounded flex-shrink-0">Has File</span>
                                    )}
                                  </label>
                                </li>
                              ))}
                              {visibleEvents.length === 0 && (
                                <li className="px-3 py-4 text-sm text-center text-gray-400">No events match the filter.</li>
                              )}
                            </ul>
                          )}
                        </div>
                      )}

                      {leagueId && season && ticked.size > 0 && (
                        summary ? (
                          <p className="text-sm text-green-400" role="status">{summary}</p>
                        ) : !pairing.ok && pairing.reason !== 'partsUnknown' ? (
                          <p className="text-sm text-amber-300 flex items-start gap-2" role="alert">
                            <ExclamationTriangleIcon className="w-5 h-5 flex-shrink-0" />
                            <span>{pairing.message}</span>
                          </p>
                        ) : (
                          <p className="text-sm text-gray-300" role="status">
                            {order.length} files pair with {tickedEvents.length} events. Each event gets {order.length / tickedEvents.length} files as parts.
                          </p>
                        )
                      )}
                      {partsError && (
                        <p className="text-sm text-red-400" role="alert">{partsError}</p>
                      )}
                    </>
                  ) : (
                    pairing.ok && (
                      <div>
                        <p className="text-sm text-gray-300 mb-3">
                          {league?.name} • Season {season}. {summary}
                        </p>
                        {partsError && (
                          <p className="text-sm text-amber-300 mb-3" role="status">
                            The event parts could not load. Each file takes its part from the file name.
                          </p>
                        )}
                        <p className="sr-only" aria-live="polite">{announcement}</p>
                        <ol className="space-y-2" aria-label="File and event pairs">
                          {pairing.pairs.map((pair, index) => {
                            const startsGroup = pairing.filesPerEvent > 1 && index > 0 && index % pairing.filesPerEvent === 0;
                            const name = pair.file.fileName;
                            return (
                              <li
                                key={pair.file.filePath}
                                draggable
                                onDragStart={(e) => {
                                  e.dataTransfer.effectAllowed = 'move';
                                  setDragIndex(index);
                                }}
                                onDragOver={(e) => {
                                  if (dragIndex === null) return;
                                  e.preventDefault();
                                  setDropIndex(index);
                                }}
                                onDragLeave={(e) => {
                                  // Moving onto a child element also fires this event.
                                  // Clear the marker only when the pointer leaves the row.
                                  if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDropIndex(null);
                                }}
                                onDrop={(e) => {
                                  e.preventDefault();
                                  if (dragIndex !== null) move(dragIndex, index);
                                  setDragIndex(null);
                                  setDropIndex(null);
                                }}
                                onDragEnd={() => {
                                  setDragIndex(null);
                                  setDropIndex(null);
                                }}
                                className={`flex items-center gap-2 sm:gap-3 p-2 sm:p-3 bg-gray-800/50 border rounded-lg transition-colors ${
                                  dropIndex === index && dragIndex !== index ? 'border-blue-500' : 'border-gray-700'
                                } ${dragIndex === index ? 'opacity-50 border-dashed' : ''} ${startsGroup ? 'mt-4' : ''}`}
                              >
                                {/* Touch screens cannot drag, so phones use the move buttons. */}
                                <span className="hidden sm:inline-flex text-gray-500 cursor-grab flex-shrink-0" title="Drag to reorder">
                                  <Bars3Icon className="w-4 h-4" />
                                </span>
                                <span className="text-gray-400 text-sm font-semibold w-6 text-right flex-shrink-0" aria-hidden="true">
                                  {index + 1}.
                                </span>
                                <div className="flex-1 min-w-0">
                                  <p className="text-white text-sm font-medium break-all sm:break-normal sm:truncate" title={name}>{name}</p>
                                  <p className="text-sm text-gray-400 flex items-center gap-1 min-w-0">
                                    <ArrowRightIcon className="w-3.5 h-3.5 flex-shrink-0" />
                                    <span className="text-green-400 break-words sm:truncate">{pair.eventTitle}</span>

                                  </p>
                                  {pair.partOptions.length > 0 && (
                                    <select
                                      value={pair.partNumber ?? 0}
                                      onChange={(e) => setPartChoices(prev =>
                                        choosePart(prev, pairing.pairs, pair.eventId, pair.slot, Number(e.target.value)))}
                                      aria-label={`Part for ${name}`}
                                      className={`mt-1 px-2 py-1 bg-gray-800 border rounded text-sm text-white focus:outline-none focus:ring-2 focus:ring-red-500 ${
                                        pairing.duplicateEvents.includes(pair.eventId) ? 'border-amber-500' : 'border-gray-600'
                                      }`}
                                    >
                                      {pairing.filesPerEvent === 1 && (
                                        <option value={0}>From the file name</option>
                                      )}
                                      {pair.partOptions.map(option => (
                                        <option key={option.partNumber} value={option.partNumber}>{option.name}</option>
                                      ))}
                                    </select>
                                  )}
                                  {(pair.replacesFile || pair.alreadyImported) && (
                                    <div className="mt-1 flex flex-wrap gap-1.5">
                                      {pair.replacesFile && (
                                        <span className="text-xs bg-amber-900/50 text-amber-300 px-2 py-0.5 rounded">
                                          Replaces the current file
                                        </span>
                                      )}
                                      {pair.alreadyImported && (
                                        <span className="text-xs bg-sky-900/50 text-sky-300 px-2 py-0.5 rounded">
                                          Already imported
                                        </span>
                                      )}
                                    </div>
                                  )}
                                </div>
                                <div className="flex items-center flex-shrink-0">
                                  <button
                                    type="button"
                                    ref={setMoveButton(`${pair.file.filePath}:up`)}
                                    onClick={() => move(index, index - 1, 'up')}
                                    disabled={index === 0}
                                    className={MOVE_BUTTON}
                                    aria-label={`Move ${name} up`}
                                    title="Move up"
                                  >
                                    <ChevronUpIcon className="w-5 h-5" />
                                  </button>
                                  <button
                                    type="button"
                                    ref={setMoveButton(`${pair.file.filePath}:down`)}
                                    onClick={() => move(index, index + 1, 'down')}
                                    disabled={index === pairing.pairs.length - 1}
                                    className={MOVE_BUTTON}
                                    aria-label={`Move ${name} down`}
                                    title="Move down"
                                  >
                                    <ChevronDownIcon className="w-5 h-5" />
                                  </button>
                                </div>
                              </li>
                            );
                          })}
                        </ol>
                        {duplicateTitles.length > 0 && (
                          <p className="mt-3 text-sm text-amber-300 flex items-start gap-2" role="alert">
                            <ExclamationTriangleIcon className="w-5 h-5 flex-shrink-0" />
                            <span>
                              Each file of an event needs a different part. Change the parts of {duplicateTitles.join(', ')}.
                            </span>
                          </p>
                        )}
                      </div>
                    )
                  )}
                </div>

                <div className="px-4 sm:px-6 py-3 border-t border-gray-700 flex items-center justify-between gap-3">
                  {step === 'preview' ? (
                    <button
                      onClick={() => setStep('events')}
                      className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors flex items-center gap-2"
                    >
                      <ArrowLeftIcon className="w-4 h-4" />
                      Back
                    </button>
                  ) : (
                    <button onClick={onClose} className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors">
                      Cancel
                    </button>
                  )}
                  {step === 'events' ? (
                    <button
                      onClick={handleNext}
                      disabled={!canContinue}
                      className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      {loadingParts && <ArrowPathIcon className="w-4 h-4 animate-spin" />}
                      Preview pairs
                      <ArrowRightIcon className="w-4 h-4" />
                    </button>
                  ) : (
                    <button
                      onClick={handleConfirm}
                      disabled={!pairing.ok || pairing.duplicateEvents.length > 0 || confirming}
                      className="px-4 py-2 bg-green-600 hover:bg-green-700 text-white rounded-lg transition-colors flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      {confirming && <ArrowPathIcon className="w-4 h-4 animate-spin" />}
                      Match {order.length} file{order.length === 1 ? '' : 's'}
                    </button>
                  )}
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
