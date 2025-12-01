import { useState, useEffect, Fragment } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon, CheckIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import apiClient from '../api/client';

interface Team {
  idTeam: string;
  strTeam: string;
  strTeamBadge?: string;
  strTeamShort?: string;
}

interface League {
  idLeague: string;
  strLeague: string;
  strSport: string;
  strCountry?: string;
  strLeagueAlternate?: string;
  strDescriptionEN?: string;
  strBadge?: string;
  strLogo?: string;
  strBanner?: string;
  strPoster?: string;
  strWebsite?: string;
  intFormedYear?: string;
}

interface QualityProfile {
  id: number;
  name: string;
}

interface AddLeagueModalProps {
  league: League | null;
  isOpen: boolean;
  onClose: () => void;
  onAdd: (
    league: League,
    monitoredTeamIds: string[],
    monitorType: string,
    qualityProfileId: number | null,
    searchForMissingEvents: boolean,
    searchForCutoffUnmetEvents: boolean,
    monitoredParts: string | null,
    applyMonitoredPartsToEvents: boolean
  ) => void;
  isAdding: boolean;
  editMode?: boolean;
  leagueId?: number | null;
}

// Helper functions defined outside component to avoid hoisting issues
const isFightingSport = (sport: string) => {
  const fightingSports = ['Fighting', 'MMA', 'UFC', 'Boxing', 'Kickboxing', 'Wrestling'];
  return fightingSports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
};

const isMotorsport = (sport: string) => {
  const motorsports = [
    'Motorsport', 'Racing', 'Formula 1', 'F1', 'NASCAR', 'IndyCar',
    'MotoGP', 'WEC', 'Formula E', 'Rally', 'WRC', 'DTM', 'Super GT',
    'IMSA', 'V8 Supercars', 'Supercars', 'Le Mans'
  ];
  return motorsports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
};

// Get the appropriate part options based on sport type
const getPartOptions = (sport: string): string[] => {
  if (isFightingSport(sport)) {
    return ['Early Prelims', 'Prelims', 'Main Card'];
  }
  if (isMotorsport(sport)) {
    return ['Pre-Season Testing', 'Practice', 'Qualifying', 'Sprint', 'Race'];
  }
  return [];
};

// Check if sport uses multi-part episodes
const usesMultiPartEpisodes = (sport: string) => {
  return isFightingSport(sport) || isMotorsport(sport);
};

export default function AddLeagueModal({ league, isOpen, onClose, onAdd, isAdding, editMode = false, leagueId }: AddLeagueModalProps) {
  const [selectedTeamIds, setSelectedTeamIds] = useState<Set<string>>(new Set());
  const [selectAll, setSelectAll] = useState(false);
  const [monitorType, setMonitorType] = useState('Future');
  const [qualityProfileId, setQualityProfileId] = useState<number | null>(null);
  const [searchForMissingEvents, setSearchForMissingEvents] = useState(false);
  const [searchForCutoffUnmetEvents, setSearchForCutoffUnmetEvents] = useState(false);
  // For motorsports: default to no sessions selected (none monitored by default)
  // For fighting sports: default to all parts selected
  const [monitoredParts, setMonitoredParts] = useState<Set<string>>(new Set());
  const [selectAllParts, setSelectAllParts] = useState(false);
  const [applyMonitoredPartsToEvents, setApplyMonitoredPartsToEvents] = useState(true);

  // Fetch teams for the league when modal opens (not for motorsports)
  const { data: teamsResponse, isLoading: isLoadingTeams } = useQuery({
    queryKey: ['league-teams', league?.idLeague],
    queryFn: async () => {
      if (!league?.idLeague) return null;
      const response = await fetch(`/api/leagues/external/${league.idLeague}/teams`);
      if (!response.ok) throw new Error('Failed to fetch teams');
      return response.json();
    },
    enabled: isOpen && !!league && !isMotorsport(league.strSport),
    staleTime: 5 * 60 * 1000,
  });

  const teams: Team[] = teamsResponse || [];

  // Fetch quality profiles
  const { data: qualityProfiles = [] } = useQuery({
    queryKey: ['quality-profiles'],
    queryFn: async () => {
      const response = await fetch('/api/qualityprofile');
      if (!response.ok) throw new Error('Failed to fetch quality profiles');
      return response.json() as Promise<QualityProfile[]>;
    },
    staleTime: 5 * 60 * 1000,
  });

  // Fetch config to check if multi-part episodes are enabled
  const { data: config } = useQuery({
    queryKey: ['config'],
    queryFn: async () => {
      const response = await apiClient.get<{ enableMultiPartEpisodes: boolean }>('/config');
      return response.data;
    },
  });

  // Fetch existing league settings if in edit mode
  const { data: existingLeague } = useQuery({
    queryKey: ['league', leagueId],
    queryFn: async () => {
      if (!leagueId) return null;
      const response = await fetch(`/api/leagues/${leagueId}`);
      if (!response.ok) throw new Error('Failed to fetch league');
      return response.json();
    },
    enabled: isOpen && editMode && !!leagueId,
    refetchOnMount: 'always',
  });

  // Load existing monitored teams when in edit mode (not for motorsports)
  useEffect(() => {
    if (editMode && existingLeague && existingLeague.monitoredTeams && teams.length > 0 && league && !isMotorsport(league.strSport)) {
      const monitoredExternalIds = existingLeague.monitoredTeams
        .filter((mt: any) => mt.monitored && mt.team)
        .map((mt: any) => mt.team.externalId);
      setSelectedTeamIds(new Set(monitoredExternalIds));
      setSelectAll(monitoredExternalIds.length === teams.length);
    }
  }, [editMode, existingLeague, teams, league]);

  // Load existing monitoring settings when in edit mode
  useEffect(() => {
    if (editMode && existingLeague) {
      setMonitorType(existingLeague.monitorType || 'Future');
      setQualityProfileId(existingLeague.qualityProfileId || null);
      setSearchForMissingEvents(existingLeague.searchForMissingEvents || false);
      setSearchForCutoffUnmetEvents(existingLeague.searchForCutoffUnmetEvents || false);

      // Load monitored parts
      if (existingLeague.monitoredParts) {
        const parts = existingLeague.monitoredParts.split(',').filter((p: string) => p.trim());
        setMonitoredParts(new Set(parts));
        if (league?.strSport) {
          const availableParts = getPartOptions(league.strSport);
          setSelectAllParts(parts.length === availableParts.length);
        }
      } else if (league?.strSport) {
        // For motorsports: no parts = none selected
        // For fighting: no parts = all selected (default behavior)
        if (isMotorsport(league.strSport)) {
          setMonitoredParts(new Set());
          setSelectAllParts(false);
        } else {
          const defaultParts = getPartOptions(league.strSport);
          setMonitoredParts(new Set(defaultParts));
          setSelectAllParts(true);
        }
      }
    }
  }, [editMode, existingLeague, league?.strSport]);

  // Reset selection when league changes (but NOT in edit mode)
  useEffect(() => {
    if (!editMode && league?.strSport) {
      setSelectedTeamIds(new Set());
      setSelectAll(false);
      setMonitorType('Future');
      setQualityProfileId(qualityProfiles.length > 0 ? qualityProfiles[0].id : null);
      setSearchForMissingEvents(false);
      setSearchForCutoffUnmetEvents(false);

      // For motorsports: default to no sessions selected
      // For fighting sports: default to all parts selected
      if (isMotorsport(league.strSport)) {
        setMonitoredParts(new Set());
        setSelectAllParts(false);
      } else {
        const defaultParts = getPartOptions(league.strSport);
        setMonitoredParts(new Set(defaultParts));
        setSelectAllParts(defaultParts.length > 0);
      }
    }
  }, [league?.idLeague, league?.strSport, editMode, qualityProfiles]);

  const handleTeamToggle = (teamId: string) => {
    setSelectedTeamIds(prev => {
      const newSet = new Set(prev);
      if (newSet.has(teamId)) {
        newSet.delete(teamId);
      } else {
        newSet.add(teamId);
      }
      return newSet;
    });
  };

  const handleSelectAll = () => {
    if (selectAll) {
      setSelectedTeamIds(new Set());
      setSelectAll(false);
    } else {
      setSelectedTeamIds(new Set(teams.map(t => t.idTeam)));
      setSelectAll(true);
    }
  };

  const handlePartToggle = (part: string) => {
    setMonitoredParts(prev => {
      const newSet = new Set(prev);
      if (newSet.has(part)) {
        newSet.delete(part);
      } else {
        newSet.add(part);
      }
      if (league?.strSport) {
        const availableParts = getPartOptions(league.strSport);
        setSelectAllParts(newSet.size === availableParts.length);
      }
      return newSet;
    });
  };

  const handleSelectAllParts = () => {
    if (!league?.strSport) return;
    const availableParts = getPartOptions(league.strSport);

    if (selectAllParts) {
      setMonitoredParts(new Set());
      setSelectAllParts(false);
    } else {
      setMonitoredParts(new Set(availableParts));
      setSelectAllParts(true);
    }
  };

  const handleAdd = () => {
    if (!league) return;

    const monitoredTeamIds = Array.from(selectedTeamIds);
    const availableParts = getPartOptions(league.strSport);

    // For motorsports: null if no sessions selected
    // For fighting sports: null if all parts selected (means all monitored)
    let partsString: string | null = null;
    if (config?.enableMultiPartEpisodes && usesMultiPartEpisodes(league.strSport)) {
      if (isMotorsport(league.strSport)) {
        // Motorsports: empty = null = none monitored
        partsString = monitoredParts.size > 0 ? Array.from(monitoredParts).join(',') : null;
      } else {
        // Fighting sports: all selected = null (monitor all), otherwise list specific parts
        partsString = monitoredParts.size === availableParts.length ? null : Array.from(monitoredParts).join(',');
      }
    }

    onAdd(
      league,
      monitoredTeamIds,
      monitorType,
      qualityProfileId,
      searchForMissingEvents,
      searchForCutoffUnmetEvents,
      partsString,
      applyMonitoredPartsToEvents
    );
  };

  if (!league) return null;

  const selectedCount = selectedTeamIds.size;
  const logoUrl = league.strBadge || league.strLogo;
  const availableParts = getPartOptions(league.strSport);
  const selectedPartsCount = monitoredParts.size;
  const showSessionSelection = config?.enableMultiPartEpisodes && isMotorsport(league.strSport);
  const showTeamSelection = !isMotorsport(league.strSport);
  const showPartsSelection = config?.enableMultiPartEpisodes && usesMultiPartEpisodes(league.strSport) && !isMotorsport(league.strSport);

  return (
    <Transition appear show={isOpen} as={Fragment}>
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

        <div className="fixed inset-0 overflow-y-auto">
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
              <Dialog.Panel className="w-full max-w-4xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 text-left align-middle shadow-xl transition-all">
                {/* Header */}
                <div className="border-b border-red-900/30 p-6">
                  <div className="flex items-start justify-between">
                    <div className="flex items-center gap-4">
                      {logoUrl && (
                        <img
                          src={logoUrl}
                          alt={league.strLeague}
                          className="w-16 h-16 object-contain"
                        />
                      )}
                      <div>
                        <Dialog.Title as="h3" className="text-2xl font-bold text-white">
                          {editMode ? 'Edit ' : 'Add '}{league.strLeague}
                        </Dialog.Title>
                        <div className="flex items-center gap-2 mt-1">
                          <span className="px-2 py-1 bg-red-600/20 text-red-400 text-xs rounded font-medium">
                            {league.strSport}
                          </span>
                          {league.strCountry && (
                            <span className="text-sm text-gray-400">{league.strCountry}</span>
                          )}
                        </div>
                      </div>
                    </div>
                    <button
                      onClick={onClose}
                      className="text-gray-400 hover:text-white transition-colors"
                    >
                      <XMarkIcon className="w-6 h-6" />
                    </button>
                  </div>
                </div>

                {/* Team Selection (for non-motorsport leagues) */}
                {showTeamSelection && (
                  <div className="p-6">
                    <div className="mb-4">
                      <h4 className="text-lg font-semibold text-white mb-2">
                        Select Teams to Monitor
                      </h4>
                      <p className="text-sm text-gray-400">
                        Choose which teams you want to follow. Only events involving selected teams will be synced.
                        {selectedCount === 0 && ' No teams selected = league will not be monitored.'}
                      </p>
                    </div>

                    {/* Loading State */}
                    {isLoadingTeams && (
                      <div className="flex flex-col items-center justify-center py-12">
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mb-4"></div>
                        <p className="text-gray-400">Loading teams...</p>
                      </div>
                    )}

                    {/* Teams List */}
                    {!isLoadingTeams && teams.length > 0 && (
                      <>
                        {/* Select All */}
                        <div className="mb-4 p-3 bg-black/50 rounded-lg border border-red-900/20">
                          <button
                            onClick={handleSelectAll}
                            className="flex items-center justify-between w-full text-left"
                          >
                            <span className="font-medium text-white">
                              {selectAll ? 'Deselect All' : 'Select All'} ({teams.length} teams)
                            </span>
                            <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-colors ${
                              selectAll ? 'bg-red-600 border-red-600' : 'border-gray-600'
                            }`}>
                              {selectAll && <CheckIcon className="w-4 h-4 text-white" />}
                            </div>
                          </button>
                        </div>

                        {/* Team Grid */}
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-3 max-h-96 overflow-y-auto">
                          {teams.map(team => {
                            const isSelected = selectedTeamIds.has(team.idTeam);
                            return (
                              <button
                                key={team.idTeam}
                                onClick={() => handleTeamToggle(team.idTeam)}
                                className={`flex items-center gap-3 p-3 rounded-lg border transition-all text-left ${
                                  isSelected
                                    ? 'bg-red-600/20 border-red-600'
                                    : 'bg-black/30 border-gray-700 hover:border-gray-600'
                                }`}
                              >
                                {team.strTeamBadge && (
                                  <img
                                    src={team.strTeamBadge}
                                    alt={team.strTeam}
                                    className="w-10 h-10 object-contain"
                                  />
                                )}
                                <div className="flex-1">
                                  <div className="font-medium text-white">{team.strTeam}</div>
                                  {team.strTeamShort && (
                                    <div className="text-xs text-gray-400">{team.strTeamShort}</div>
                                  )}
                                </div>
                                <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-colors ${
                                  isSelected ? 'bg-red-600 border-red-600' : 'border-gray-600'
                                }`}>
                                  {isSelected && <CheckIcon className="w-4 h-4 text-white" />}
                                </div>
                              </button>
                            );
                          })}
                        </div>
                      </>
                    )}

                    {/* No Teams */}
                    {!isLoadingTeams && teams.length === 0 && (
                      <div className="text-center py-12">
                        <p className="text-gray-400">
                          No teams found for this league. All events will be monitored.
                        </p>
                      </div>
                    )}
                  </div>
                )}

                {/* Session Selection (for Motorsports only) */}
                {showSessionSelection && (
                  <div className="p-6">
                    <div className="mb-4">
                      <h4 className="text-lg font-semibold text-white mb-2">
                        Select Sessions to Monitor
                      </h4>
                      <p className="text-sm text-gray-400">
                        Choose which racing sessions you want to download. Only selected sessions will be monitored.
                        {selectedPartsCount === 0 && (
                          <span className="text-yellow-500"> No sessions selected = none will be monitored.</span>
                        )}
                      </p>
                    </div>

                    {/* Select All */}
                    <div className="mb-4 p-3 bg-black/50 rounded-lg border border-red-900/20">
                      <button
                        onClick={handleSelectAllParts}
                        className="flex items-center justify-between w-full text-left"
                      >
                        <span className="font-medium text-white">
                          {selectAllParts ? 'Deselect All' : 'Select All'} ({availableParts.length} sessions)
                        </span>
                        <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-colors ${
                          selectAllParts ? 'bg-red-600 border-red-600' : 'border-gray-600'
                        }`}>
                          {selectAllParts && <CheckIcon className="w-4 h-4 text-white" />}
                        </div>
                      </button>
                    </div>

                    {/* Session Grid */}
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      {availableParts.map((part, index) => {
                        const isSelected = monitoredParts.has(part);
                        const partNumber = index + 1;
                        return (
                          <button
                            key={part}
                            onClick={() => handlePartToggle(part)}
                            className={`flex items-center gap-3 p-3 rounded-lg border transition-all text-left ${
                              isSelected
                                ? 'bg-red-600/20 border-red-600'
                                : 'bg-black/30 border-gray-700 hover:border-gray-600'
                            }`}
                          >
                            <div className="w-10 h-10 rounded-lg bg-gray-800 flex items-center justify-center text-lg font-bold text-gray-400">
                              {partNumber}
                            </div>
                            <div className="flex-1">
                              <div className="font-medium text-white">{part}</div>
                              <div className="text-xs text-gray-400">pt{partNumber}</div>
                            </div>
                            <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-colors ${
                              isSelected ? 'bg-red-600 border-red-600' : 'border-gray-600'
                            }`}>
                              {isSelected && <CheckIcon className="w-4 h-4 text-white" />}
                            </div>
                          </button>
                        );
                      })}
                    </div>
                  </div>
                )}

                {/* Monitoring Options */}
                <div className="px-6 pb-6 border-t border-red-900/20 pt-6">
                  <h4 className="text-lg font-semibold text-white mb-4">
                    Monitoring Options
                  </h4>

                  {/* Monitor Type */}
                  <div className="mb-4">
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Monitor Events
                    </label>
                    <select
                      value={monitorType}
                      onChange={(e) => setMonitorType(e.target.value)}
                      className="w-full px-3 py-2 bg-black border border-red-900/30 rounded-lg text-white focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
                    >
                      <option value="All">All Events (past, present, and future)</option>
                      <option value="Future">Future Events (events that haven't occurred yet)</option>
                      <option value="CurrentSeason">Current Season Only</option>
                      <option value="LatestSeason">Latest Season Only</option>
                      <option value="NextSeason">Next Season Only</option>
                      <option value="Recent">Recent Events (last 30 days)</option>
                      <option value="None">None (manual monitoring only)</option>
                    </select>
                  </div>

                  {/* Monitor Parts (Fighting Sports - shown in monitoring options) */}
                  {showPartsSelection && (
                    <div className="mb-4">
                      <label className="block text-sm font-medium text-gray-300 mb-2">
                        Monitor Parts
                      </label>
                      <div className="space-y-2">
                        {availableParts.map((part) => (
                          <label key={part} className="flex items-center gap-3 cursor-pointer">
                            <input
                              type="checkbox"
                              checked={monitoredParts.has(part)}
                              onChange={() => handlePartToggle(part)}
                              className="w-5 h-5 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2"
                            />
                            <span className="text-sm text-white">{part}</span>
                          </label>
                        ))}
                      </div>
                      <p className="text-xs text-gray-400 mt-2">
                        Select which parts of fight cards to monitor. Unselected parts will not be automatically downloaded.
                      </p>
                      {editMode && (
                        <label className="flex items-center gap-3 cursor-pointer mt-3 p-3 bg-red-900/10 rounded-lg border border-red-900/30">
                          <input
                            type="checkbox"
                            checked={applyMonitoredPartsToEvents}
                            onChange={(e) => setApplyMonitoredPartsToEvents(e.target.checked)}
                            className="w-5 h-5 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2"
                          />
                          <div>
                            <div className="text-sm font-medium text-white">Apply to all existing events</div>
                            <div className="text-xs text-gray-400">
                              Update monitored parts for all existing events in this league
                            </div>
                          </div>
                        </label>
                      )}
                    </div>
                  )}

                  {/* Apply to existing events option (only in edit mode for motorsports) */}
                  {editMode && showSessionSelection && (
                    <div className="mb-4">
                      <label className="flex items-center gap-3 cursor-pointer p-3 bg-red-900/10 rounded-lg border border-red-900/30">
                        <input
                          type="checkbox"
                          checked={applyMonitoredPartsToEvents}
                          onChange={(e) => setApplyMonitoredPartsToEvents(e.target.checked)}
                          className="w-5 h-5 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2"
                        />
                        <div>
                          <div className="text-sm font-medium text-white">Apply to all existing events</div>
                          <div className="text-xs text-gray-400">
                            Update monitored sessions for all existing events in this league
                          </div>
                        </div>
                      </label>
                    </div>
                  )}

                  {/* Quality Profile */}
                  <div className="mb-4">
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Quality Profile
                    </label>
                    <select
                      value={qualityProfileId || ''}
                      onChange={(e) => setQualityProfileId(e.target.value ? parseInt(e.target.value) : null)}
                      className="w-full px-3 py-2 bg-black border border-red-900/30 rounded-lg text-white focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
                    >
                      <option value="">No Quality Profile</option>
                      {qualityProfiles.map(profile => (
                        <option key={profile.id} value={profile.id}>
                          {profile.name}
                        </option>
                      ))}
                    </select>
                  </div>

                  {/* Search Options */}
                  <div className="space-y-3">
                    <label className="flex items-center gap-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={searchForMissingEvents}
                        onChange={(e) => setSearchForMissingEvents(e.target.checked)}
                        className="w-5 h-5 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2"
                      />
                      <div>
                        <div className="text-sm font-medium text-white">Search on add/update</div>
                        <div className="text-xs text-gray-400">Automatically search when league is added or settings change</div>
                      </div>
                    </label>

                    <label className="flex items-center gap-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={searchForCutoffUnmetEvents}
                        onChange={(e) => setSearchForCutoffUnmetEvents(e.target.checked)}
                        className="w-5 h-5 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2"
                      />
                      <div>
                        <div className="text-sm font-medium text-white">Search for upgrades on add/update</div>
                        <div className="text-xs text-gray-400">Search for quality upgrades when league is added or settings change</div>
                      </div>
                    </label>
                  </div>
                </div>

                {/* Footer */}
                <div className="border-t border-red-900/30 p-6 bg-black/30">
                  <div className="flex items-center justify-between">
                    <div className="text-sm text-gray-400">
                      {showSessionSelection ? (
                        selectedPartsCount > 0 ? (
                          <span>
                            <span className="font-semibold text-white">{selectedPartsCount}</span> session{selectedPartsCount !== 1 ? 's' : ''} selected
                          </span>
                        ) : (
                          <span className="text-yellow-500">No sessions selected - none will be monitored</span>
                        )
                      ) : showTeamSelection ? (
                        selectedCount > 0 ? (
                          <span>
                            <span className="font-semibold text-white">{selectedCount}</span> team{selectedCount !== 1 ? 's' : ''} selected
                          </span>
                        ) : (
                          <span>No teams selected - league will not be monitored</span>
                        )
                      ) : (
                        <span>All events will be monitored</span>
                      )}
                    </div>
                    <div className="flex gap-3">
                      <button
                        onClick={onClose}
                        disabled={isAdding}
                        className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        Cancel
                      </button>
                      <button
                        onClick={handleAdd}
                        disabled={isAdding || (showTeamSelection && isLoadingTeams)}
                        className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        {isAdding ? (editMode ? 'Updating...' : 'Adding...') : (editMode ? 'Update' : 'Add to Library')}
                      </button>
                    </div>
                  </div>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
