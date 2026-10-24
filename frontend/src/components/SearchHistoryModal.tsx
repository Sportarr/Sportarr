import { Fragment } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon } from '@heroicons/react/24/outline';
import {
  searchMessage,
  useEventSearchHistory,
  type SearchHistoryRecord,
  type SearchOutcome,
  type SearchReleaseRow,
} from '../api/searchHistory';
import { formatRelativeDate, parseAsUtc } from '../utils/timezone';
import { BADGE_AMBER, BADGE_BLUE, BADGE_GRAY, BADGE_GREEN, BADGE_RED } from '../utils/designTokens';

interface SearchHistoryModalProps {
  isOpen: boolean;
  onClose: () => void;
  eventId: number;
  eventTitle: string;
}

const outcomeBadge: Record<SearchOutcome, { label: string; className: string }> = {
  grabbed: { label: 'Grabbed', className: BADGE_GREEN },
  noResults: { label: 'Nothing found', className: BADGE_AMBER },
  nothingPassed: { label: 'All rejected', className: BADGE_RED },
  notGrabbed: { label: 'Not grabbed', className: BADGE_AMBER },
  failed: { label: 'Error', className: BADGE_RED },
};

const releaseBadge: Record<SearchReleaseRow['status'], { label: string; className: string }> = {
  grabbed: { label: 'Grabbed', className: BADGE_GREEN },
  passed: { label: 'Passed', className: BADGE_BLUE },
  rejected: { label: 'Rejected', className: BADGE_GRAY },
};

function formatSize(bytes: number): string {
  if (!bytes) return 'Unknown size';
  const gb = bytes / 1024 / 1024 / 1024;
  return gb >= 1 ? `${gb.toFixed(2)} GB` : `${(bytes / 1024 / 1024).toFixed(0)} MB`;
}

function ReleaseList({ releases }: { releases: SearchReleaseRow[] }) {
  return (
    <ul className="divide-y divide-gray-800 rounded-lg border border-gray-800 bg-black/30">
      {releases.map((release, index) => {
        const badge = releaseBadge[release.status];
        return (
          <li key={`${release.title}-${index}`} className="px-3 py-2">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <span className="min-w-0 flex-1 break-all text-sm text-white">{release.title}</span>
              <span className={badge.className}>{badge.label}</span>
            </div>
            <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-gray-400">
              <span>{release.quality || 'Unknown quality'}</span>
              <span>{formatSize(release.size)}</span>
              <span>{release.indexer}</span>
              <span>Match {release.matchScore}</span>
              {release.part && <span>{release.part}</span>}
            </div>
            {release.reasons.length > 0 && (
              <ul className="mt-1 space-y-1 text-xs">
                {release.reasons.map((reason, reasonIndex) => {
                  const plain = release.explanations?.[reasonIndex];
                  return (
                    <li key={reason}>
                      <span className="text-gray-300">{plain || reason}</span>
                      {plain && plain !== reason && <span className="block text-gray-500">{reason}</span>}
                    </li>
                  );
                })}
              </ul>
            )}
          </li>
        );
      })}
    </ul>
  );
}

function RecordCard({ record }: { record: SearchHistoryRecord }) {
  const badge = outcomeBadge[record.outcome] ?? { label: record.outcome, className: BADGE_GRAY };
  const searchedAt = parseAsUtc(record.searchedAt);
  return (
    <section className="rounded-lg border border-gray-800 bg-black/30 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium text-white" title={searchedAt.toLocaleString()}>
          {formatRelativeDate(record.searchedAt)}
        </span>
        <span className={badge.className}>{badge.label}</span>
        <span className={record.isManual ? BADGE_BLUE : BADGE_GRAY}>{record.isManual ? 'Started by you' : 'Automatic'}</span>
        {record.part && <span className={BADGE_GRAY}>{record.part}</span>}
      </div>

      <p className="mt-2 text-sm font-medium text-white">{record.headline || searchMessage(record)}</p>
      {record.help && (
        <p className={`mt-1 text-sm ${record.outcome === 'grabbed' ? 'break-all text-green-400' : 'text-gray-400'}`}>{record.help}</p>
      )}
      <p className="mt-2 text-xs text-gray-400">
        {record.releasesFound} found, {record.releasesPassed} passed the checks
        {record.qualityProfile ? `, judged with the ${record.qualityProfile} quality profile` : ''}
      </p>

      {record.reasons.length > 0 && (
        <div className="mt-3">
          <h4 className="text-xs font-medium uppercase tracking-wide text-gray-500">Why releases were rejected</h4>
          <ul className="mt-1 space-y-1">
            {record.reasons.map((reason) => (
              <li key={reason.reason} className="text-sm">
                <div className="flex items-start justify-between gap-3">
                  <span className="min-w-0 text-gray-300">{reason.reason}</span>
                  <span className="flex-none text-gray-400">{reason.count}</span>
                </div>
                {reason.help && <p className="mt-0.5 text-xs text-gray-500">{reason.help}</p>}
              </li>
            ))}
          </ul>
        </div>
      )}

      {record.indexers.length > 0 && (
        <p className="mt-3 text-xs text-gray-400">
          <span className="text-gray-500">Indexers: </span>
          {record.indexers.map((indexer, index) => (
            <Fragment key={indexer.name}>
              {index > 0 && ', '}
              <span className={indexer.status === 'ok' ? 'text-gray-300' : 'text-amber-400'}>
                {indexer.name} {indexer.status === 'ok' ? `(${indexer.results} found)` : indexer.status}
              </span>
            </Fragment>
          ))}
        </p>
      )}

      {record.releases && record.releases.length > 0 && (
        <details className="mt-3">
          <summary className="cursor-pointer text-sm font-medium text-red-400 hover:text-red-300">
            Show releases ({record.releases.length}{record.releasesFound > record.releases.length ? ` of ${record.releasesFound}` : ''})
          </summary>
          <div className="mt-2">
            <ReleaseList releases={record.releases} />
          </div>
        </details>
      )}
    </section>
  );
}

export default function SearchHistoryModal({ isOpen, onClose, eventId, eventTitle }: SearchHistoryModalProps) {
  const { data, isLoading, isError } = useEventSearchHistory(eventId, isOpen);

  return (
    <Transition appear show={isOpen} as={Fragment} unmount>
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

        <div className="fixed inset-0 overflow-hidden">
          <div className="flex min-h-full items-center justify-center p-4 text-center">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="mx-2 max-h-[calc(100dvh-2rem)] w-full max-w-2xl transform overflow-y-auto overscroll-contain rounded-lg border border-gray-700 bg-gray-900 text-left align-middle shadow-xl transition-all md:mx-4">
                <div className="flex items-center justify-between border-b border-gray-700 p-3 md:p-4">
                  <div className="mr-2 min-w-0 flex-1">
                    <Dialog.Title className="text-base font-medium text-white md:text-lg">Search history</Dialog.Title>
                    <p className="mt-1 truncate text-xs text-gray-400 md:text-sm">{eventTitle}</p>
                  </div>
                  <button
                    type="button"
                    onClick={onClose}
                    className="flex-shrink-0 rounded-lg p-2 text-gray-400 transition-colors hover:text-white"
                    aria-label="Close"
                  >
                    <XMarkIcon className="h-5 w-5 md:h-6 md:w-6" />
                  </button>
                </div>

                <div className="space-y-3 p-3 md:p-4">
                  {isLoading && <p className="text-sm text-gray-400">Loading searches...</p>}
                  {isError && <p className="text-sm text-red-400">Could not load the search history.</p>}
                  {data && data.length === 0 && (
                    <p className="text-sm text-gray-400">No searches are recorded for this event yet.</p>
                  )}
                  {data?.map((record) => <RecordCard key={record.id} record={record} />)}
                  {data && data.length > 0 && (
                    <p className="text-xs text-gray-500">
                      The last 5 searches are kept for 30 days. The newest search of each part keeps its release list.
                    </p>
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
