import { useState, useEffect, useMemo, useRef, Fragment } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { MagnifyingGlassIcon, XMarkIcon, CheckIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import { apiGet } from '../utils/api';

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
    applyMonitoredPartsToEvents: boolean,
    monitoredSessionTypes: string | null
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

// Golf tournaments have all players competing together, not home/away teams
const isGolf = (sport: string) => {
  return sport.toLowerCase() === 'golf';
};

// Check if tennis league is individual-based (ATP, WTA tours) vs team-based (Fed Cup, Davis Cup, Olympics)
// Individual tennis leagues don't have meaningful team data - all events should sync
const isIndividualTennis = (sport: string, leagueName: string) => {
  if (sport.toLowerCase() !== 'tennis') return false;
  const nameLower = leagueName.toLowerCase();
  // Individual tours - no team selection needed
  const individualTours = ['atp', 'wta'];
  // Team-based competitions - team selection IS needed
  const teamBased = ['fed cup', 'davis cup', 'olympic', 'billie jean king'];
  // If it's a team-based league, it's NOT individual tennis
  if (teamBased.some(t => nameLower.includes(t))) return false;
  // If it contains ATP or WTA, it's individual tennis
  return individualTours.some(t => nameLower.includes(t));
};

// Get the appropriate part options based on sport type
// Only fighting sports use multi-part episodes
// Motorsports do NOT use multi-part - each session is a separate event from TheSportsDB
const getPartOptions = (sport: string): string[] => {
  if (isFightingSport(sport)) {
    return ['Early Prelims', 'Prelims', 'Main Card'];
  }
  // Motorsports and other sports don't have parts
  return [];
};

// Check if sport uses multi-part episodes
// Only fighting sports use multi-part episodes
const usesMultiPartEpisodes = (sport: string) => {
  return isFightingSport(sport);
};

export default function AddLeagueModal({ league, isOpen, onClose, onAdd, isAdding, editMode = false, leagueId }: AddLeagueModalProps) {
  const [selectedTeamIds, setSelectedTeamIds] = useState<Set<string>>(new Set());
  const [selectAll, setSelectAll] = useState(false);
  const [monitorType, setMonitorType] = useState('All');
  const [qualityProfileId, setQualityProfileId] = useState<number | null>(null);
  const [searchForMissingEvents, setSearchForMissingEvents] = useState(false);
  const [searchForCutoffUnmetEvents, setSearchForCutoffUnmetEvents] = useState(false);
  // For fighting sports: default to all parts selected
  const [monitoredParts, setMonitoredParts] = useState<Set<string>>(new Set());
  const [selectAllParts, setSelectAllParts] = useState(false);
  const [applyMonitoredPartsToEvents, setApplyMonitoredPartsToEvents] = useState(true);
  // For motorsports: session types to monitor (default to all selected)
  // Note: selectAllSessionTypes starts false to match empty Set, will be set true when availableSessionTypes loads
  const [monitoredSessionTypes, setMonitoredSessionTypes] = useState<Set<string>>(new Set());
  const [selectAllSessionTypes, setSelectAllSessionTypes] = useState(false);

  // enable team based filtering on league add --> teams to monitor
  const [searchQuery, setSearchQuery] = useState('');

  // Track initialization state to prevent re-initialization when queries complete
  // or other dependencies change. We track separately for teams and settings.
  // Store the data version (using a key that changes when data changes) to detect when fresh data arrives
  const initializedTeamsRef = useRef<boolean>(false);
  const initializedSettingsRef = useRef<boolean>(false);
  // Track which version of existingLeague data we've initialized from
  // This allows us to re-initialize when fresh data arrives after save
  const initializedDataVersionRef = useRef<string | null>(null);

  // Fetch teams for the league when modal opens (not for motorsports)
  const { data: teamsResponse, isLoading: isLoadingTeams } = useQuery({
    queryKey: ['league-teams', league?.idLeague],
    queryFn: async () => {
      if (!league?.idLeague) return null;
      const response = await apiGet(`/api/leagues/external/${league.idLeague}/teams`);
      if (!response.ok) throw new Error('Failed to fetch teams');
      return response.json();
    },
    enabled: isOpen && !!league && !isMotorsport(league.strSport) && !isGolf(league.strSport) && !isIndividualTennis(league.strSport, league.strLeague),
    staleTime: 5 * 60 * 1000,
  });

  const teams: Team[] = teamsResponse || [];

  // Fetch quality profiles
  const { data: qualityProfiles = [] } = useQuery({
    queryKey: ['quality-profiles'],
    queryFn: async () => {
      const response = await apiGet('/api/qualityprofile');
      if (!response.ok) throw new Error('Failed to fetch quality profiles');
      return response.json() as Promise<QualityProfile[]>;
    },
    staleTime: 5 * 60 * 1000,
  });

  // Fetch config to check if multi-part episodes are enabled
  const { data: config } = useQuery({
    queryKey: ['config'],
    queryFn: async () => {
      const response = await apiGet('/api/config');
      if (!response.ok) throw new Error('Failed to fetch config');
      return response.json() as Promise<{ enableMultiPartEpisodes: boolean }>;
    },
  });

  // Fetch motorsport session types for the league
  const { data: sessionTypesResponse } = useQuery({
    queryKey: ['motorsport-session-types', league?.strLeague],
    queryFn: async () => {
      if (!league?.strLeague) return [];
      const response = await apiGet(`/api/motorsport/session-types?leagueName=${encodeURIComponent(league.strLeague)}`);
      if (!response.ok) throw new Error('Failed to fetch session types');
      return response.json() as Promise<string[]>;
    },
    enabled: isOpen && !!league && isMotorsport(league.strSport),
    staleTime: 5 * 60 * 1000,
  });

  const availableSessionTypes: string[] = sessionTypesResponse || [];

  // Fetch existing league settings if in edit mode
  // IMPORTANT: Use string for query key to match LeagueDetailPage's useParams (which returns strings)
  // This ensures refetchQueries from parent components will refresh this data
  const leagueIdStr = leagueId?.toString();
  const { data: existingLeague } = useQuery({
    queryKey: ['league', leagueIdStr],
    queryFn: async () => {
      if (!leagueId) return null;
      const response = await apiGet(`/api/leagues/${leagueId}`);
      if (!response.ok) throw new Error('Failed to fetch league');
      return response.json();
    },
    enabled: isOpen && editMode && !!leagueId,
    refetchOnMount: 'always',
  });

  // Real-time filtering based on search query and selected team
  const filteredTeams = useMemo(() => {
    let filtered = teams;

    // Filter by search query, keeping previously selected teams in view
    if (searchQuery.trim()) {
      const query = searchQuery.toLowerCase();
      filtered = filtered.filter(team =>
        team.strTeam.toLowerCase().includes(query) ||
        team.strTeamShort?.toLowerCase().includes(query) ||
        selectedTeamIds.has(team.idTeam)
      );
    }
    return filtered;
  }, [teams, searchQuery]);

  // Load existing monitored teams when in edit mode (not for motorsports)
  // Only load once when existingLeague first becomes available
  useEffect(() => {
    if (editMode && isOpen && existingLeague && existingLeague.monitoredTeams && teams.length > 0 && league && !isMotorsport(league.strSport)) {
      // Only initialize teams once per modal open
      if (initializedTeamsRef.current) {
        return;
      }
      initializedTeamsRef.current = true;

      const monitoredExternalIds = existingLeague.monitoredTeams
        .filter((mt: any) => mt.monitored && mt.team)
        .map((mt: any) => mt.team.externalId);
      setSelectedTeamIds(new Set(monitoredExternalIds));
      setSelectAll(monitoredExternalIds.length === teams.length);
    }
  }, [editMode, isOpen, existingLeague, teams, league]);

  // Load existing monitoring settings when in edit mode
  // Re-initialize when fresh data arrives (detected by comparing data version)
  useEffect(() => {
    if (editMode && isOpen && existingLeague && league?.strSport) {
      // Create a version key from the data that changes when saved
      // Include key fields that can be modified to detect data changes
      // Also include availableSessionTypes.length to re-run when session types load
      const dataVersion = JSON.stringify({
        id: existingLeague.id,
        monitorType: existingLeague.monitorType,
        qualityProfileId: existingLeague.qualityProfileId,
        monitoredParts: existingLeague.monitoredParts,
        monitoredSessionTypes: existingLeague.monitoredSessionTypes,
        searchForMissingEvents: existingLeague.searchForMissingEvents,
        searchForCutoffUnmetEvents: existingLeague.searchForCutoffUnmetEvents,
        availableSessionTypesCount: availableSessionTypes.length, // Include to re-run when session types load
      });

      // Only skip if we've already initialized with THIS EXACT data version
      if (initializedSettingsRef.current && initializedDataVersionRef.current === dataVersion) {
        return;
      }
      initializedSettingsRef.current = true;
      initializedDataVersionRef.current = dataVersion;

      setMonitorType(existingLeague.monitorType || 'All');
      setQualityProfileId(existingLeague.qualityProfileId || null);
      setSearchForMissingEvents(existingLeague.searchForMissingEvents || false);
      setSearchForCutoffUnmetEvents(existingLeague.searchForCutoffUnmetEvents || false);

      // Load monitored parts (only for fighting sports)
      // null = all parts monitored (default)
      // "" (empty string) = no parts monitored
      // "Part1,Part2" = specific parts monitored
      if (isFightingSport(league.strSport)) {
        const availableParts = getPartOptions(league.strSport);
        if (existingLeague.monitoredParts === null || existingLeague.monitoredParts === undefined) {
          // null = all parts selected (default)
          setMonitoredParts(new Set(availableParts));
          setSelectAllParts(true);
        } else if (existingLeague.monitoredParts === '') {
          // Empty string = no parts selected
          setMonitoredParts(new Set());
          setSelectAllParts(false);
        } else {
          // Specific parts string
          const parts = existingLeague.monitoredParts.split(',').filter((p: string) => p.trim());
          setMonitoredParts(new Set(parts));
          setSelectAllParts(parts.length === availableParts.length);
        }
      }

      // Load monitored session types (only for motorsports with F1-style sessions)
      // null = all sessions monitored (default)
      // "" (empty string) = no sessions monitored
      // "Race,Qualifying" = specific sessions monitored
      if (isMotorsport(league.strSport) && availableSessionTypes.length > 0) {
        if (existingLeague.monitoredSessionTypes === null || existingLeague.monitoredSessionTypes === undefined) {
          // null = all sessions monitored (default)
          setMonitoredSessionTypes(new Set(availableSessionTypes));
          setSelectAllSessionTypes(true);
        } else if (existingLeague.monitoredSessionTypes === '') {
          // Empty string = no sessions selected
          setMonitoredSessionTypes(new Set());
          setSelectAllSessionTypes(false);
        } else {
          // Specific session types are selected
          const sessionTypes = existingLeague.monitoredSessionTypes.split(',').filter((s: string) => s.trim());
          setMonitoredSessionTypes(new Set(sessionTypes));
          setSelectAllSessionTypes(sessionTypes.length === availableSessionTypes.length);
        }
      }
    }
  }, [editMode, isOpen, existingLeague, league?.strSport, availableSessionTypes]);

  // Reset selection when modal opens with a NEW league (but NOT in edit mode)
  // Use ref to track initialization, preventing re-initialization when async queries complete
  useEffect(() => {
    // Only initialize for add mode (not edit mode) when modal is open
    if (!editMode && isOpen && league?.idLeague) {
      // Check if we've already initialized (use settingsRef for add mode too)
      if (initializedSettingsRef.current) {
        return; // Already initialized, don't reset state
      }

      // Mark as initialized
      initializedSettingsRef.current = true;

      // Reset state for new league
      setSelectedTeamIds(new Set());
      setSelectAll(false);
      setSearchQuery('');
      setMonitorType('Future');
      setQualityProfileId(qualityProfiles.length > 0 ? qualityProfiles[0].id : null);
      setSearchForMissingEvents(false);
      setSearchForCutoffUnmetEvents(false);

      // For fighting sports: default to all parts selected
      // Other sports (including motorsports) don't use parts
      if (isFightingSport(league.strSport)) {
        const defaultParts = getPartOptions(league.strSport);
        setMonitoredParts(new Set(defaultParts));
        setSelectAllParts(defaultParts.length > 0);
      } else {
        setMonitoredParts(new Set());
        setSelectAllParts(false);
      }

      // For motorsports: default to all session types selected
      if (isMotorsport(league.strSport) && availableSessionTypes.length > 0) {
        setMonitoredSessionTypes(new Set(availableSessionTypes));
        setSelectAllSessionTypes(true);
      } else {
        setMonitoredSessionTypes(new Set());
        setSelectAllSessionTypes(false);
      }
    }
  }, [league?.idLeague, league?.strSport, editMode, isOpen, qualityProfiles, availableSessionTypes]);

  // Clear initialization tracking when modal closes
  useEffect(() => {
    if (!isOpen) {
      initializedTeamsRef.current = false;
      initializedSettingsRef.current = false;
      initializedDataVersionRef.current = null;
    }
  }, [isOpen]);

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

  const handleSessionTypeToggle = (sessionType: string) => {
    setMonitoredSessionTypes(prev => {
      const newSet = new Set(prev);
      if (newSet.has(sessionType)) {
        newSet.delete(sessionType);
      } else {
        newSet.add(sessionType);
      }
      setSelectAllSessionTypes(newSet.size === availableSessionTypes.length);
      return newSet;
    });
  };

  const handleSelectAllSessionTypes = () => {
    if (selectAllSessionTypes) {
      setMonitoredSessionTypes(new Set());
      setSelectAllSessionTypes(false);
    } else {
      setMonitoredSessionTypes(new Set(availableSessionTypes));
      setSelectAllSessionTypes(true);
    }
  };

  const handleAdd = () => {
    if (!league) return;

    const monitoredTeamIds = Array.from(selectedTeamIds);
    const availableParts = getPartOptions(league.strSport);

    // Only fighting sports use multi-part episodes
    // Motorsports do NOT use multi-part - each session is a separate event from TheSportsDB
    // null = all parts monitored, "" = no parts monitored, "Part1,Part2" = specific parts
    let partsString: string | null = null;
    if (config?.enableMultiPartEpisodes && isFightingSport(league.strSport)) {
      if (monitoredParts.size === availableParts.length) {
        partsString = null; // All selected = null (monitor all)
      } else if (monitoredParts.size === 0) {
        partsString = ''; // None selected = empty string (monitor none)
      } else {
        partsString = Array.from(monitoredParts).join(','); // Specific parts
      }
    }

    // For motorsports: session types to monitor
    // null = all sessions monitored, "" = no sessions monitored, "Race,Qualifying" = specific sessions
    let sessionTypesString: string | null = null;
    if (isMotorsport(league.strSport) && availableSessionTypes.length > 0) {
      if (monitoredSessionTypes.size === availableSessionTypes.length) {
        sessionTypesString = null; // All selected = null (monitor all)
      } else if (monitoredSessionTypes.size === 0) {
        sessionTypesString = ''; // None selected = empty string (monitor none)
      } else {
        sessionTypesString = Array.from(monitoredSessionTypes).join(','); // Specific sessions
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
      applyMonitoredPartsToEvents,
      sessionTypesString
    );
  };

  // Calculate derived values only when league exists
  const selectedCount = selectedTeamIds.size;
  const logoUrl = league?.strBadge || league?.strLogo;
  const availableParts = league ? getPartOptions(league.strSport) : [];
  const selectedPartsCount = monitoredParts.size;
  const selectedSessionTypesCount = monitoredSessionTypes.size;
  // Show team selection for leagues with meaningful team data
  // Skip for: Motorsport (no home/away teams) and individual Tennis (ATP, WTA - matches are between players)
  const showTeamSelection = league ? !isMotorsport(league.strSport) && !isGolf(league.strSport) && !isIndividualTennis(league.strSport, league.strLeague) : false;
  // Only fighting sports use multi-part episodes
  const showPartsSelection = config?.enableMultiPartEpisodes && league && isFightingSport(league.strSport);
  // Show session type selection for motorsports
  const showSessionTypeSelection = league && isMotorsport(league.strSport) && availableSessionTypes.length > 0;

  // Always render Transition to ensure cleanup callback runs
  // Use isOpen AND league existence to control visibility
  return (
    <Transition
      appear
      show={isOpen && !!league}
      as={Fragment}
      unmount={true}
      afterLeave={() => {
        // Safety net: remove any lingering inert attributes
        document.querySelectorAll('[inert]').forEach((el) => {
          el.removeAttribute('inert');
        });
      }}
    >
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
              <Dialog.Panel className="w-full max-w-4xl mx-2 md:mx-4 transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 text-left align-middle shadow-xl transition-all">
                {/* Header */}
                <div className="border-b border-red-900/30 p-4 md:p-6">
                  <div className="flex items-start justify-between">
                    <div className="flex items-center gap-3 md:gap-4 min-w-0 flex-1">
                      {logoUrl && (
                        <img
                          src={logoUrl}
                          alt={league?.strLeague || 'League'}
                          className="w-10 h-10 md:w-16 md:h-16 object-contain flex-shrink-0"
                        />
                      )}
                      <div className="min-w-0">
                        <Dialog.Title as="h3" className="text-lg md:text-2xl font-bold text-white truncate">
                          {editMode ? 'Edit ' : 'Add '}{league?.strLeague || ''}
                        </Dialog.Title>
                        <div className="flex flex-wrap items-center gap-2 mt-1">
                          <span className="px-2 py-0.5 md:py-1 bg-red-600/20 text-red-400 text-xs rounded font-medium">
                            {league?.strSport || ''}
                          </span>
                          {league?.strCountry && (
                            <span className="text-xs md:text-sm text-gray-400">{league.strCountry}</span>
                          )}
                        </div>
                      </div>
                    </div>
                    <button
                      onClick={onClose}
                      className="text-gray-400 hover:text-white transition-colors flex-shrink-0 ml-2"
                    >
                      <XMarkIcon className="w-5 h-5 md:w-6 md:h-6" />
                    </button>
                  </div>
                </div>

                {/* Team Selection (for non-motorsport leagues) */}
                {showTeamSelection && (
                  <div className="p-4 md:p-6">
                    <div className="mb-3 md:mb-4">
                      <h4 className="text-base md:text-lg font-semibold text-white mb-1 md:mb-2">
                        Select Teams to Monitor
                      </h4>
                      <p className="text-xs md:text-sm text-gray-400">
                        Choose which teams you want to follow. Only events involving selected teams will be synced.
                        {teams.length > 0 && selectedCount === 0 && (
                          <span className="text-yellow-500"> No teams selected = league will not be monitored.</span>
                        )}
                        {teams.length === 0 && !isLoadingTeams && (
                          <span className="text-green-400"> No team data available - all events will be monitored.</span>
                        )}
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

                        {teams.length >= 25 && (
                          <div className="mb-4">
                            <div className="relative">
                              <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
                              <input
                                type="text"
                                value={searchQuery}
                                onChange={(e) => setSearchQuery(e.target.value)}
                                placeholder="Filter Teams (e.g. Kansas City, Detroit, Liverpool)..."
                                className="w-full pl-10 pr-4 py-3 bg-black border border-red-900/30 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
                              />
                            </div>
                          </div>
                        )}

                        {/* Team Grid */}
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-3 max-h-96 overflow-y-auto">
                          {filteredTeams.map(team => {
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

                {/* Session Type Selection (for Motorsports) */}
                {showSessionTypeSelection && (
                  <div className="p-6">
                    <div className="mb-4">
                      <h4 className="text-lg font-semibold text-white mb-2">
                        Select Session Types to Monitor
                      </h4>
                      <p className="text-sm text-gray-400">
                        Choose which types of sessions you want to monitor. Each session is a separate event.
                        {selectedSessionTypesCount === 0 && (
                          <span className="text-yellow-500"> No sessions selected = none will be monitored.</span>
                        )}
                      </p>
                    </div>

                    {/* Select All */}
                    <div className="mb-4 p-3 bg-black/50 rounded-lg border border-red-900/20">
                      <button
                        onClick={handleSelectAllSessionTypes}
                        className="flex items-center justify-between w-full text-left"
                      >
                        <span className="font-medium text-white">
                          {selectAllSessionTypes ? 'Deselect All' : 'Select All'} ({availableSessionTypes.length} session types)
                        </span>
                        <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-colors ${
                          selectAllSessionTypes ? 'bg-red-600 border-red-600' : 'border-gray-600'
                        }`}>
                          {selectAllSessionTypes && <CheckIcon className="w-4 h-4 text-white" />}
                        </div>
                      </button>
                    </div>

                    {/* Session Type Grid */}
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      {availableSessionTypes.map((sessionType) => {
                        const isSelected = monitoredSessionTypes.has(sessionType);
                        return (
                          <button
                            key={sessionType}
                            onClick={() => handleSessionTypeToggle(sessionType)}
                            className={`flex items-center gap-3 p-3 rounded-lg border transition-all text-left ${
                              isSelected
                                ? 'bg-red-600/20 border-red-600'
                                : 'bg-black/30 border-gray-700 hover:border-gray-600'
                            }`}
                          >
                            <div className="flex-1">
                              <div className="font-medium text-white">{sessionType}</div>
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
                        Select which parts of fight cards to monitor. Unselected parts will not be searched.
                        {editMode && ' Changes will apply to all existing events in this league.'}
                      </p>
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
                    {editMode && (
                      <p className="text-xs text-gray-400 mt-2">
                        Changes will apply to all events in this league.
                      </p>
                    )}
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
                      {showSessionTypeSelection ? (
                        selectedSessionTypesCount > 0 ? (
                          <span>
                            <span className="font-semibold text-white">{selectedSessionTypesCount}</span> session type{selectedSessionTypesCount !== 1 ? 's' : ''} selected
                          </span>
                        ) : (
                          <span className="text-yellow-500">No session types selected - no events will be monitored</span>
                        )
                      ) : showTeamSelection ? (
                        teams.length === 0 ? (
                          <span>All events will be monitored (no team data available)</span>
                        ) : selectedCount > 0 ? (
                          <span>
                            <span className="font-semibold text-white">{selectedCount}</span> team{selectedCount !== 1 ? 's' : ''} selected
                          </span>
                        ) : (
                          <span className="text-yellow-500">No teams selected - league will not be monitored</span>
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
