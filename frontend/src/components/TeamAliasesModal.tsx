import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { XMarkIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';

interface TeamRow {
  id: number;
  strTeam: string;
  strTeamShort?: string;
  strAlternate?: string;
  userAliases?: string;
}

interface TeamAliasesModalProps {
  isOpen: boolean;
  onClose: () => void;
  leagueId: number;
  leagueName: string;
}

/// Per-team alias editor. Release groups often use names none of the
/// upstream data carries ("GWS", "ManCity", bare club names); aliases added
/// here are matched against release titles exactly like the synced
/// alternates, and they are local-only so metadata syncs never overwrite
/// them.
export default function TeamAliasesModal({ isOpen, onClose, leagueId, leagueName }: TeamAliasesModalProps) {
  const [teams, setTeams] = useState<TeamRow[]>([]);
  const [edits, setEdits] = useState<Record<number, string>>({});
  const [loading, setLoading] = useState(false);
  const [savingId, setSavingId] = useState<number | null>(null);

  useEffect(() => {
    if (!isOpen) return;
    setEdits({});
    setLoading(true);
    apiClient
      .get<TeamRow[]>(`/teams?leagueId=${leagueId}`)
      .then((response) => setTeams(response.data))
      .catch((err) => {
        console.error('Failed to load teams:', err);
        toast.error('Failed to load teams');
      })
      .finally(() => setLoading(false));
  }, [isOpen, leagueId]);

  if (!isOpen) return null;

  const valueFor = (team: TeamRow) =>
    edits[team.id] !== undefined ? edits[team.id] : (team.userAliases || '');

  const isDirty = (team: TeamRow) =>
    edits[team.id] !== undefined && edits[team.id] !== (team.userAliases || '');

  const save = async (team: TeamRow) => {
    setSavingId(team.id);
    try {
      const { data } = await apiClient.put<{ id: number; userAliases?: string }>(
        `/teams/${team.id}/aliases`,
        { userAliases: valueFor(team) }
      );
      setTeams((prev) => prev.map((t) => (t.id === team.id ? { ...t, userAliases: data.userAliases } : t)));
      setEdits((prev) => {
        const next = { ...prev };
        delete next[team.id];
        return next;
      });
      toast.success(`Aliases saved for ${team.strTeam}`);
    } catch (err) {
      console.error('Failed to save aliases:', err);
      toast.error('Failed to save aliases');
    } finally {
      setSavingId(null);
    }
  };

  return (
    <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-3xl w-full my-8">
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-2xl font-bold text-white">Team Aliases - {leagueName}</h3>
          <button onClick={onClose} className="text-gray-400 hover:text-white transition-colors">
            <XMarkIcon className="w-6 h-6" />
          </button>
        </div>
        <p className="text-sm text-gray-400 mb-4">
          Add the names your release groups actually use, comma-separated (e.g. "GWS, GWS Giants").
          Aliases are used when matching releases to events and are never overwritten by metadata
          syncs. The official name and any synced alternates always match too.
        </p>

        {loading ? (
          <p className="text-gray-400 py-8 text-center">Loading teams...</p>
        ) : teams.length === 0 ? (
          <p className="text-gray-400 py-8 text-center">No teams found for this league.</p>
        ) : (
          <div className="space-y-2 max-h-[60vh] overflow-y-auto pr-1">
            {teams.map((team) => (
              <div key={team.id} className="flex items-center gap-3 p-3 bg-gray-800/50 border border-gray-700 rounded-lg">
                <div className="w-64 flex-shrink-0">
                  <div className="text-white text-sm font-medium truncate">{team.strTeam}</div>
                  {team.strAlternate && (
                    <div className="text-gray-500 text-xs truncate" title={`Synced alternates: ${team.strAlternate}`}>
                      also: {team.strAlternate}
                    </div>
                  )}
                </div>
                <input
                  type="text"
                  value={valueFor(team)}
                  onChange={(e) => setEdits((prev) => ({ ...prev, [team.id]: e.target.value }))}
                  placeholder="Your aliases, comma-separated"
                  className="flex-1 px-3 py-1.5 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm placeholder-gray-600 focus:outline-none focus:border-red-600"
                />
                <button
                  onClick={() => save(team)}
                  disabled={!isDirty(team) || savingId === team.id}
                  className="px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm rounded-lg transition-colors disabled:opacity-40 disabled:cursor-not-allowed flex-shrink-0"
                >
                  {savingId === team.id ? 'Saving...' : 'Save'}
                </button>
              </div>
            ))}
          </div>
        )}

        <div className="flex justify-end mt-4">
          <button
            onClick={onClose}
            className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
