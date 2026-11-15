import { useState } from 'react';
import { ChevronDownIcon, ChevronRightIcon } from '@heroicons/react/24/outline';

// Shape returned by GET /api/iptv/leagues/{lid}/exclusions.
export interface LeagueChannelExclusion {
  mappingId: number;
  channelId: number;
  channelName: string | null;
  sourceName: string | null;
  country: string | null;
}

const buttonClass =
  'px-2.5 py-1 text-xs bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-200 rounded transition-colors disabled:opacity-50';

/**
 * Lists the channels that Unmap excluded from a league. The section stays
 * closed until the user opens it, and it is not shown when the list is empty.
 */
export default function ExcludedChannelsSection({
  exclusions,
  error,
  busy,
  onMapAgain,
  onRemove,
}: {
  exclusions: LeagueChannelExclusion[] | undefined;
  error?: string;
  busy: boolean;
  onMapAgain: (channelId: number) => void;
  onRemove: (channelId: number) => void;
}) {
  const [open, setOpen] = useState(false);

  if (error) {
    return (
      <p className="mt-3 text-xs text-red-300">
        Could not load the excluded channels. {error}
      </p>
    );
  }
  if (!exclusions || exclusions.length === 0) return null;

  return (
    <div className="mt-3">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="flex items-center gap-1 text-xs uppercase tracking-wide text-gray-400 hover:text-white transition-colors"
      >
        {open ? <ChevronDownIcon className="w-3.5 h-3.5" /> : <ChevronRightIcon className="w-3.5 h-3.5" />}
        Excluded channels ({exclusions.length})
      </button>
      {open && (
        <div className="grid grid-cols-1 gap-1.5 mt-2">
          <p className="text-xs text-gray-500">
            Auto-mapping skips these channels. Map again maps a channel to this league now. Remove
            exclusion lets auto-mapping decide.
          </p>
          {exclusions.map((x) => {
            const name = x.channelName || `Channel ${x.channelId}`;
            return (
              <div
                key={x.channelId}
                className="flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-3 px-3 py-2 rounded bg-gray-900/40 border border-gray-800"
              >
                <div className="flex-1 min-w-0">
                  <div className="text-gray-300 truncate">{name}</div>
                  <div className="text-xs text-gray-500 mt-0.5 truncate">
                    {x.sourceName || 'Unknown source'}
                    {x.country && ` · ${x.country}`}
                  </div>
                </div>
                <div className="flex items-center justify-end gap-1 flex-shrink-0">
                  <button
                    type="button"
                    onClick={() => onMapAgain(x.channelId)}
                    disabled={busy}
                    aria-label={`Map ${name} again`}
                    className={buttonClass}
                  >
                    Map again
                  </button>
                  <button
                    type="button"
                    onClick={() => onRemove(x.channelId)}
                    disabled={busy}
                    aria-label={`Remove exclusion for ${name}`}
                    className={buttonClass}
                  >
                    Remove exclusion
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
