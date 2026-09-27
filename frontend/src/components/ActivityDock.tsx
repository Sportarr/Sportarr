import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { ArrowDownTrayIcon, ArrowRightIcon, CheckCircleIcon, ExclamationTriangleIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import { getRefetchIntervalWithBackoff } from '../utils/queryBackoff';

interface QueueItem {
  id: number;
  title: string;
  event?: { title: string } | null;
  status: number;
  progress: number;
  downloaded: number;
  size: number;
  added: string;
  lastProgressAt?: string | null;
  lastUpdate?: string | null;
  timeRemaining?: string | null;
}

type DockState = 'downloading' | 'queued' | 'importing' | 'attention' | 'imported';
interface ImportedNotice { item: QueueItem; count: number }

function ageInMinutes(value: string | null | undefined, now: number): number | null {
  if (!value) return null;
  const timestamp = new Date(value).getTime();
  return Number.isFinite(timestamp) ? Math.max(0, Math.floor((now - timestamp) / 60000)) : null;
}

function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '0 MB';
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
  return `${Math.round(bytes / 1024 ** 2)} MB`;
}

function formatRemaining(value: string | null | undefined): string | null {
  if (!value) return null;
  const match = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?$/.exec(value);
  if (!match) return null;
  const seconds = Number(match[1] ?? 0) * 86400 + Number(match[2]) * 3600
    + Number(match[3]) * 60 + Number(match[4]);
  if (seconds <= 0) return null;
  if (seconds >= 3600) return `${Math.ceil(seconds / 3600)}h left`;
  return `${Math.ceil(seconds / 60)}m left`;
}

function selectItem(queue: QueueItem[], now: number): { item: QueueItem; state: DockState } | null {
  const recent = [...queue].sort((a, b) => b.id - a.id);
  const importing = recent.find(item => item.status === 6 &&
    (ageInMinutes(item.lastUpdate, now) ?? Infinity) < 10);
  if (importing) return { item: importing, state: 'importing' };
  const downloading = recent.find(item => item.status === 1 &&
    (ageInMinutes(item.lastProgressAt ?? item.added, now) ?? Infinity) < 10);
  if (downloading) return { item: downloading, state: 'downloading' };
  const queued = recent.find(item => item.status === 0 &&
    (ageInMinutes(item.added, now) ?? Infinity) < 10);
  if (queued) return { item: queued, state: 'queued' };
  const attention = recent.find(item => item.status === 1 || item.status === 0 || item.status === 6);
  return attention ? { item: attention, state: 'attention' } : null;
}

export default function ActivityDock() {
  const { data: queue } = useQuery({
    queryKey: ['activityDockQueue'],
    queryFn: async () => (await apiClient.get<QueueItem[]>('/queue')).data,
    refetchInterval: query => getRefetchIntervalWithBackoff(5000, query.state.fetchFailureCount),
  });
  const seen = useRef(new Map<number, number>());
  const importedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [imported, setImported] = useState<ImportedNotice | null>(null);
  const [now, setNow] = useState(Date.now());

  useEffect(() => {
    const interval = setInterval(() => setNow(Date.now()), 60000);
    return () => {
      clearInterval(interval);
      if (importedTimer.current) clearTimeout(importedTimer.current);
    };
  }, []);

  useEffect(() => {
    if (!queue) return;
    const completed = queue.filter(item => item.status === 7 &&
      seen.current.has(item.id) && seen.current.get(item.id) !== 7);
    seen.current = new Map(queue.map(item => [item.id, item.status]));
    if (completed.length === 0) return;
    const notice = { item: completed.sort((a, b) => b.id - a.id)[0], count: completed.length };
    setImported(notice);
    if (importedTimer.current) clearTimeout(importedTimer.current);
    importedTimer.current = setTimeout(() => setImported(current => current?.item.id === notice.item.id ? null : current), 5000);
  }, [queue]);

  const selected = imported ? { item: imported.item, state: 'imported' as DockState }
    : selectItem(queue ?? [], now);
  if (!selected) return null;

  const { item, state } = selected;
  const progress = Math.min(100, Math.max(0, Number.isFinite(item.progress) ? item.progress : 0));
  const age = ageInMinutes(item.lastProgressAt, now);
  const warning = state === 'attention';
  const complete = state === 'imported';
  const label = warning ? 'Needs attention' : complete ? 'Completed' : 'Now working';
  const status = warning
    ? item.status === 6 ? 'Import taking longer than expected'
      : item.status === 0 ? 'Waiting longer than expected'
      : (age !== null && age >= 10 ? `No progress for ${age >= 60 ? `${Math.floor(age / 60)}h` : `${age}m`}` : 'No recent progress')
    : complete ? 'Imported to library'
    : state === 'importing' ? 'Importing to library'
    : state === 'queued' ? 'Waiting for download client'
    : `Downloading ${Math.round(progress)}%`;
  const remaining = state === 'downloading' ? formatRemaining(item.timeRemaining) : null;
  const size = state === 'downloading' && item.size > 0
    ? `${formatSize(item.downloaded)} / ${formatSize(item.size)}` : null;

  return (
    <section
      aria-label="Activity dock"
      className={`mx-3 my-2 rounded-xl border ${warning ? 'border-amber-700/60' : 'border-red-900/50'} bg-gray-900/95 p-3 text-gray-100 shadow-lg shadow-black/30`}
    >
      <div className="flex items-center gap-2 text-[10px] font-semibold uppercase tracking-[0.13em]">
        {warning ? <ExclamationTriangleIcon className="h-4 w-4 text-amber-400" />
          : complete ? <CheckCircleIcon className="h-4 w-4 text-green-400" />
          : <ArrowDownTrayIcon className="h-4 w-4 text-red-400" />}
        <span className={warning ? 'text-amber-300' : complete ? 'text-green-400' : 'text-red-400'}>{label}</span>
      </div>
      <div className="mt-1.5 truncate text-sm font-semibold" title={item.event?.title || item.title}>
        {complete && imported && imported.count > 1 ? `${imported.count} events imported` : item.event?.title || item.title}
      </div>
      <div className={`mt-0.5 text-xs ${warning ? 'text-amber-300' : 'text-gray-300'}`}>{status}</div>
      {(state === 'downloading' || warning && item.status === 1) && (
        <div
          role="progressbar"
          aria-label="Download progress"
          aria-valuenow={progress}
          aria-valuemin={0}
          aria-valuemax={100}
          className="mt-2 h-1.5 overflow-hidden rounded-full bg-gray-700"
        >
          <div className={`h-full rounded-full ${warning ? 'bg-amber-400' : 'bg-red-500'}`} style={{ width: `${progress}%` }} />
        </div>
      )}
      {(size || remaining) && (
        <div className="mt-1.5 flex justify-between gap-2 text-[11px] text-gray-400">
          <span>{size}</span><span>{remaining}</span>
        </div>
      )}
      <Link
        to={complete && imported && imported.count > 1 ? '/activity' : `/activity?queue=${item.id}`}
        className="mt-2 flex min-h-11 items-center justify-between rounded-lg px-2 text-xs font-medium text-red-300 transition-colors hover:bg-red-900/20 hover:text-red-200 focus-visible:outline focus-visible:outline-2 focus-visible:outline-red-400"
      >
        View in Activity <ArrowRightIcon className="h-4 w-4" />
      </Link>
    </section>
  );
}
