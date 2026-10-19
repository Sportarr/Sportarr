import { useState } from 'react';
import { MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import { describeOutcome, describeSearch, type SearchHistoryRecord } from '../api/searchHistory';
import { formatRelativeDate } from '../utils/timezone';
import SearchHistoryModal from './SearchHistoryModal';

interface LastSearchLineProps {
  record: SearchHistoryRecord;
  eventTitle: string;
  className?: string;
  /** Show only the outcome, for narrow table cells. The full line is in the tooltip. */
  compact?: boolean;
}

/**
 * One line that says what the last search did, for example "Searched 2h
 * ago: every release was rejected. The most common reason was quality not
 * allowed (300 of 412)". A click opens the search history for the event.
 */
export default function LastSearchLine({ record, eventTitle, className = '', compact = false }: LastSearchLineProps) {
  const [open, setOpen] = useState(false);
  const when = formatRelativeDate(record.searchedAt).toLowerCase();
  const part = record.part ? ` (${record.part})` : '';
  const full = `Searched ${when}${part}: ${describeSearch(record)}`;
  const tone = record.outcome === 'grabbed'
    ? 'text-green-400 hover:text-green-300'
    : record.outcome === 'nothingPassed' || record.outcome === 'failed'
      ? 'text-red-400 hover:text-red-300'
      : 'text-amber-400 hover:text-amber-300';
  return (
    <>
      <button
        type="button"
        onClick={(event) => {
          event.stopPropagation();
          setOpen(true);
        }}
        className={`inline-flex min-w-0 max-w-full items-start gap-1 text-left text-xs transition-colors ${tone} ${className}`}
        title={compact ? `${full}. Click for the search history.` : 'Show the search history for this event'}
      >
        <MagnifyingGlassIcon className="mt-px h-3.5 w-3.5 flex-none" aria-hidden="true" />
        <span className="min-w-0 break-words">
          {compact ? `${describeOutcome(record)} ${when}${part}` : full}
        </span>
      </button>
      <SearchHistoryModal isOpen={open} onClose={() => setOpen(false)} eventId={record.eventId} eventTitle={eventTitle} />
    </>
  );
}
