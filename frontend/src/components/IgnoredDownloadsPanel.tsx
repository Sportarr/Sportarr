import { useEffect, useRef, useState } from 'react';
import axios from 'axios';
import apiClient from '../api/client';
import { TrashIcon } from '@heroicons/react/24/outline';
import ConfirmationModal from './ConfirmationModal';
import { BUTTON_DESTRUCTIVE, BUTTON_SECONDARY } from '../utils/designTokens';

interface IgnoredDownload {
  id: number;
  title: string;
  clientName: string | null;
  ignoredAt: string | null;
}

export default function IgnoredDownloadsPanel({ refreshToken, compactView = false }: { refreshToken: number; compactView?: boolean }) {
  const [items, setItems] = useState<IgnoredDownload[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [confirmClear, setConfirmClear] = useState(false);
  const readVersion = useRef(0);

  useEffect(() => {
    let active = true;
    const version = readVersion.current;
    setLoading(true);
    setError('');
    apiClient.get<IgnoredDownload[]>('/queue/ignored')
      .then(response => { if (active && version === readVersion.current) setItems(response.data); })
      .catch(() => { if (active && version === readVersion.current) setError('Could not load ignored downloads. Refresh to try again.'); })
      .finally(() => { if (active && version === readVersion.current) setLoading(false); });
    return () => { active = false; };
  }, [refreshToken]);

  const resume = async (item?: IgnoredDownload) => {
    setBusyId(item?.id ?? 0);
    setError('');
    try {
      await apiClient.post(item ? `/queue/ignored/${item.id}/resume` : '/queue/ignored/clear');
      readVersion.current++;
      setError('');
      setItems(current => item ? current.filter(row => row.id !== item.id) : []);
    } catch (failure) {
      setError(axios.isAxiosError<{ error?: string }>(failure)
        ? failure.response?.data?.error || 'Could not unignore this download. Try again.'
        : 'Could not unignore this download. Try again.');
    } finally {
      setLoading(false);
      setBusyId(null);
    }
  };

  const clearAll = async () => {
    setConfirmClear(false);
    await resume();
  };

  const unignoreButton = (item: IgnoredDownload) => (
    <button className={BUTTON_SECONDARY} disabled={busyId !== null} onClick={() => resume(item)}
      title="Unignore download" aria-label="Unignore">
      Unignore
    </button>
  );

  return (
    <div className="space-y-4">
      <p className="text-sm text-gray-400">
        Sportarr skips these jobs when polling download clients. Automatic file discovery also skips known job paths;
        {' '}files with shared or unknown paths can still be discovered independently.
        {' '}Unignore allows processing again; Clear All unignores every listed job. Files and release blocks are kept.
        {' '}Ignoring a job does not prevent a future download.
      </p>
      {!loading && items.length > 0 && (
        <div className="flex justify-end">
          <button className={BUTTON_DESTRUCTIVE} disabled={busyId !== null} onClick={() => setConfirmClear(true)}>
            <TrashIcon className="w-4 h-4" />Clear All
          </button>
        </div>
      )}
      <ConfirmationModal isOpen={confirmClear} onClose={() => setConfirmClear(false)} onConfirm={clearAll}
        title="Clear ignored downloads" confirmText="Clear All"
        message="Unignore every download listed here? Sportarr can process these jobs again on its next scan. Files and release blocks will be kept." />
      {error && <p role="alert" className="text-red-400">{error}</p>}
      {loading ? <p role="status">Loading ignored downloads...</p> : !error && items.length === 0 ? (
        <p className="py-8 text-center text-gray-400">No ignored downloads</p>
      ) : compactView ? (
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-gray-800 text-gray-300 text-xs">
                <th className="px-3 py-1.5 text-left font-medium">Download</th>
                <th className="px-2 py-1.5 text-center font-medium">Client</th>
                <th className="px-2 py-1.5 text-right font-medium">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-700">
              {items.map(item => (
                <tr key={item.id} className="hover:bg-gray-800/50 transition-colors">
                  <td className="px-3 py-1.5 min-w-[200px] text-xs text-gray-300 break-words">{item.title}</td>
                  <td className="px-2 py-1.5 text-center text-xs text-gray-400">{item.clientName ?? 'Download client no longer available'}</td>
                  <td className="px-2 py-1.5"><div className="flex justify-end">{unignoreButton(item)}</div></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <ul className="space-y-3">
          {items.map(item => (
            <li key={item.id} className="flex flex-wrap items-center justify-between gap-3 bg-gray-800 border border-gray-700 rounded-lg p-4 hover:bg-gray-750 transition-colors">
              <div className="min-w-0">
                <p className="break-words">{item.title}</p>
                <p className="text-sm text-gray-400">{item.clientName ?? 'Download client no longer available'}</p>
              </div>
              {unignoreButton(item)}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
