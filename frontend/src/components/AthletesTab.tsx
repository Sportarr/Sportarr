import { useState, useRef } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronDownIcon, ChevronUpIcon, MagnifyingGlassIcon, TrashIcon, UserGroupIcon, UserIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../api/client';
import { getSportIcon } from '../utils/sportIcons';
import type { QualityProfile } from '../types';

// The Athletes tab of the Follow page. Combat athletes monitor through
// per-event participation on the metadata side; team-sport athletes resolve
// to their current team and ride team scoping. The discovery response's
// `mode` field tells us which story to present.

interface AthleteSearchResult {
  idPlayer?: string;
  strPlayer?: string;
  strSport?: string;
  strThumb?: string;
  strTeam?: string;
  strNationality?: string;
}

interface FollowedAthlete {
  id: number;
  externalId: string;
  name: string;
  sport: string;
  thumbUrl?: string;
  resolvedTeamName?: string;
}

interface AthleteLeague {
  externalId: string;
  name: string;
  sport: string;
  eventCount: number;
  isAdded: boolean;
}

interface AthleteDiscovery {
  athleteId: number;
  athleteName: string;
  mode: 'events' | 'team' | 'none';
  eventCount: number;
  resolvedTeam: { externalId: string; name: string } | null;
  leagues: AthleteLeague[];
  message?: string;
}

export default function AthletesTab({ qualityProfiles }: { qualityProfiles: QualityProfile[] }) {
  const queryClient = useQueryClient();
  const [searchInput, setSearchInput] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [expandedAthleteId, setExpandedAthleteId] = useState<number | null>(null);
  const [discovery, setDiscovery] = useState<AthleteDiscovery | null>(null);
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [selectedLeagueIds, setSelectedLeagueIds] = useState<Set<string>>(new Set());
  const [qualityProfileId, setQualityProfileId] = useState<number>(1);
  const [isAdding, setIsAdding] = useState(false);

  const { data: searchResults = [], isFetching: isSearching } = useQuery({
    queryKey: ['athlete-search', searchQuery],
    queryFn: async () => {
      const response = await apiClient.get<AthleteSearchResult[]>(`/athletes/search/${encodeURIComponent(searchQuery)}`);
      return Array.isArray(response.data) ? response.data : [];
    },
    enabled: searchQuery.trim().length >= 2,
  });

  const { data: followedAthletes = [] } = useQuery({
    queryKey: ['followed-athletes'],
    queryFn: async () => {
      const response = await apiClient.get<FollowedAthlete[]>('/followed-athletes');
      return Array.isArray(response.data) ? response.data : [];
    },
  });

  const followedIds = new Set(followedAthletes.map((a) => a.externalId));

  const followMutation = useMutation({
    mutationFn: async (athlete: AthleteSearchResult) => apiClient.post('/followed-athletes', {
      externalId: athlete.idPlayer,
      name: athlete.strPlayer,
      sport: athlete.strSport ?? '',
      thumbUrl: athlete.strThumb,
    }),
    onSuccess: async (_response, athlete) => {
      await queryClient.invalidateQueries({ queryKey: ['followed-athletes'] });
      toast.success(`Now following ${athlete.strPlayer}`);
    },
    onError: () => toast.error('Failed to follow athlete'),
  });

  const unfollowMutation = useMutation({
    mutationFn: async (id: number) => apiClient.delete(`/followed-athletes/${id}`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['followed-athletes'] });
      setExpandedAthleteId(null);
      toast.success('Unfollowed athlete');
    },
    onError: () => toast.error('Failed to unfollow athlete'),
  });

  const discoverLeagues = async (athlete: FollowedAthlete) => {
    if (expandedAthleteId === athlete.id) {
      setExpandedAthleteId(null);
      setDiscovery(null);
      return;
    }
    const seq = ++discoverSeq.current;
    setExpandedAthleteId(athlete.id);
    setDiscovery(null);
    setSelectedLeagueIds(new Set());
    setIsDiscovering(true);
    try {
      const response = await apiClient.get<AthleteDiscovery>(`/followed-athletes/${athlete.id}/leagues`);
      // Expanding a second athlete while the first was still loading let the
      // first one's leagues arrive last and be listed under the second, and
      // the user then added leagues for an athlete they were not looking at.
      if (seq !== discoverSeq.current) return;
      setDiscovery(response.data);
      setSelectedLeagueIds(new Set(response.data.leagues.filter((l) => !l.isAdded).map((l) => l.externalId)));
    } catch {
      if (seq !== discoverSeq.current) return;
      toast.error('Failed to discover leagues for athlete');
      setExpandedAthleteId(null);
    } finally {
      if (seq === discoverSeq.current) setIsDiscovering(false);
    }
  };

  const discoverSeq = useRef(0);

  const addLeagues = async () => {
    if (!discovery || selectedLeagueIds.size === 0) return;

    // Refuse to submit one athlete's leagues while another is expanded.
    if (discovery.athleteId !== expandedAthleteId) {
      toast.error('These leagues belong to a different athlete. Reopen this one to load theirs.');
      return;
    }
    setIsAdding(true);
    try {
      await apiClient.post(`/followed-athletes/${discovery.athleteId}/add-leagues`, {
        leagueExternalIds: Array.from(selectedLeagueIds),
        qualityProfileId,
      });
      toast.success(`Added ${selectedLeagueIds.size} league${selectedLeagueIds.size === 1 ? '' : 's'} for ${discovery.athleteName}`);
      setExpandedAthleteId(null);
      setDiscovery(null);
      await queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch {
      toast.error('Failed to add leagues');
    } finally {
      setIsAdding(false);
    }
  };

  return (
    <div>
      <div className="bg-gradient-to-r from-blue-900/30 to-purple-900/30 border border-blue-700/30 rounded-lg p-4 mb-6">
        <p className="text-sm text-gray-300">
          <span className="font-semibold text-white">Follow Athlete</span> monitors every event an athlete
          appears on. Fighters are tracked per card. Team-sport athletes follow their current team's games,
          and the mapping updates itself when they change teams.
        </p>
      </div>

      {/* Search */}
      <form
        className="mb-4 flex gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          setSearchQuery(searchInput);
        }}
      >
        <div className="relative min-w-[180px] flex-1">
          <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500 pointer-events-none" />
          <input
            type="text"
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            placeholder="Search athletes (e.g., Dan Ige, Austin Reaves)..."
            className="w-full pl-10 pr-4 py-2.5 bg-gray-800 border border-gray-700 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
          />
        </div>
        <button
          type="submit"
          disabled={searchInput.trim().length < 2}
          className="rounded-lg bg-red-600 px-4 py-2.5 text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {isSearching ? 'Searching...' : 'Search'}
        </button>
      </form>

      {/* Search results */}
      {searchQuery && (
        <div className="mb-8">
          <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-gray-400">
            Results for "{searchQuery}"
          </h3>
          {searchResults.length === 0 && !isSearching ? (
            <p className="text-sm text-gray-500">No athletes found.</p>
          ) : (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {searchResults.map((athlete) => (
                <div
                  key={athlete.idPlayer}
                  className="flex items-center gap-3 rounded-lg border border-gray-800 bg-gray-900/60 p-3"
                >
                  <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center overflow-hidden rounded-full bg-black/50">
                    {athlete.strThumb ? (
                      <img src={athlete.strThumb} alt={athlete.strPlayer} className="h-full w-full object-cover" />
                    ) : (
                      <UserIcon className="h-5 w-5 text-gray-500" />
                    )}
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="truncate font-medium text-white">{athlete.strPlayer}</div>
                    <div className="truncate text-xs text-gray-400">
                      {athlete.strSport ? `${getSportIcon(athlete.strSport)} ${athlete.strSport}` : 'Sport unknown'}
                      {athlete.strTeam ? ` - ${athlete.strTeam}` : ''}
                    </div>
                  </div>
                  {followedIds.has(athlete.idPlayer ?? '') ? (
                    <span className="whitespace-nowrap rounded bg-green-900/30 px-2 py-1 text-xs text-green-400">Following</span>
                  ) : (
                    <button
                      onClick={() => followMutation.mutate(athlete)}
                      disabled={followMutation.isPending}
                      className="inline-flex items-center gap-1 whitespace-nowrap rounded-lg bg-red-600 px-3 py-1.5 text-xs font-medium text-white transition-colors hover:bg-red-700 disabled:opacity-50"
                    >
                      <UserGroupIcon className="h-4 w-4" />
                      Follow
                    </button>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Followed athletes */}
      <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-gray-400">
        Followed Athletes ({followedAthletes.length})
      </h3>
      {followedAthletes.length === 0 ? (
        <p className="text-sm text-gray-500">
          No followed athletes yet. Search above and hit Follow.
        </p>
      ) : (
        <div className="space-y-2">
          {followedAthletes.map((athlete) => (
            <div key={athlete.id} className="rounded-lg border border-gray-800 bg-gray-900/60">
              <div className="flex items-center gap-3 p-3">
                <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center overflow-hidden rounded-full bg-black/50">
                  {athlete.thumbUrl ? (
                    <img src={athlete.thumbUrl} alt={athlete.name} className="h-full w-full object-cover" />
                  ) : (
                    <UserIcon className="h-5 w-5 text-gray-500" />
                  )}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium text-white">{athlete.name}</div>
                  <div className="truncate text-xs text-gray-400">
                    {athlete.sport ? `${getSportIcon(athlete.sport)} ${athlete.sport}` : ''}
                    {athlete.resolvedTeamName ? ` - ${athlete.resolvedTeamName}` : ''}
                  </div>
                </div>
                <button
                  onClick={() => discoverLeagues(athlete)}
                  className="inline-flex items-center gap-1 rounded-lg bg-gray-700 px-3 py-1.5 text-xs font-medium text-white transition-colors hover:bg-gray-600"
                >
                  {expandedAthleteId === athlete.id ? (
                    <ChevronUpIcon className="h-4 w-4" />
                  ) : (
                    <ChevronDownIcon className="h-4 w-4" />
                  )}
                  Leagues
                </button>
                <button
                  onClick={() => unfollowMutation.mutate(athlete.id)}
                  disabled={unfollowMutation.isPending}
                  className="rounded-lg bg-gray-800 p-1.5 text-gray-400 transition-colors hover:bg-red-900/40 hover:text-red-400"
                  title="Unfollow"
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              </div>

              {expandedAthleteId === athlete.id && (
                <div className="border-t border-gray-800 p-3">
                  {isDiscovering ? (
                    <p className="text-sm text-gray-400">Discovering leagues...</p>
                  ) : discovery == null ? null : discovery.mode === 'none' ? (
                    <p className="text-sm text-gray-400">{discovery.message}</p>
                  ) : (
                    <>
                      <p className="mb-2 text-xs text-gray-400">
                        {discovery.mode === 'events'
                          ? `${discovery.eventCount} events on record. Adding a league monitors exactly this athlete's events in it.`
                          : `Follows via current team ${discovery.resolvedTeam?.name ?? ''}. Adding a league monitors the team's events.`}
                      </p>
                      <div className="space-y-1">
                        {discovery.leagues.map((league) => (
                          <label
                            key={league.externalId}
                            className={`flex items-center gap-2 rounded p-2 text-sm ${league.isAdded ? 'text-gray-500' : 'cursor-pointer text-gray-200 hover:bg-gray-800/60'}`}
                          >
                            <input
                              type="checkbox"
                              disabled={league.isAdded}
                              checked={league.isAdded || selectedLeagueIds.has(league.externalId)}
                              onChange={(event) => {
                                setSelectedLeagueIds((prev) => {
                                  const next = new Set(prev);
                                  if (event.target.checked) next.add(league.externalId);
                                  else next.delete(league.externalId);
                                  return next;
                                });
                              }}
                              className="h-4 w-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                            />
                            <span className="flex-1 truncate">{league.name}</span>
                            <span className="text-xs text-gray-500">
                              {league.eventCount > 0 ? `${league.eventCount} events` : ''}
                            </span>
                            {league.isAdded && (
                              <span className="rounded bg-green-900/30 px-1.5 py-0.5 text-xs text-green-400">Added</span>
                            )}
                          </label>
                        ))}
                      </div>
                      {discovery.leagues.some((l) => !l.isAdded) && (
                        <div className="mt-3 flex flex-wrap items-center gap-2">
                          <select
                            value={qualityProfileId}
                            onChange={(event) => setQualityProfileId(Number.parseInt(event.target.value, 10))}
                            className="rounded-lg border border-gray-700 bg-gray-800 px-2 py-1.5 text-sm text-white focus:border-red-600 focus:outline-none"
                          >
                            {qualityProfiles.map((profile) => (
                              <option key={profile.id} value={profile.id}>
                                {profile.name}
                              </option>
                            ))}
                          </select>
                          <button
                            onClick={addLeagues}
                            disabled={isAdding || selectedLeagueIds.size === 0}
                            className="rounded-lg bg-red-600 px-4 py-1.5 text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
                          >
                            {isAdding ? 'Adding...' : `Add ${selectedLeagueIds.size} league${selectedLeagueIds.size === 1 ? '' : 's'}`}
                          </button>
                        </div>
                      )}
                    </>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
