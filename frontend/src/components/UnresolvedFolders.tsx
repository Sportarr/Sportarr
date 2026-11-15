import { useState } from 'react';
import { ArrowPathIcon, FolderIcon, PlusIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPost } from '../utils/api';
import { BUTTON_INFO } from '../utils/designTokens';
import { isTeamlessSport, usesFightingEventTypes } from '../utils/leagueSportRules';

export interface UnresolvedFolder {
  path: string;
  name: string;
  suggestedLeagueExternalId?: string | null;
  suggestedLeagueName?: string | null;
  suggestedSport?: string | null;
  suggestedSportFormat?: string | null;
}

interface RootFolder {
  id: number;
  path: string;
  defaultQualityProfileId?: number | null;
}

interface QualityProfile {
  id: number;
}

interface UnresolvedFoldersProps {
  folders: UnresolvedFolder[];
  // Scans the folder again so its files match the new league.
  onLeagueAdded: () => Promise<void> | void;
}

// A path with a drive letter or a backslash is a Windows path.
function isWindowsPath(path: string): boolean {
  return /^[A-Za-z]:/.test(path) || path.includes('\\');
}

function isInside(path: string, root: string): boolean {
  const trimmed = root.replace(/[\\/]+$/, '');
  if (trimmed.length === 0) return false;
  // Windows ignores letter case in paths. Other systems do not.
  const ignoreCase = isWindowsPath(path) || isWindowsPath(root);
  const folder = ignoreCase ? path.toLowerCase() : path;
  const base = ignoreCase ? trimmed.toLowerCase() : trimmed;
  return folder.startsWith(base + '/') || folder.startsWith(base + '\\');
}

async function readJson<T>(url: string): Promise<T | null> {
  try {
    const response = await apiGet(url);
    return response.ok ? ((await response.json()) as T) : null;
  } catch {
    return null;
  }
}

async function readError(response: Response): Promise<string> {
  const text = await response.text().catch(() => '');
  try {
    const body = JSON.parse(text);
    const message = body?.error ?? body?.detail ?? body?.title;
    if (typeof message === 'string' && message.trim()) return message;
  } catch {
    // The body is not JSON. Show it as it is.
  }
  return text.trim() || 'Could not add the league.';
}

export default function UnresolvedFolders({ folders, onLeagueAdded }: UnresolvedFoldersProps) {
  const [addingPath, setAddingPath] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const queryClient = useQueryClient();

  if (folders.length === 0) return null;

  const addLeague = async (folder: UnresolvedFolder) => {
    const externalId = folder.suggestedLeagueExternalId;
    const name = folder.suggestedLeagueName;
    const sport = folder.suggestedSport ?? '';
    const sportFormat = folder.suggestedSportFormat ?? null;
    if (!externalId || !name) return;

    setAddingPath(folder.path);
    setErrors(prev => {
      const next = { ...prev };
      delete next[folder.path];
      return next;
    });

    try {
      // The add-league dialog binds a new league to a root folder and a
      // quality profile. Bind it to the root folder that holds this folder.
      const [rootFolders, profiles] = await Promise.all([
        readJson<RootFolder[]>('/api/rootfolder'),
        readJson<QualityProfile[]>('/api/qualityprofile'),
      ]);
      const root = (rootFolders ?? [])
        .filter(r => isInside(folder.path, r.path))
        .sort((a, b) => b.path.length - a.path.length)[0];

      // These are the defaults of the add-league dialog for a new league.
      const response = await apiPost('/api/leagues', {
        externalId,
        name,
        sport,
        sportFormat,
        monitored: isTeamlessSport(sport, name, sportFormat) || usesFightingEventTypes(sport, name),
        monitorType: 'Future',
        qualityProfileId: root?.defaultQualityProfileId ?? profiles?.[0]?.id ?? null,
        rootFolderId: root?.id ?? null,
        searchForMissingEvents: false,
        searchForCutoffUnmetEvents: false,
        monitoredParts: null,
        monitoredSessionTypes: null,
        monitoredEventTypes: null,
        monitorFinals: false,
        monitorPlayoffs: false,
        monitorPreseason: false,
        finalTeamMode: 'Off',
        playoffTeamMode: 'Off',
        preseasonTeamMode: 'Off',
        specialEventsMonitorType: 'Future',
        allowHighlights: false,
        retentionDays: 0,
        enableDvr: true,
      });

      if (!response.ok) {
        const message = await readError(response);
        setErrors(prev => ({ ...prev, [folder.path]: message }));
        setAddingPath(null);
        return;
      }

      // The league lists elsewhere in the app must show the new league.
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
      queryClient.invalidateQueries({ queryKey: ['sportarr-leagues'] });

      const added = await response.json().catch(() => null);
      if (added?.monitored === false) {
        // A team league added with no teams gets no events.
        toast.success(`Added ${name} with no teams, so it has no events yet. Choose its teams on the league page.`);
      } else {
        toast.success(`Added ${name}. Its events can take a few minutes to load. Scan again if files stay unmatched.`);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not add the league.';
      setErrors(prev => ({ ...prev, [folder.path]: message }));
      setAddingPath(null);
      return;
    }

    try {
      await onLeagueAdded();
    } finally {
      setAddingPath(null);
    }
  };

  return (
    <div>
      <h3 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
        <FolderIcon className="w-6 h-6 text-yellow-400" />
        Folders Without a League ({folders.length})
      </h3>
      <div className="space-y-2">
        {folders.map(folder => {
          const isAdding = addingPath === folder.path;
          const error = errors[folder.path];
          const canAdd = Boolean(folder.suggestedLeagueExternalId && folder.suggestedLeagueName);
          return (
            <div key={folder.path} className="p-3 bg-gray-800 rounded-lg border border-yellow-700">
              <div className="flex flex-wrap items-center gap-3">
                <div className="flex-1 min-w-[12rem]">
                  <p className="text-white font-medium break-words">
                    Folder "{folder.name}" is not one of your leagues.
                  </p>
                  <p className="text-sm text-gray-500 break-all">{folder.path}</p>
                </div>
                {canAdd ? (
                  <button
                    type="button"
                    onClick={() => addLeague(folder)}
                    disabled={addingPath !== null}
                    className={`${BUTTON_INFO} min-h-11`}
                  >
                    {isAdding ? (
                      <ArrowPathIcon className="w-4 h-4 animate-spin" />
                    ) : (
                      <PlusIcon className="w-4 h-4" />
                    )}
                    {isAdding ? `Adding ${folder.suggestedLeagueName}` : `Add ${folder.suggestedLeagueName}`}
                  </button>
                ) : (
                  <p className="text-sm text-gray-400">No matching league found. Add the league from the Leagues page, then scan again.</p>
                )}
              </div>
              {error && (
                <p role="alert" className="mt-2 text-sm text-red-400 break-words">{error}</p>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
