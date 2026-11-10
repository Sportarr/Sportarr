import { useEffect, useRef, useState } from 'react';
import { toast } from 'sonner';
import {
  ArrowPathIcon,
  Bars3Icon,
  ChevronDownIcon,
  ChevronUpIcon,
  MagnifyingGlassIcon,
  TrashIcon,
} from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import { BADGE_AMBER, BADGE_RED } from '../utils/designTokens';
import { moveItem } from '../utils/moveItem';

export interface TeamChannelEntry {
  id: number;
  channelId: number;
  channelName?: string | null;
  sourceName?: string | null;
  isEnabled: boolean;
  status: 'Unknown' | 'Online' | 'Offline' | 'Error';
  priority: number;
  isPreferred: boolean;
}

interface ChannelOption {
  id: number;
  name: string;
  group?: string | null;
}

interface TeamChannelRankingProps {
  teamExternalId: string;
  teamName: string;
  /** Change this value to look the team up again, for example after leagues are added. */
  refreshKey?: number;
}

type Direction = 'up' | 'down';

const MOVE_BUTTON =
  'p-1.5 rounded text-gray-400 hover:text-white hover:bg-gray-700 transition-colors disabled:opacity-30 disabled:cursor-not-allowed';

const nameOf = (entry: TeamChannelEntry) => entry.channelName || `Channel #${entry.channelId}`;

/**
 * Ranked DVR channel list for one team. The DVR records the team's games
 * from the highest ranked channel that the guide lists the game on, and
 * tries the rest in order when a recording fails.
 */
export default function TeamChannelRanking({ teamExternalId, teamName, refreshKey = 0 }: TeamChannelRankingProps) {
  // Undefined while loading. Null when the library has no row for the team.
  const [teamId, setTeamId] = useState<number | null | undefined>(undefined);
  const [loadFailed, setLoadFailed] = useState(false);
  const [retryKey, setRetryKey] = useState(0);
  const [entries, setEntries] = useState<TeamChannelEntry[]>([]);
  const [pendingSaves, setPendingSaves] = useState(0);
  const [announcement, setAnnouncement] = useState('');
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<ChannelOption[]>([]);
  const [searching, setSearching] = useState(false);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [dropIndex, setDropIndex] = useState<number | null>(null);
  const [pendingFocus, setPendingFocus] = useState<{ channelId: number; direction: Direction } | null>(null);
  const loadSeq = useRef(0);
  const saveSeq = useRef(0);
  const saveChain = useRef<Promise<void>>(Promise.resolve());
  const confirmed = useRef<TeamChannelEntry[]>([]);
  const moveButtons = useRef(new Map<string, HTMLButtonElement>());

  useEffect(() => {
    const seq = ++loadSeq.current;
    // A reply to a save for the previous team must not land here.
    saveSeq.current += 1;
    confirmed.current = [];
    setTeamId(undefined);
    setLoadFailed(false);
    (async () => {
      try {
        const { data: teams } = await apiClient.get<Array<{ id: number }>>('/teams', {
          params: { externalId: teamExternalId },
        });
        const id = Array.isArray(teams) && teams.length > 0 ? teams[0].id : null;
        if (id == null) {
          if (seq !== loadSeq.current) return;
          setEntries([]);
          setTeamId(null);
          return;
        }
        const { data } = await apiClient.get<TeamChannelEntry[]>(`/iptv/teams/${id}/channel-mappings`);
        if (seq !== loadSeq.current) return;
        const list = Array.isArray(data) ? data : [];
        confirmed.current = list;
        setEntries(list);
        setTeamId(id);
      } catch {
        if (seq !== loadSeq.current) return;
        setLoadFailed(true);
        setTeamId(null);
      }
    })();
  }, [teamExternalId, refreshKey, retryKey]);

  useEffect(() => {
    const term = search.trim();
    if (term.length < 2) {
      setOptions([]);
      return;
    }
    let current = true;
    const handle = setTimeout(async () => {
      setSearching(true);
      try {
        const { data } = await apiClient.get<ChannelOption[]>('/iptv/channels', {
          params: { search: term, enabledOnly: true, limit: 20 },
        });
        if (current) setOptions(Array.isArray(data) ? data : []);
      } catch {
        if (current) setOptions([]);
      } finally {
        if (current) setSearching(false);
      }
    }, 300);
    return () => {
      current = false;
      clearTimeout(handle);
    };
  }, [search]);

  // Keep focus on the moved row. At the top or the bottom of the list the
  // pressed button is disabled, so the other move button gets the focus.
  useEffect(() => {
    if (!pendingFocus) return;
    const { channelId, direction } = pendingFocus;
    const other: Direction = direction === 'up' ? 'down' : 'up';
    const pressed = moveButtons.current.get(`${channelId}:${direction}`);
    const target = pressed && !pressed.disabled ? pressed : moveButtons.current.get(`${channelId}:${other}`);
    target?.focus();
    setPendingFocus(null);
  }, [entries, pendingFocus]);

  // Saves run one after another, so the server ends with the newest list.
  // Only the reply to the newest save updates the screen.
  const save = (next: TeamChannelEntry[]) => {
    if (teamId == null) return;
    const seq = ++saveSeq.current;
    const forTeam = teamId;
    const forLoad = loadSeq.current;
    setEntries(next);
    setPendingSaves((count) => count + 1);
    saveChain.current = saveChain.current.then(async () => {
      try {
        const { data } = await apiClient.put<TeamChannelEntry[]>(`/iptv/teams/${forTeam}/channel-mappings`, {
          channelIds: next.map((entry) => entry.channelId),
        });
        // Drop a reply that belongs to a team that is no longer shown.
        if (forLoad !== loadSeq.current) return;
        const list = Array.isArray(data) ? data : next;
        confirmed.current = list;
        if (seq === saveSeq.current) setEntries(list);
      } catch (err) {
        if (forLoad === loadSeq.current && seq === saveSeq.current) setEntries(confirmed.current);
        toast.error('Failed to save channel order', {
          description: err instanceof Error ? err.message : undefined,
        });
      } finally {
        setPendingSaves((count) => count - 1);
      }
    });
  };

  const move = (from: number, to: number, direction?: Direction) => {
    const next = moveItem(entries, from, to);
    if (next === entries) return;
    const moved = entries[from];
    setAnnouncement(`${nameOf(moved)} moved to rank ${to + 1} of ${entries.length}.`);
    if (direction) setPendingFocus({ channelId: moved.channelId, direction });
    save(next);
  };

  const remove = (index: number) => {
    setAnnouncement(`${nameOf(entries[index])} removed.`);
    save(entries.filter((_, i) => i !== index));
  };

  const add = (option: ChannelOption) => {
    setSearch('');
    setOptions([]);
    if (entries.some((entry) => entry.channelId === option.id)) return;
    setAnnouncement(`${option.name} added at rank ${entries.length + 1}.`);
    save([
      ...entries,
      {
        id: 0,
        channelId: option.id,
        channelName: option.name,
        isEnabled: true,
        status: 'Unknown',
        priority: entries.length,
        isPreferred: entries.length === 0,
      },
    ]);
  };

  const setMoveButton = (key: string) => (element: HTMLButtonElement | null) => {
    if (element) moveButtons.current.set(key, element);
    else moveButtons.current.delete(key);
  };

  const listed = new Set(entries.map((entry) => entry.channelId));
  const choices = options.filter((option) => !listed.has(option.id));

  return (
    <div className="bg-gray-900/50 border border-gray-800 rounded-lg p-4">
      <div className="flex items-center gap-2 mb-1">
        <h4 className="font-medium text-white">DVR Channels</h4>
        {pendingSaves > 0 && <ArrowPathIcon className="w-4 h-4 text-gray-400 animate-spin" aria-label="Saving" />}
      </div>
      <p className="text-xs text-gray-500 mb-3">
        Games record on the highest ranked channel the TV guide lists them on. If the guide lists none of these,
        another listed channel is used. With no listing at all, rank 1 records and the rest are backups.
      </p>
      <p className="sr-only" aria-live="polite">{announcement}</p>

      {teamId === undefined ? (
        <p className="text-sm text-gray-400 py-4 text-center">Loading channels...</p>
      ) : loadFailed ? (
        <div className="text-sm text-gray-400 py-4 text-center">
          <p>Could not load this team's channels.</p>
          <button
            type="button"
            onClick={() => setRetryKey((key) => key + 1)}
            className="mt-1 text-sm text-blue-400 hover:text-blue-300"
          >
            Try again
          </button>
        </div>
      ) : teamId === null ? (
        <p className="text-sm text-gray-400 py-4 text-center">
          Add one of this team's leagues first, then pick its channels here.
        </p>
      ) : (
        <>
          {entries.length === 0 ? (
            <p className="text-sm text-gray-400 py-4 text-center">No channels yet. The league's channels are used.</p>
          ) : (
            <ol className="space-y-2 mb-3" aria-label={`${teamName} DVR channels`}>
              {entries.map((entry, index) => {
                const name = nameOf(entry);
                const dead = entry.status === 'Offline' || entry.status === 'Error';
                return (
                  <li
                    key={entry.channelId}
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
                    } ${dragIndex === index ? 'opacity-50 border-dashed' : ''}`}
                  >
                    {/* Touch screens cannot drag, so phones use the move buttons. */}
                    <span className="hidden sm:inline-flex text-gray-500 cursor-grab flex-shrink-0" title="Drag to reorder">
                      <Bars3Icon className="w-4 h-4" />
                    </span>
                    <div className="flex-1 min-w-0">
                      <div className="text-white text-sm font-medium break-words sm:truncate">
                        <span className="sm:hidden mr-1.5 text-gray-400 font-semibold" aria-hidden="true">{index + 1}.</span>
                        {name}
                      </div>
                      <div className="flex items-center gap-2 min-w-0">
                        {entry.sourceName && (
                          <span className="text-gray-500 text-xs truncate">{entry.sourceName}</span>
                        )}
                        {!entry.isEnabled && <span className={BADGE_AMBER}>Disabled</span>}
                        {entry.isEnabled && dead && <span className={BADGE_RED}>{entry.status}</span>}
                      </div>
                    </div>
                    <div className="hidden sm:block text-center flex-shrink-0 w-10">
                      <div className="text-[10px] uppercase tracking-wide text-gray-500">Rank</div>
                      <div className="text-white text-sm font-semibold">{index + 1}</div>
                    </div>
                    <div className="flex items-center flex-shrink-0">
                      <button
                        type="button"
                        ref={setMoveButton(`${entry.channelId}:up`)}
                        onClick={() => move(index, index - 1, 'up')}
                        disabled={index === 0}
                        className={MOVE_BUTTON}
                        aria-label={`Move ${name} up`}
                        title="Move up"
                      >
                        <ChevronUpIcon className="w-4 h-4" />
                      </button>
                      <button
                        type="button"
                        ref={setMoveButton(`${entry.channelId}:down`)}
                        onClick={() => move(index, index + 1, 'down')}
                        disabled={index === entries.length - 1}
                        className={MOVE_BUTTON}
                        aria-label={`Move ${name} down`}
                        title="Move down"
                      >
                        <ChevronDownIcon className="w-4 h-4" />
                      </button>
                      <button
                        type="button"
                        onClick={() => remove(index)}
                        className="p-1.5 rounded text-gray-400 hover:text-red-400 hover:bg-red-900/30 transition-colors"
                        aria-label={`Remove ${name}`}
                        title="Remove"
                      >
                        <TrashIcon className="w-4 h-4" />
                      </button>
                    </div>
                  </li>
                );
              })}
            </ol>
          )}

          <div>
            <div className="relative">
              <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 pointer-events-none" />
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Add a channel..."
                aria-label="Search channels to add"
                className="w-full pl-9 pr-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm placeholder-gray-500 focus:outline-none focus:border-red-600"
              />
            </div>
            {search.trim().length >= 2 && (
              <div className="mt-1 w-full max-h-60 overflow-y-auto bg-gray-900 border border-gray-700 rounded-lg">
                {searching && choices.length === 0 ? (
                  <p className="px-3 py-2 text-sm text-gray-400">Searching...</p>
                ) : choices.length === 0 ? (
                  <p className="px-3 py-2 text-sm text-gray-400">No enabled channels match</p>
                ) : (
                  choices.map((option) => (
                    <button
                      key={option.id}
                      type="button"
                      onClick={() => add(option)}
                      className="w-full text-left px-3 py-2 text-sm text-white hover:bg-gray-800 transition-colors"
                    >
                      {option.name}
                      {option.group && <span className="ml-2 text-xs text-gray-500">{option.group}</span>}
                    </button>
                  ))
                )}
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
