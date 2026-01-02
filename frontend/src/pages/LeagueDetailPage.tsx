import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowLeftIcon, MagnifyingGlassIcon, ChevronDownIcon, ChevronRightIcon, UserIcon, ArrowPathIcon, UsersIcon, TrashIcon, FilmIcon, FolderOpenIcon, ExclamationTriangleIcon, SignalIcon, VideoCameraIcon } from '@heroicons/react/24/outline';
import { CheckCircleIcon, XCircleIcon } from '@heroicons/react/24/solid';
import { useState, useEffect, useRef, useMemo } from 'react';
import apiClient from '../api/client';
import { toast } from 'sonner';
import ManualSearchModal from '../components/ManualSearchModal';
import SeasonSearchModal from '../components/SeasonSearchModal';
import AddLeagueModal from '../components/AddLeagueModal';
import ConfirmationModal from '../components/ConfirmationModal';
import EventFileDetailModal from '../components/EventFileDetailModal';
import LeagueFilesModal from '../components/LeagueFilesModal';
import EventStatusBadge from '../components/EventStatusBadge';
import { useSearchQueueStatus, useDownloadQueue } from '../api/hooks';
import { useTimezone } from '../hooks/useTimezone';
import { formatDateInTimezone } from '../utils/timezone';

// Type for the league prop passed to AddLeagueModal
interface ModalLeagueData {
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

interface MonitoredTeamInfo {
  id: number;
  leagueId: number;
  teamId: number;
  monitored: boolean;
  added: string;
  team?: {
    id: number;
    externalId?: string;
    name: string;
    shortName?: string;
    badgeUrl?: string;
  };
}

interface LeagueDetail {
  id: number;
  externalId?: string;
  name: string;
  sport: string;
  country?: string;
  description?: string;
  monitored: boolean;
  monitorType?: string;
  qualityProfileId?: number;
  searchForMissingEvents?: boolean;
  searchForCutoffUnmetEvents?: boolean;
  monitoredParts?: string;
  monitoredSessionTypes?: string;
  logoUrl?: string;
  bannerUrl?: string;
  posterUrl?: string;
  website?: string;
  formedYear?: number;
  added: string;
  lastUpdate?: string;
  eventCount: number;
  monitoredEventCount: number;
  fileCount: number;
  monitoredTeams?: MonitoredTeamInfo[];
}

interface EventFile {
  id: number;
  eventId: number;
  filePath: string;
  size: number;
  quality?: string;
  qualityScore?: number;
  customFormatScore?: number;
  codec?: string;
  source?: string;
  partName?: string;
  partNumber?: number;
  added: string;
  exists: boolean;
  originalTitle?: string;
}

// Part-level status for multi-part episodes (fighting sports)
interface PartStatus {
  partName: string;
  partNumber: number;
  monitored: boolean;
  downloaded: boolean;
  file?: EventFile;
}

interface EventDetail {
  id: number;
  externalId?: string;
  title: string;
  sport: string;
  leagueId?: number;
  leagueName?: string;
  homeTeamId?: number;
  homeTeamName?: string;
  awayTeamId?: number;
  awayTeamName?: string;
  season?: string;
  seasonNumber?: number;
  episodeNumber?: number;
  round?: string;
  eventDate: string;
  venue?: string;
  location?: string;
  broadcast?: string;
  monitored: boolean;
  monitoredParts?: string;
  hasFile: boolean;
  filePath?: string;
  fileSize?: number;
  quality?: string;
  qualityProfileId?: number;
  images: string[];
  added: string;
  lastUpdate?: string;
  homeScore?: string;
  awayScore?: string;
  status?: string;
  files?: EventFile[];
  partStatuses?: PartStatus[]; // Event-specific parts (e.g., Fight Night has only Prelims + Main Card)
}


interface QualityProfile {
  id: number;
  name: string;
}

interface DvrChannel {
  channel: {
    id: number;
    name: string;
    logoUrl?: string;
    status: string;
    detectedQuality?: string;
  };
  quality: string;
  qualityScore: number;
  isPreferred: boolean;
}

// Fuzzy matching helper - lenient matching that allows partial word matches
function fuzzyMatch(text: string, search: string): boolean {
  if (!search.trim()) return true;

  const textLower = text.toLowerCase();
  const searchLower = search.toLowerCase().trim();

  // Direct substring match (most lenient)
  if (textLower.includes(searchLower)) return true;

  // Split search into words and check if all words appear somewhere
  const searchWords = searchLower.split(/\s+/).filter(w => w.length > 0);
  if (searchWords.length === 0) return true;

  // Check if each search word is contained in the text (fuzzy word match)
  const allWordsMatch = searchWords.every(word => {
    // Allow partial word matching - if the word is at least 2 chars
    if (word.length >= 2) {
      return textLower.includes(word);
    }
    return true; // Skip single char words
  });

  if (allWordsMatch) return true;

  // Character-based fuzzy matching - check if search chars appear in order (with gaps allowed)
  let searchIndex = 0;
  for (let i = 0; i < textLower.length && searchIndex < searchLower.length; i++) {
    if (textLower[i] === searchLower[searchIndex]) {
      searchIndex++;
    }
  }
  // If we matched at least 70% of the search characters in sequence, it's a match
  return searchIndex >= searchLower.length * 0.7;
}

export default function LeagueDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const { timezone } = useTimezone();
  const [manualSearchModal, setManualSearchModal] = useState<{ isOpen: boolean; eventId: number; eventTitle: string; part?: string; existingFiles?: EventFile[] }>({
    isOpen: false,
    eventId: 0,
    eventTitle: '',
  });
  const [fileDetailModal, setFileDetailModal] = useState<{ isOpen: boolean; eventId: number; eventTitle: string; files: EventFile[]; isFightingSport: boolean }>({
    isOpen: false,
    eventId: 0,
    eventTitle: '',
    files: [],
    isFightingSport: false,
  });
  const [leagueFilesModal, setLeagueFilesModal] = useState<{ isOpen: boolean; season?: string }>({
    isOpen: false,
  });
  const [seasonSearchModal, setSeasonSearchModal] = useState<{ isOpen: boolean; season: string }>({
    isOpen: false,
    season: '',
  });
  const [isEditTeamsModalOpen, setIsEditTeamsModalOpen] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  // CRITICAL: Store stable modal data in refs to prevent modal unmounting during query refetch
  // When queryClient.invalidateQueries runs, the league data might briefly become undefined,
  // which would unmount the modal BEFORE the Transition can clean up, leaving inert attributes
  const editModalDataRef = useRef<{ league: ModalLeagueData; leagueId: number } | null>(null);
  const deleteModalDataRef = useRef<{ name: string; eventCount: number } | null>(null);

  // Track which seasons are expanded (default: current year)
  const currentYear = new Date().getFullYear().toString();
  const [expandedSeasons, setExpandedSeasons] = useState<Set<string>>(new Set([currentYear]));
  const [dvrChannelSearch, setDvrChannelSearch] = useState('');

  // Fetch config to check if multi-part episodes are enabled
  const { data: config } = useQuery({
    queryKey: ['config'],
    queryFn: async () => {
      const response = await apiClient.get<{ enableMultiPartEpisodes: boolean }>('/config');
      return response.data;
    },
  });

  // Fetch league details
  const { data: league, isLoading, error } = useQuery({
    queryKey: ['league', id],
    queryFn: async () => {
      const response = await apiClient.get<LeagueDetail>(`/leagues/${id}`);
      return response.data;
    },
  });

  // Fetch events for this league
  const { data: events = [], isLoading: eventsLoading } = useQuery({
    queryKey: ['league-events', id],
    queryFn: async () => {
      const response = await apiClient.get<EventDetail[]>(`/leagues/${id}/events`);
      return response.data;
    },
    enabled: !!id,
    refetchInterval: (query) => {
      // Auto-refresh every 3 seconds if no events yet (sync in progress)
      // Stop polling once events appear
      const data = query.state.data;
      return (!data || data.length === 0) ? 3000 : false;
    },
  });

  // Fetch quality profiles
  const { data: qualityProfiles = [] } = useQuery({
    queryKey: ['quality-profiles'],
    queryFn: async () => {
      const response = await apiClient.get<QualityProfile[]>('/qualityprofile');
      return response.data;
    },
  });

  // Check if IPTV sources exist (to conditionally show DVR section)
  const { data: iptvSourcesExist = false } = useQuery({
    queryKey: ['iptv-sources-exist'],
    queryFn: async () => {
      const response = await apiClient.get<{ id: number }[]>('/iptv/sources');
      return Array.isArray(response.data) && response.data.length > 0;
    },
    staleTime: 60000, // Cache for 1 minute
  });

  // Fetch DVR channels for this league (only if IPTV sources exist)
  const { data: dvrChannels = [], isLoading: dvrChannelsLoading } = useQuery({
    queryKey: ['dvr-channels', id],
    queryFn: async () => {
      const response = await apiClient.get<DvrChannel[]>(`/iptv/leagues/${id}/channels-by-quality`);
      return response.data;
    },
    enabled: !!id && iptvSourcesExist,
  });

  // Memoized filtered DVR channels for instant search results
  const filteredDvrChannels = useMemo(() => {
    return dvrChannels.filter((c) => fuzzyMatch(c.channel.name, dvrChannelSearch));
  }, [dvrChannels, dvrChannelSearch]);

  // Fetch search queue and download queue status for real-time progress display
  const { data: searchQueue } = useSearchQueueStatus();
  const { data: downloadQueue } = useDownloadQueue();

  // Track locally pending searches for immediate UI feedback on rapid clicks
  const [pendingSearches, setPendingSearches] = useState<{ eventId: number; part?: string; queuedAt: number }[]>([]);

  // Track previous download queue to detect completed imports
  const prevDownloadQueueRef = useRef<typeof downloadQueue>(undefined);

  // Helper to check if an event/part has a search in progress
  const getSearchStatus = useMemo(() => {
    return (eventId: number, part?: string): 'idle' | 'queued' | 'searching' => {
      // Check local pending first (immediate feedback)
      if (pendingSearches.some((p) => p.eventId === eventId && p.part === part)) {
        return 'queued';
      }
      // Check server-side queue
      if (searchQueue) {
        const matchFn = (s: { eventId: number; part: string | null }) => {
          if (s.eventId !== eventId) return false;
          if (part) return s.part === part;
          return !s.part;
        };
        if (searchQueue.activeSearches?.some(matchFn)) {
          return 'searching';
        }
        if (searchQueue.pendingSearches?.some(matchFn)) {
          return 'queued';
        }
      }
      return 'idle';
    };
  }, [pendingSearches, searchQueue]);

  // Clean up stale pending searches (older than 10 seconds or confirmed by server)
  useEffect(() => {
    const interval = setInterval(() => {
      const now = Date.now();
      setPendingSearches((prev) =>
        prev.filter((p) => {
          // Remove if older than 10 seconds
          if (now - p.queuedAt > 10000) return false;
          // Remove if server has confirmed (in pending or active)
          if (searchQueue) {
            const matchFn = (s: { eventId: number; part: string | null }) => {
              if (s.eventId !== p.eventId) return false;
              if (p.part) return s.part === p.part;
              return !s.part;
            };
            const inPending = searchQueue.pendingSearches?.some(matchFn);
            const inActive = searchQueue.activeSearches?.some(matchFn);
            if (inPending || inActive) return false;
          }
          return true;
        })
      );
    }, 1000);
    return () => clearInterval(interval);
  }, [searchQueue]);

  // Detect when imports complete and refresh event data to show quality/CF score
  useEffect(() => {
    if (!downloadQueue || !prevDownloadQueueRef.current) {
      prevDownloadQueueRef.current = downloadQueue;
      return;
    }

    const prevQueue = prevDownloadQueueRef.current;
    const currentQueue = downloadQueue;

    // Find items that were in "Imported" status (7) in prev queue but are now gone
    // OR items that just transitioned to status 7
    const completedImports = prevQueue.filter((prevItem) => {
      // Item was importing/imported and is now gone from queue
      if (prevItem.status === 6 || prevItem.status === 7) {
        const stillInQueue = currentQueue.some((curr) => curr.id === prevItem.id);
        if (!stillInQueue) return true;
      }
      return false;
    });

    // Also check for items that just became imported
    const newlyImported = currentQueue.filter((currItem) => {
      if (currItem.status === 7) {
        const prevItem = prevQueue.find((p) => p.id === currItem.id);
        // Was not imported before, or didn't exist
        if (!prevItem || prevItem.status !== 7) return true;
      }
      return false;
    });

    const allCompleted = [...completedImports, ...newlyImported];

    // If any imports completed for events in this league, refresh the event data
    if (allCompleted.length > 0 && id) {
      // Delay slightly to ensure backend has updated
      setTimeout(() => {
        queryClient.refetchQueries({ queryKey: ['league-events', id] });
        queryClient.refetchQueries({ queryKey: ['league', id] });
      }, 500);
    }

    prevDownloadQueueRef.current = downloadQueue;
  }, [downloadQueue, id, queryClient]);

  // Toggle event monitoring
  const toggleMonitorMutation = useMutation({
    mutationFn: async ({ eventId, monitored, monitoredParts }: { eventId: number; monitored: boolean; monitoredParts?: string | null }) => {
      // When monitoring is toggled, also update parts:
      // - If monitored ON: Use league default parts
      // - If monitored OFF: Clear all parts (null)
      const response = await apiClient.put(`/events/${eventId}`, {
        monitored,
        monitoredParts: monitored ? monitoredParts : null
      });
      return response.data;
    },
    onSuccess: async () => {
      // Use refetchQueries for immediate UI update
      await queryClient.refetchQueries({ queryKey: ['league-events', id] });
      await queryClient.refetchQueries({ queryKey: ['league', id] });
      await queryClient.refetchQueries({ queryKey: ['leagues'] }); // Update league stats
      toast.success('Event updated');
    },
    onError: () => {
      toast.error('Failed to update event');
    },
  });

  // Update event quality profile
  const updateQualityMutation = useMutation({
    mutationFn: async ({ eventId, qualityProfileId }: { eventId: number; qualityProfileId: number | null }) => {
      const response = await apiClient.put(`/events/${eventId}`, { qualityProfileId });
      return response.data;
    },
    onSuccess: async () => {
      await queryClient.refetchQueries({ queryKey: ['league-events', id] });
      toast.success('Quality profile updated');
    },
    onError: () => {
      toast.error('Failed to update quality profile');
    },
  });

  // Set preferred DVR channel for this league
  const setPreferredChannelMutation = useMutation({
    mutationFn: async (channelId: number | null) => {
      const response = await apiClient.post(`/iptv/leagues/${id}/preferred-channel`, { channelId });
      return response.data;
    },
    onSuccess: async (data) => {
      await queryClient.refetchQueries({ queryKey: ['dvr-channels', id] });
      toast.success(data.message || 'Preferred channel updated');
    },
    onError: () => {
      toast.error('Failed to update preferred channel');
    },
  });

  // Toggle league monitoring (monitors/unmonitors all events based on league settings)
  const toggleLeagueMonitorMutation = useMutation({
    mutationFn: async (monitored: boolean) => {
      const response = await apiClient.put(`/leagues/${id}`, { monitored });
      return response.data;
    },
    onSuccess: async () => {
      // Refetch all relevant data - backend updates all events when league monitored status changes
      await queryClient.refetchQueries({ queryKey: ['league', id] });
      await queryClient.refetchQueries({ queryKey: ['league-events', id] }); // Events are updated by backend
      await queryClient.refetchQueries({ queryKey: ['leagues'] });
      toast.success('League monitoring updated');
    },
    onError: () => {
      toast.error('Failed to update league monitoring');
    },
  });

  // Helper to check if sport is motorsport
  const isMotorsport = (sport: string) => {
    const motorsports = [
      'Motorsport', 'Racing', 'Formula 1', 'F1', 'NASCAR', 'IndyCar',
      'MotoGP', 'WEC', 'Formula E', 'Rally', 'WRC', 'DTM', 'Super GT',
      'IMSA', 'V8 Supercars', 'Supercars', 'Le Mans'
    ];
    return motorsports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
  };

  // Update league settings (monitor type, quality profile, search options, monitored parts, session types)
  const updateLeagueSettingsMutation = useMutation({
    mutationFn: async (settings: {
      monitorType?: string;
      qualityProfileId?: number | null;
      searchForMissingEvents?: boolean;
      searchForCutoffUnmetEvents?: boolean;
      monitoredParts?: string | null;
      applyMonitoredPartsToEvents?: boolean;
      monitoredSessionTypes?: string | null;
      monitoredTeamIds?: string[];
    }) => {
      const isMotorsportLeague = league?.sport ? isMotorsport(league.sport) : false;

      // Build the payload - only include monitored if monitoredTeamIds was explicitly provided
      // This prevents inline settings changes (like monitorType dropdown) from accidentally
      // resetting the monitored status
      const payload: Record<string, unknown> = { ...settings };
      delete payload.monitoredTeamIds; // Remove from settings payload - handled separately

      // Only recalculate monitored if monitoredTeamIds was explicitly provided (from edit modal)
      if (settings.monitoredTeamIds !== undefined) {
        // For motorsports, league is always monitored
        // For other sports, league is monitored only if teams are selected
        payload.monitored = isMotorsportLeague ? true : (settings.monitoredTeamIds.length > 0);
      }

      // Update league settings
      const response = await apiClient.put(`/leagues/${id}`, payload);

      // Update monitored teams (only for non-motorsport)
      if (!isMotorsportLeague && settings.monitoredTeamIds !== undefined) {
        await apiClient.put(`/leagues/${id}/teams`, {
          monitoredTeamIds: settings.monitoredTeamIds.length > 0 ? settings.monitoredTeamIds : null,
        });
      }

      return response.data;
    },
    onSuccess: async (_data, variables) => {
      // Use refetchQueries to immediately fetch fresh data before closing modal
      // This ensures UI shows updated part statuses without requiring page refresh
      await queryClient.refetchQueries({ queryKey: ['league', id] });
      await queryClient.refetchQueries({ queryKey: ['league-events', id] });
      await queryClient.refetchQueries({ queryKey: ['leagues'] });

      // Close modal if this was triggered from the edit modal (team changes)
      if (variables.monitoredTeamIds !== undefined) {
        closeEditModal();
        toast.success('League settings updated');
      }
    },
    onError: () => {
      toast.error('Failed to update league settings');
      queryClient.invalidateQueries({ queryKey: ['league', id] });
    },
  });

  // Delete league
  const deleteLeagueMutation = useMutation({
    mutationFn: async () => {
      // Pass deleteFiles: false (matches LeaguesPage behavior when checkbox is unchecked)
      const response = await apiClient.delete(`/leagues/${id}`, {
        params: { deleteFiles: false }
      });
      return response.data;
    },
    onSuccess: async () => {
      toast.success('League deleted successfully');
      // Invalidate queries before navigating to ensure /leagues page is updated
      await queryClient.invalidateQueries({ queryKey: ['leagues'] });
      navigate('/leagues');
    },
    onError: (error: any) => {
      const errorMessage = error.response?.data?.error || 'Failed to delete league';
      toast.error(errorMessage);
    },
  });


  // Update event monitored parts (for fighting sports multi-part episodes)
  const updateEventPartsMutation = useMutation({
    mutationFn: async ({ eventId, monitoredParts }: { eventId: number; monitoredParts: string | null }) => {
      const response = await apiClient.put(`/events/${eventId}/parts`, { monitoredParts });
      return response.data;
    },
    onSuccess: async () => {
      // Use refetchQueries for immediate UI update of part status checkboxes
      await queryClient.refetchQueries({ queryKey: ['league-events', id] });
      await queryClient.refetchQueries({ queryKey: ['league', id] });
      toast.success('Event parts updated');
    },
    onError: () => {
      toast.error('Failed to update event parts');
    },
  });

  // Delete a specific file for an event (for part files)
  const deleteEventFileMutation = useMutation({
    mutationFn: async ({ eventId, fileId }: { eventId: number; fileId: number }) => {
      const response = await apiClient.delete(`/events/${eventId}/files/${fileId}`);
      return response.data;
    },
    onSuccess: async (data) => {
      await queryClient.refetchQueries({ queryKey: ['league-events', id] });
      await queryClient.refetchQueries({ queryKey: ['league', id] });
      await queryClient.refetchQueries({ queryKey: ['leagues'] });
      toast.success(data.message || 'File deleted');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.detail || 'Failed to delete file');
    },
  });

  // Toggle season monitoring (bulk update all events in a season)
  const toggleSeasonMutation = useMutation({
    mutationFn: async ({ leagueId, season, monitored }: { leagueId: number; season: string; monitored: boolean }) => {
      const response = await apiClient.put(`/leagues/${leagueId}/seasons/${season}/toggle`, { monitored });
      return response.data;
    },
    onSuccess: async (data) => {
      // Use refetchQueries for immediate UI update
      await queryClient.refetchQueries({ queryKey: ['league-events', id] });
      await queryClient.refetchQueries({ queryKey: ['league', id] });
      toast.success(data.message || 'Season monitoring updated');
    },
    onError: () => {
      toast.error('Failed to toggle season monitoring');
    },
  });


  const handleEditLeagueSettings = (
    league: any,
    monitoredTeamIds: string[],
    monitorType: string,
    qualityProfileId: number | null,
    searchForMissingEvents: boolean,
    searchForCutoffUnmetEvents: boolean,
    monitoredParts: string | null,
    applyMonitoredPartsToEvents: boolean,
    monitoredSessionTypes: string | null
  ) => {
    updateLeagueSettingsMutation.mutate({
      monitoredTeamIds,
      monitorType,
      qualityProfileId,
      searchForMissingEvents,
      searchForCutoffUnmetEvents,
      monitoredParts,
      applyMonitoredPartsToEvents,
      monitoredSessionTypes,
    });
  };

  // Helper to open edit modal with stable data stored in ref
  // This prevents modal unmounting when query data changes during refetch
  const openEditModal = () => {
    if (league && league.externalId) {
      editModalDataRef.current = {
        league: {
          idLeague: league.externalId,
          strLeague: league.name,
          strSport: league.sport,
          strCountry: league.country,
          strLeagueAlternate: undefined,
          strDescriptionEN: league.description,
          strBadge: league.logoUrl,
          strLogo: league.logoUrl,
          strBanner: league.bannerUrl,
          strPoster: league.posterUrl,
          strWebsite: league.website,
          intFormedYear: league.formedYear?.toString(),
        },
        leagueId: league.id,
      };
      setIsEditTeamsModalOpen(true);
    }
  };

  // Helper to close edit modal and clean up ref
  const closeEditModal = () => {
    setIsEditTeamsModalOpen(false);
    // Clear ref after modal transition completes
    setTimeout(() => {
      editModalDataRef.current = null;
    }, 300);
  };

  // Helper to open delete confirmation with stable data
  const openDeleteConfirm = () => {
    if (league) {
      deleteModalDataRef.current = {
        name: league.name,
        eventCount: league.eventCount,
      };
      setShowDeleteConfirm(true);
    }
  };

  // Helper to close delete confirmation and clean up ref
  const closeDeleteConfirm = () => {
    setShowDeleteConfirm(false);
    setTimeout(() => {
      deleteModalDataRef.current = null;
    }, 300);
  };

  const handleManualSearch = (eventId: number, eventTitle: string, part?: string, existingFiles?: EventFile[]) => {
    setManualSearchModal({
      isOpen: true,
      eventId,
      eventTitle,
      part,
      existingFiles,
    });
  };

  const handleAutomaticSearch = async (eventId: number, eventTitle: string, qualityProfileId?: number, part?: string) => {
    const status = getSearchStatus(eventId, part);

    // If already searching or queued, show feedback but don't re-queue
    if (status === 'searching') {
      toast.info('Search in progress', {
        description: `Already searching for "${eventTitle}"${part ? ` (${part})` : ''}`,
      });
      return;
    }

    if (status === 'queued') {
      toast.info('Search already queued', {
        description: `"${eventTitle}"${part ? ` (${part})` : ''} is waiting in queue`,
      });
      return;
    }

    // Add to local pending immediately for instant UI feedback
    setPendingSearches((prev) => [...prev, { eventId, part, queuedAt: Date.now() }]);

    try {
      // Status shown in sidebar FooterStatusBar - no need for toast here
      const response = await apiClient.post(`/event/${eventId}/automatic-search`, { qualityProfileId, part });

      if (!response.data.success) {
        // Remove from local pending on error
        setPendingSearches((prev) => prev.filter((p) => !(p.eventId === eventId && p.part === part)));
        toast.error('Automatic search failed', {
          description: response.data.message || 'Failed to queue automatic search',
        });
      }
      // Success - local pending will be cleaned up when server confirms
    } catch (error) {
      // Remove from local pending on error
      setPendingSearches((prev) => prev.filter((p) => !(p.eventId === eventId && p.part === part)));
      console.error('Automatic search error:', error);
      toast.error('Automatic search failed', {
        description: 'Failed to start automatic search. Please try again.',
      });
    }
  };

  const handleLeagueAutomaticSearch = async () => {
    if (!id) return;

    try {
      // Status shown in sidebar FooterStatusBar - no need for toast here
      const response = await apiClient.post(`/league/${id}/automatic-search`);

      if (response.data.success) {
        // Refresh league data to update counts
        queryClient.invalidateQueries({ queryKey: ['league', id] });
      } else {
        toast.error('League search failed', {
          description: response.data.message || 'Failed to queue league search',
        });
      }
    } catch (error) {
      console.error('League search error:', error);
      toast.error('League search failed', {
        description: 'Failed to start league search. Please try again.',
      });
    }
  };

  const handleRefreshEvents = async () => {
    if (!id) return;

    try {
      toast.info('Refreshing events...', {
        description: `Fetching events from TheSportsDB for ${league?.name}`,
      });

      // Don't specify seasons - let the backend fetch all available seasons from TheSportsDB
      const response = await apiClient.post(`/leagues/${id}/refresh-events`, {});

      if (response.data.success) {
        toast.success('Events refreshed successfully', {
          description: `${response.data.newEvents} new events added, ${response.data.updatedEvents} updated, ${response.data.skippedEvents} skipped`,
        });

        // Also recalculate episode numbers to fix any ordering issues
        try {
          const recalcResponse = await apiClient.post(`/leagues/${id}/recalculate-episodes`);
          if (recalcResponse.data.success && recalcResponse.data.renumberedCount > 0) {
            toast.info('Episode numbers fixed', {
              description: `${recalcResponse.data.renumberedCount} events renumbered`,
            });
          }
        } catch (recalcError) {
          console.error('Recalculate episodes error:', recalcError);
          // Don't show error toast - episode recalculation is a secondary operation
        }

        // Refresh league data to show new events
        queryClient.invalidateQueries({ queryKey: ['league', id] });
        queryClient.invalidateQueries({ queryKey: ['league', id, 'events'] });
        queryClient.invalidateQueries({ queryKey: ['leagues'] }); // Update league stats
      } else {
        toast.error('Failed to refresh events', {
          description: response.data.message || 'Failed to fetch events from TheSportsDB',
        });
      }
    } catch (error) {
      console.error('Refresh events error:', error);
      toast.error('Failed to refresh events', {
        description: 'An error occurred while fetching events. Please try again.',
      });
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
      </div>
    );
  }

  if (error || !league) {
    return (
      <div className="p-8">
        <div className="max-w-4xl mx-auto">
          <button
            onClick={() => navigate('/leagues')}
            className="flex items-center gap-2 text-gray-400 hover:text-white mb-4 transition-colors"
          >
            <ArrowLeftIcon className="w-5 h-5" />
            Back to Leagues
          </button>
          <div className="text-center py-12">
            <p className="text-red-500 text-xl mb-4">League not found</p>
            <button
              onClick={() => navigate('/leagues')}
              className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
            >
              Go to Leagues
            </button>
          </div>
        </div>
      </div>
    );
  }

  // Group events by season
  const groupedEvents = (events || []).reduce((acc, event) => {
    const season = event.season || 'Unknown';
    if (!acc[season]) {
      acc[season] = [];
    }
    acc[season].push(event);
    return acc;
  }, {} as Record<string, EventDetail[]>);

  // Sort events within each season by episode number (descending - newest first)
  Object.keys(groupedEvents).forEach(season => {
    groupedEvents[season].sort((a, b) => {
      // Sort by episode number descending (highest/newest first)
      const epA = a.episodeNumber ?? 0;
      const epB = b.episodeNumber ?? 0;
      if (epA !== epB) return epB - epA;
      // Fallback to event date descending
      return new Date(b.eventDate).getTime() - new Date(a.eventDate).getTime();
    });
  });

  // Sort seasons newest first
  const sortedSeasons = Object.keys(groupedEvents).sort((a, b) => {
    // Handle 'Unknown' season
    if (a === 'Unknown') return 1;
    if (b === 'Unknown') return -1;
    // Sort numerically for years (handle multi-year seasons like "2024-2025")
    const yearA = parseInt(a.split('-')[0]);
    const yearB = parseInt(b.split('-')[0]);
    return yearB - yearA;
  });

  // Toggle season expansion
  const toggleSeason = (season: string) => {
    setExpandedSeasons(prev => {
      const newSet = new Set(prev);
      if (newSet.has(season)) {
        newSet.delete(season);
      } else {
        newSet.add(season);
      }
      return newSet;
    });
  };

  // Helper to check if a sport is a fighting sport that supports multi-part episodes
  const isFightingSport = (sport: string) => {
    const fightingSports = ['Fighting', 'MMA', 'UFC', 'Boxing', 'Kickboxing', 'Wrestling'];
    return fightingSports.some(s => sport.toLowerCase().includes(s.toLowerCase()));
  };

  // Helper to format file size
  const formatFileSize = (bytes: number): string => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
  };

  // Multi-part episode segments for Fighting sports
  // Fallback parts if event doesn't have partStatuses (backward compatibility)
  const defaultFightCardParts: { name: string; label: string }[] = [
    { name: 'Early Prelims', label: 'Early Prelims' },
    { name: 'Prelims', label: 'Prelims' },
    { name: 'Main Card', label: 'Main Card' },
  ];

  // Get parts for an event - uses event-specific partStatuses from API (which is event-type-aware)
  // e.g., Fight Night events only get Prelims + Main Card, PPV gets all 4 parts
  // DWCS/Contender Series: partStatuses is an empty array (no multi-part)
  const getEventParts = (event: EventDetail): { name: string; label: string }[] => {
    // If partStatuses is explicitly set (even if empty), use it
    // Empty array = event type has no parts (e.g., DWCS)
    if (event.partStatuses !== undefined) {
      return event.partStatuses.map((ps: PartStatus) => ({ name: ps.partName, label: ps.partName }));
    }
    // Undefined = backward compat, use default parts
    return defaultFightCardParts;
  };

  // Check if event uses multi-part episodes
  // Returns false for DWCS/Contender Series (partStatuses is empty array)
  const eventHasMultiPart = (event: EventDetail): boolean => {
    // If partStatuses is defined and empty, event doesn't use multi-part
    if (event.partStatuses !== undefined && event.partStatuses.length === 0) {
      return false;
    }
    return true;
  };

  // Helper to extract resolution from a quality string (e.g., "1080p WEB h264" -> "1080p")
  const extractResolution = (quality: string | undefined | null): string | null => {
    if (!quality) return null;
    const match = quality.match(/\b(2160p|1080p|720p|480p|360p)\b/i);
    return match ? match[1].toLowerCase() : null;
  };

  // Helper to check for part file mismatches (quality/codec/source consistency)
  const getPartMismatchWarnings = (files: EventFile[] | undefined): string[] => {
    if (!files || files.length < 2) return [];

    const existingFiles = files.filter(f => f.exists && f.partName);
    if (existingFiles.length < 2) return [];

    const warnings: string[] = [];
    const firstFile = existingFiles[0];
    const firstResolution = extractResolution(firstFile.quality);

    // Check each subsequent file against the first one
    for (let i = 1; i < existingFiles.length; i++) {
      const file = existingFiles[i];
      const fileResolution = extractResolution(file.quality);

      // Check resolution mismatch (extracted from quality string)
      if (firstResolution && fileResolution && firstResolution !== fileResolution) {
        warnings.push(`Resolution mismatch: ${firstFile.partName} (${firstResolution}) vs ${file.partName} (${fileResolution})`);
      }

      // Check codec mismatch
      if (firstFile.codec && file.codec && firstFile.codec !== file.codec) {
        warnings.push(`Codec mismatch: ${firstFile.partName} (${firstFile.codec}) vs ${file.partName} (${file.codec})`);
      }

      // Check source mismatch
      if (firstFile.source && file.source && firstFile.source !== file.source) {
        warnings.push(`Source mismatch: ${firstFile.partName} (${firstFile.source}) vs ${file.partName} (${file.source})`);
      }
    }

    return warnings;
  };

  return (
    <div className="p-4 md:p-8">
      <div className="max-w-6xl mx-auto">
        {/* Back Button */}
        <button
          onClick={() => navigate('/leagues')}
          className="flex items-center gap-2 text-gray-400 hover:text-white mb-4 md:mb-6 transition-colors text-sm md:text-base"
        >
          <ArrowLeftIcon className="w-4 h-4 md:w-5 md:h-5" />
          Back to Leagues
        </button>

        {/* League Header */}
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden mb-4 md:mb-8">
          {/* Banner/Logo */}
          {(league.bannerUrl || league.logoUrl || league.posterUrl) && (
            <div className="relative h-40 md:h-64 bg-gray-800">
              <img
                src={league.bannerUrl || league.logoUrl || league.posterUrl}
                alt={league.name}
                className="w-full h-full object-cover"
              />
              <div className="absolute inset-0 bg-gradient-to-t from-black via-black/50 to-transparent"></div>
            </div>
          )}

          <div className="p-4 md:p-8">
            <div className="flex flex-col md:flex-row md:items-start justify-between gap-4">
              <div>
                <h1 className="text-2xl md:text-4xl font-bold text-white mb-2">{league.name}</h1>
                <div className="flex flex-wrap items-center gap-2 md:gap-4 text-gray-400">
                  <span className="px-2 md:px-3 py-1 bg-red-600/20 text-red-400 text-xs md:text-sm rounded font-medium">
                    {league.sport}
                  </span>
                  {league.country && (
                    <span className="text-xs md:text-sm">{league.country}</span>
                  )}
                  {league.formedYear && (
                    <span className="text-xs md:text-sm">Est. {league.formedYear}</span>
                  )}
                </div>
              </div>

              <div className="flex flex-wrap md:flex-col gap-2 md:gap-3">
                <button
                  onClick={() => toggleLeagueMonitorMutation.mutate(!league.monitored)}
                  disabled={toggleLeagueMonitorMutation.isPending}
                  className={`px-3 md:px-4 py-1.5 md:py-2 text-white text-xs md:text-sm font-semibold rounded-lg transition-colors ${
                    league.monitored
                      ? 'bg-green-600 hover:bg-green-700'
                      : 'bg-gray-600 hover:bg-gray-700'
                  } ${toggleLeagueMonitorMutation.isPending ? 'opacity-50 cursor-not-allowed' : ''}`}
                  title="Toggle league monitoring - When monitored, events will be tracked for downloads"
                >
                  {league.monitored ? 'Monitored' : 'Not Monitored'}
                </button>
                <button
                  onClick={openEditModal}
                  className="px-3 md:px-4 py-1.5 md:py-2 bg-blue-600 hover:bg-blue-700 text-white text-xs md:text-sm font-semibold rounded-lg transition-colors flex items-center justify-center gap-1.5 md:gap-2"
                  title="Edit monitored teams and monitoring settings"
                >
                  <UsersIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                  Edit
                </button>
                {league.fileCount > 0 && (
                  <button
                    onClick={() => setLeagueFilesModal({ isOpen: true })}
                    className="px-3 md:px-4 py-1.5 md:py-2 bg-gray-600 hover:bg-gray-700 text-white text-xs md:text-sm font-semibold rounded-lg transition-colors flex items-center justify-center gap-1.5 md:gap-2"
                    title="View all downloaded files for this league"
                  >
                    <FolderOpenIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                    <span className="hidden sm:inline">All Files</span> ({league.fileCount})
                  </button>
                )}
                <button
                  onClick={openDeleteConfirm}
                  disabled={deleteLeagueMutation.isPending}
                  className="px-3 md:px-4 py-1.5 md:py-2 bg-red-600/80 hover:bg-red-700 text-white text-xs md:text-sm font-semibold rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  title="Remove league from library"
                >
                  {deleteLeagueMutation.isPending ? 'Deleting...' : 'Delete'}
                </button>
              </div>
            </div>

            {league.description && (
              <p className="text-gray-400 mt-4 leading-relaxed">
                {league.description}
              </p>
            )}

            {league.website && (
              <a
                href={league.website.startsWith('http://') || league.website.startsWith('https://')
                  ? league.website
                  : `https://${league.website}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-2 mt-4 text-red-400 hover:text-red-300 transition-colors"
              >
                Visit Official Website →
              </a>
            )}

            {/* League-Level Search Actions (Sonarr-style show/season search) */}
            <div className="mt-4 md:mt-6 pt-4 md:pt-6 border-t border-red-900/30">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div>
                  <h3 className="text-xs md:text-sm font-semibold text-white mb-0.5 md:mb-1">Search All Monitored Events</h3>
                  <p className="text-xs text-gray-400">
                    Search for missing files and quality upgrades
                  </p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <button
                    onClick={handleLeagueAutomaticSearch}
                    className="px-3 md:px-4 py-1.5 md:py-2 bg-red-600 hover:bg-red-700 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1.5 md:gap-2"
                    title="Search all monitored events - downloads missing files and upgrades existing files if better quality is available"
                  >
                    <MagnifyingGlassIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                    <span className="hidden sm:inline">Search League</span>
                    <span className="sm:hidden">Search</span>
                  </button>
                  <button
                    onClick={handleRefreshEvents}
                    className="px-3 md:px-4 py-1.5 md:py-2 bg-blue-600 hover:bg-blue-700 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1.5 md:gap-2"
                    title="Refresh events from TheSportsDB API - fetches and adds new events to the league"
                  >
                    <ArrowPathIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                    Refresh
                  </button>
                </div>
              </div>
            </div>

            {/* Monitoring Settings */}
            <div className="mt-4 md:mt-6 pt-4 md:pt-6 border-t border-red-900/30">
              <h3 className="text-xs md:text-sm font-semibold text-white mb-3 md:mb-4">Monitoring Settings</h3>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 md:gap-4">
                {/* Monitor Type */}
                <div>
                  <label className="block text-xs font-medium text-gray-400 mb-2">
                    Monitor Events
                  </label>
                  <select
                    value={league?.monitorType || 'Future'}
                    onChange={(e) => updateLeagueSettingsMutation.mutate({ monitorType: e.target.value })}
                    disabled={updateLeagueSettingsMutation.isPending}
                    className="w-full px-3 py-2 bg-black border border-red-900/30 rounded text-white text-sm focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600 disabled:opacity-50"
                  >
                    <option value="All">All Events</option>
                    <option value="Future">Future Events</option>
                    <option value="CurrentSeason">Current Season</option>
                    <option value="LatestSeason">Latest Season</option>
                    <option value="NextSeason">Next Season</option>
                    <option value="Recent">Recent (30 days)</option>
                    <option value="None">None</option>
                  </select>
                </div>

                {/* Quality Profile */}
                <div>
                  <label className="block text-xs font-medium text-gray-400 mb-2">
                    Quality Profile
                  </label>
                  <select
                    value={league?.qualityProfileId || ''}
                    onChange={(e) => updateLeagueSettingsMutation.mutate({
                      qualityProfileId: e.target.value ? parseInt(e.target.value) : null
                    })}
                    disabled={updateLeagueSettingsMutation.isPending}
                    className="w-full px-3 py-2 bg-black border border-red-900/30 rounded text-white text-sm focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600 disabled:opacity-50"
                  >
                    <option value="">No Quality Profile</option>
                    {qualityProfiles.map(profile => (
                      <option key={profile.id} value={profile.id}>
                        {profile.name}
                      </option>
                    ))}
                  </select>
                </div>

                {/* Search for Missing Events */}
                <div>
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={league?.searchForMissingEvents || false}
                      onChange={(e) => updateLeagueSettingsMutation.mutate({
                        searchForMissingEvents: e.target.checked
                      })}
                      disabled={updateLeagueSettingsMutation.isPending}
                      className="w-4 h-4 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2 disabled:opacity-50"
                    />
                    <div>
                      <div className="text-xs font-medium text-white">Search on add/update</div>
                      <div className="text-xs text-gray-400">Search when league settings change</div>
                    </div>
                  </label>
                </div>

                {/* Search for Cutoff Unmet Events */}
                <div>
                  <label className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={league?.searchForCutoffUnmetEvents || false}
                      onChange={(e) => updateLeagueSettingsMutation.mutate({
                        searchForCutoffUnmetEvents: e.target.checked
                      })}
                      disabled={updateLeagueSettingsMutation.isPending}
                      className="w-4 h-4 bg-black border-2 border-gray-600 rounded text-red-600 focus:ring-red-600 focus:ring-offset-0 focus:ring-2 disabled:opacity-50"
                    />
                    <div>
                      <div className="text-xs font-medium text-white">Search for upgrades on add/update</div>
                      <div className="text-xs text-gray-400">Search for quality upgrades when settings change</div>
                    </div>
                  </label>
                </div>
              </div>
            </div>

            {/* DVR Channel Preference - Only show if IPTV sources are configured */}
            {iptvSourcesExist && (
              <div className="mt-4 md:mt-6 pt-4 md:pt-6 border-t border-red-900/30">
                <div className="flex items-center justify-between mb-3 md:mb-4">
                  <div className="flex items-center gap-2">
                    <VideoCameraIcon className="w-4 h-4 md:w-5 md:h-5 text-red-400" />
                    <h3 className="text-xs md:text-sm font-semibold text-white">DVR Channel Preference</h3>
                  </div>
                  {dvrChannels.length > 0 && (
                    <span className="text-xs text-gray-500">
                      {dvrChannelSearch && filteredDvrChannels.length !== dvrChannels.length
                        ? `${filteredDvrChannels.length} of ${dvrChannels.length}`
                        : dvrChannels.length} channel{(dvrChannelSearch ? filteredDvrChannels.length : dvrChannels.length) !== 1 ? 's' : ''}
                    </span>
                  )}
                </div>

                {dvrChannelsLoading ? (
                  <div className="text-sm text-gray-400">Loading channels...</div>
                ) : dvrChannels.length === 0 ? (
                  <div className="bg-gray-800/50 border border-gray-700 rounded-lg p-3">
                    <p className="text-xs text-gray-400">No channels mapped to this league. Map channels in Settings → IPTV Channels.</p>
                  </div>
                ) : (
                  <div className="space-y-2">
                    {/* Search input for channels */}
                    <div className="relative">
                      <MagnifyingGlassIcon className="absolute left-2 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                      <input
                        type="text"
                        placeholder="Search channels..."
                        value={dvrChannelSearch}
                        onChange={(e) => setDvrChannelSearch(e.target.value)}
                        className="w-full pl-8 pr-3 py-1.5 bg-black border border-gray-700 rounded text-white text-xs placeholder-gray-500 focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
                      />
                    </div>

                    {/* Auto-select option */}
                    <button
                      onClick={() => setPreferredChannelMutation.mutate(null)}
                      disabled={setPreferredChannelMutation.isPending}
                      className={`w-full flex items-center gap-2 p-2 rounded-lg border transition-colors ${
                        !dvrChannels.some(c => c.isPreferred)
                          ? 'bg-red-950/30 border-red-900/50 ring-1 ring-red-600'
                          : 'bg-gray-800/50 border-gray-700 hover:border-gray-600'
                      }`}
                    >
                      <SignalIcon className="w-4 h-4 text-gray-400" />
                      <div className="flex-1 text-left">
                        <div className="text-xs font-medium text-white">Auto-select best quality</div>
                      </div>
                      {!dvrChannels.some(c => c.isPreferred) && (
                        <CheckCircleIcon className="w-4 h-4 text-green-500" />
                      )}
                    </button>

                    {/* Scrollable channel list */}
                    <div className="max-h-48 overflow-y-auto space-y-1.5 pr-1">
                      {filteredDvrChannels.map((dvrChannel) => (
                        <button
                          key={dvrChannel.channel.id}
                          onClick={() => setPreferredChannelMutation.mutate(dvrChannel.channel.id)}
                          disabled={setPreferredChannelMutation.isPending}
                          className={`w-full flex items-center gap-2 p-2 rounded-lg border transition-colors ${
                            dvrChannel.isPreferred
                              ? 'bg-red-950/30 border-red-900/50 ring-1 ring-red-600'
                              : 'bg-gray-800/50 border-gray-700 hover:border-gray-600'
                          }`}
                        >
                          {dvrChannel.channel.logoUrl ? (
                            <img
                              src={dvrChannel.channel.logoUrl}
                              alt={dvrChannel.channel.name}
                              className="w-6 h-6 rounded object-contain bg-gray-800 flex-shrink-0"
                              onError={(e) => {
                                (e.target as HTMLImageElement).style.display = 'none';
                              }}
                            />
                          ) : (
                            <div className="w-6 h-6 rounded bg-gray-800 flex items-center justify-center flex-shrink-0">
                              <SignalIcon className="w-3 h-3 text-gray-600" />
                            </div>
                          )}
                          <div className="flex-1 text-left min-w-0">
                            <div className="text-xs font-medium text-white truncate">{dvrChannel.channel.name}</div>
                          </div>
                          <div className="flex items-center gap-1 flex-shrink-0">
                            <span className={`px-1 py-0.5 text-[10px] rounded ${
                              dvrChannel.quality === '4K' ? 'bg-purple-900/30 text-purple-400' :
                              dvrChannel.quality === 'FHD' ? 'bg-blue-900/30 text-blue-400' :
                              dvrChannel.quality === 'HD' ? 'bg-green-900/30 text-green-400' :
                              'bg-yellow-900/30 text-yellow-400'
                            }`}>
                              {dvrChannel.quality}
                            </span>
                            {dvrChannel.isPreferred && (
                              <CheckCircleIcon className="w-4 h-4 text-green-500" />
                            )}
                          </div>
                        </button>
                      ))}
                      {dvrChannelSearch && filteredDvrChannels.length === 0 && (
                        <div className="text-xs text-gray-500 text-center py-2">No matching channels</div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Stats */}
        <div className="grid grid-cols-3 gap-2 md:gap-6 mb-4 md:mb-8">
          <div className="bg-gray-900 border border-red-900/30 rounded-lg p-3 md:p-6">
            <div className="text-gray-400 text-xs md:text-sm mb-1">Total Events</div>
            <div className="text-xl md:text-3xl font-bold text-white">{league.eventCount}</div>
          </div>
          <div className="bg-gray-900 border border-red-900/30 rounded-lg p-3 md:p-6">
            <div className="text-gray-400 text-xs md:text-sm mb-1">Monitored</div>
            <div className="text-xl md:text-3xl font-bold text-green-400">{league.monitoredEventCount}</div>
          </div>
          <div className="bg-gray-900 border border-red-900/30 rounded-lg p-3 md:p-6">
            <div className="text-gray-400 text-xs md:text-sm mb-1">Downloaded</div>
            <div className="text-xl md:text-3xl font-bold text-blue-400">{league.fileCount}</div>
          </div>
        </div>

        {/* Events Section */}
        <div className="bg-gray-900 border border-red-900/30 rounded-lg overflow-hidden">
          <div className="p-4 md:p-6 border-b border-red-900/30">
            <h2 className="text-xl md:text-2xl font-bold text-white">Events</h2>
            <p className="text-gray-400 text-xs md:text-sm mt-1">
              {Array.isArray(events) ? events.length : 0} event{Array.isArray(events) && events.length !== 1 ? 's' : ''} in this league
            </p>
          </div>

          {eventsLoading ? (
            <div className="p-12 text-center">
              <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
              <p className="text-gray-400">Loading events...</p>
            </div>
          ) : !Array.isArray(events) || events.length === 0 ? (
            <div className="p-12 text-center">
              <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-red-600 mx-auto mb-4"></div>
              <p className="text-gray-400 mb-2">Syncing events from TheSportsDB...</p>
              <p className="text-gray-500 text-sm">This may take a moment for leagues with many seasons</p>
            </div>
          ) : (
            <div>
              {/* Season Groups */}
              {sortedSeasons.map(season => {
                const seasonEvents = groupedEvents[season];
                const isExpanded = expandedSeasons.has(season);
                const monitoredCount = seasonEvents.filter(e => e.monitored).length;
                const hasFileCount = seasonEvents.filter(e => e.hasFile).length;

                return (
                  <div key={season} className="border-b border-red-900/30 last:border-b-0">
                    {/* Season Header Row */}
                    <div className="p-4 md:p-6 hover:bg-gray-800/30 transition-colors">
                      <div className="flex items-center gap-2 md:gap-4">
                        {/* Season Monitor Toggle */}
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            toggleSeasonMutation.mutate({
                              leagueId: Number(id),
                              season,
                              monitored: monitoredCount === 0
                            });
                          }}
                          className="focus:outline-none focus:ring-2 focus:ring-red-500 rounded flex-shrink-0"
                          disabled={toggleSeasonMutation.isPending}
                          title={monitoredCount > 0 ? "Unmonitor all events in this season" : "Monitor all events in this season"}
                        >
                          {monitoredCount > 0 ? (
                            <CheckCircleIcon className="w-5 h-5 md:w-6 md:h-6 text-green-500" />
                          ) : (
                            <XCircleIcon className="w-5 h-5 md:w-6 md:h-6 text-gray-600" />
                          )}
                        </button>

                        {/* Season Title */}
                        <button
                          onClick={() => toggleSeason(season)}
                          className="flex items-center gap-2 flex-1 text-left"
                        >
                          {isExpanded ? (
                            <ChevronDownIcon className="w-4 h-4 md:w-5 md:h-5 text-gray-400" />
                          ) : (
                            <ChevronRightIcon className="w-4 h-4 md:w-5 md:h-5 text-gray-400" />
                          )}
                          <div>
                            <h3 className="text-base md:text-xl font-bold text-white">
                              {season === 'Unknown' ? 'No Season Info' : `Season ${season}`}
                            </h3>
                            <p className="text-xs md:text-sm text-gray-400 mt-0.5 md:mt-1">
                              {seasonEvents.length} event{seasonEvents.length !== 1 ? 's' : ''}
                              {monitoredCount > 0 && ` • ${monitoredCount} monitored`}
                              {hasFileCount > 0 && ` • ${hasFileCount} downloaded`}
                            </p>
                          </div>
                        </button>
                      </div>

                      {/* Season Actions Row */}
                      <div className="flex flex-wrap items-center gap-2 md:gap-3 mt-3 md:mt-4 ml-7 md:ml-10">
                        {/* Season Quality Profile */}
                        <select
                          value={league?.qualityProfileId || ''}
                          onChange={(e) => updateLeagueSettingsMutation.mutate({
                            qualityProfileId: e.target.value ? parseInt(e.target.value) : null
                          })}
                          disabled={updateLeagueSettingsMutation.isPending}
                          className="px-2 md:px-3 py-1 md:py-1.5 bg-gray-800 border border-gray-700 text-gray-200 text-xs md:text-sm rounded focus:outline-none focus:ring-2 focus:ring-red-500"
                          onClick={(e) => e.stopPropagation()}
                        >
                          <option value="">No Quality Profile</option>
                          {qualityProfiles.map(profile => (
                            <option key={profile.id} value={profile.id}>
                              {profile.name}
                            </option>
                          ))}
                        </select>

                        {/* Season Manual Search */}
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            setSeasonSearchModal({ isOpen: true, season });
                          }}
                          className="px-2 md:px-4 py-1 md:py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1 md:gap-2"
                          title="Manual Search - Browse and select releases for all events in this season"
                        >
                          <UserIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                          <span className="hidden sm:inline">Manual Search</span>
                          <span className="sm:hidden">Manual</span>
                        </button>

                        {/* Season Auto Search */}
                        <button
                          onClick={async (e) => {
                            e.stopPropagation();
                            if (!league?.id) return;

                            try {
                              toast.info('Starting season search...', {
                                description: `Searching all monitored events in ${season}`
                              });

                              const response = await apiClient.post(`/leagues/${league.id}/seasons/${season}/automatic-search`);

                              if (response.data.success) {
                                toast.success('Season search queued', {
                                  description: response.data.message || `Queued searches for all monitored events in ${season}`
                                });
                                // Refetch for immediate UI update
                                await queryClient.refetchQueries({ queryKey: ['league-events', id] });
                                await queryClient.refetchQueries({ queryKey: ['league', id] });
                              } else {
                                toast.error('Season search failed', {
                                  description: response.data.message || 'Failed to queue season search'
                                });
                              }
                            } catch (error) {
                              console.error('Season search error:', error);
                              toast.error('Season search failed', {
                                description: 'Failed to start season search. Please try again.'
                              });
                            }
                          }}
                          className="px-2 md:px-4 py-1 md:py-1.5 bg-red-600 hover:bg-red-700 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1 md:gap-2"
                          title="Automatic Search - Search for all monitored events in this season"
                        >
                          <MagnifyingGlassIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                          <span className="hidden sm:inline">Auto Search</span>
                          <span className="sm:hidden">Auto</span>
                        </button>

                        {/* Season Files Button */}
                        {hasFileCount > 0 && (
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              setLeagueFilesModal({ isOpen: true, season });
                            }}
                            className="px-2 md:px-4 py-1 md:py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1 md:gap-2"
                            title={`View all downloaded files for ${season}`}
                          >
                            <FolderOpenIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                            <span className="hidden sm:inline">Files</span> ({hasFileCount})
                          </button>
                        )}
                      </div>
                    </div>

                    {/* Season Events */}
                    {isExpanded && (
                      <div className="divide-y divide-red-900/30">
                        {seasonEvents.map(event => {
                const hasFile = event.hasFile;
                const eventDate = new Date(event.eventDate);
                const isPast = eventDate < new Date();

                return (
                  <div key={event.id} className="hover:bg-gray-800/50 transition-colors">
                    {/* Event Row */}
                    <div className="p-3 md:p-6">
                      {/* Event Header */}
                      <div className="flex items-center gap-2 md:gap-4">
                        {/* Monitor Toggle */}
                        <button
                          onClick={() => toggleMonitorMutation.mutate({
                            eventId: event.id,
                            monitored: !event.monitored,
                            monitoredParts: league?.monitoredParts
                          })}
                          className="focus:outline-none focus:ring-2 focus:ring-red-500 rounded flex-shrink-0"
                          disabled={toggleMonitorMutation.isPending}
                        >
                          {event.monitored ? (
                            <CheckCircleIcon className="w-5 h-5 md:w-6 md:h-6 text-green-500" />
                          ) : (
                            <XCircleIcon className="w-5 h-5 md:w-6 md:h-6 text-gray-600" />
                          )}
                        </button>

                        {/* Event Thumbnail */}
                        {event.images && event.images.length > 0 ? (
                          <img
                            src={event.images[0]}
                            alt={event.title}
                            className="w-10 h-10 md:w-12 md:h-12 rounded object-cover flex-shrink-0 bg-gray-800"
                            onError={(e) => {
                              // Hide broken images
                              (e.target as HTMLImageElement).style.display = 'none';
                            }}
                          />
                        ) : (
                          <div className="w-10 h-10 md:w-12 md:h-12 rounded bg-gray-800 flex items-center justify-center flex-shrink-0">
                            <FilmIcon className="w-5 h-5 md:w-6 md:h-6 text-gray-600" />
                          </div>
                        )}

                        {/* Event Title */}
                        <div className="flex-1 min-w-0">
                          <h3 className="text-sm md:text-lg font-semibold text-white truncate">
                            {event.title}
                          </h3>
                        </div>

                        {/* Event Status Badge - Shows search/download/import progress */}
                        {/* Show for non-fighting sports, OR fighting sports without multi-part (e.g., DWCS) */}
                        {/* Shows even when hasFile=true for upgrade scenarios */}
                        {!(config?.enableMultiPartEpisodes && isFightingSport(event.sport) && eventHasMultiPart(event)) && (
                          <EventStatusBadge
                            eventId={event.id}
                            searchQueue={searchQueue}
                            downloadQueue={downloadQueue}
                          />
                        )}

                        {/* File Status Badge - Click to view/manage files */}
                        {/* Only show if there's no active status (status badge takes priority during search/download) */}
                        {hasFile && (
                          <button
                            onClick={() => setFileDetailModal({
                              isOpen: true,
                              eventId: event.id,
                              eventTitle: event.title,
                              files: event.files || [],
                              isFightingSport: isFightingSport(event.sport),
                            })}
                            className="px-3 py-1 bg-green-600 hover:bg-green-700 text-white text-xs font-semibold rounded transition-colors flex items-center gap-1.5"
                            title="Click to view and manage downloaded files"
                          >
                            <FilmIcon className="w-3.5 h-3.5" />
                            {event.files && event.files.length > 1 ? `${event.files.length} Files` : 'Downloaded'}
                          </button>
                        )}
                      </div>

                      {/* Event Details */}
                      <div className="ml-7 md:ml-10 mt-2 space-y-1">
                        <div className="flex flex-wrap items-center gap-2 md:gap-3 text-xs md:text-sm text-gray-400">
                          <span>{formatDateInTimezone(event.eventDate, timezone, {
                            year: 'numeric',
                            month: 'short',
                            day: 'numeric'
                          })}</span>

                          {/* Season/Episode Number (Plex format) */}
                          {event.seasonNumber && event.episodeNumber && (
                            <span className="px-2 py-0.5 bg-blue-600/20 text-blue-400 rounded font-mono">
                              S{event.seasonNumber}E{String(event.episodeNumber).padStart(2, '0')}
                            </span>
                          )}

                          {/* Status badge - infer from date if not set */}
                          {(() => {
                            const eventDate = new Date(event.eventDate);
                            const now = new Date();
                            const isPast = eventDate < now;
                            const status = event.status?.toUpperCase();
                            // Event is completed if: has file, OR explicit completed status, OR past date with unstarted/no status
                            const isCompleted = event.hasFile || status === 'FT' || status === 'COMPLETED' || status === 'MATCH FINISHED' || (isPast && (!status || status === 'NS' || status === 'NOT STARTED'));
                            const isLive = status === 'LIVE';
                            const isNotStarted = !isCompleted && !isLive;

                            if (isCompleted) {
                              return (
                                <span className="px-2 py-0.5 rounded bg-blue-600/20 text-blue-400">
                                  Completed
                                </span>
                              );
                            } else if (isLive) {
                              return (
                                <span className="px-2 py-0.5 rounded bg-green-600/20 text-green-400">
                                  Live
                                </span>
                              );
                            } else if (isNotStarted) {
                              return (
                                <span className="px-2 py-0.5 rounded bg-gray-600/20 text-gray-400">
                                  Not Started
                                </span>
                              );
                            } else if (event.status) {
                              return (
                                <span className="px-2 py-0.5 rounded bg-gray-600/20 text-gray-400">
                                  {event.status}
                                </span>
                              );
                            }
                            return null;
                          })()}
                        </div>

                        {/* Team Names */}
                        {event.homeTeamName && event.awayTeamName && (
                          <div className="text-xs md:text-sm text-gray-300">
                            {event.homeTeamName} vs {event.awayTeamName}
                            {event.homeScore !== undefined && event.awayScore !== undefined && (
                              <span className="ml-2 text-gray-400">
                                ({event.homeScore} - {event.awayScore})
                              </span>
                            )}
                          </div>
                        )}

                        {event.venue && (
                          <div className="text-xs md:text-sm text-gray-400 hidden sm:block">
                            {event.venue}
                            {event.location && `, ${event.location}`}
                          </div>
                        )}
                      </div>

                      {/* Event Actions */}
                      <div className="flex flex-wrap items-center gap-2 md:gap-3 mt-3 md:mt-4 ml-7 md:ml-10">
                            {/* Quality Profile Dropdown */}
                            <div className="flex-1 max-w-[150px] md:max-w-xs">
                              <select
                                value={event.qualityProfileId || league?.qualityProfileId || ''}
                                onChange={(e) => updateQualityMutation.mutate({
                                  eventId: event.id,
                                  qualityProfileId: e.target.value ? Number(e.target.value) : null
                                })}
                                className="w-full px-2 md:px-3 py-1 md:py-1.5 bg-gray-800 border border-gray-700 text-gray-200 text-xs md:text-sm rounded focus:outline-none focus:ring-2 focus:ring-red-500"
                                disabled={updateQualityMutation.isPending}
                              >
                                <option value="">
                                  {league?.qualityProfileId
                                    ? `League (${Array.isArray(qualityProfiles) ? qualityProfiles.find(p => p.id === league.qualityProfileId)?.name || '?' : '?'})`
                                    : 'No Profile'}
                                </option>
                                {Array.isArray(qualityProfiles) && qualityProfiles.map(profile => (
                                  <option key={profile.id} value={profile.id}>
                                    {profile.name}
                                    {event.qualityProfileId === profile.id && ' (Custom)'}
                                  </option>
                                ))}
                              </select>
                            </div>

                            {/* Search Buttons - Hidden for fighting sports with multi-part episodes (show per-part buttons instead) */}
                            {/* Show for non-fighting sports, OR fighting sports without multi-part (e.g., DWCS/Contender Series) */}
                            {!(config?.enableMultiPartEpisodes && isFightingSport(event.sport) && eventHasMultiPart(event)) && (
                              <>
                                <button
                                  onClick={() => handleManualSearch(event.id, event.title, undefined, event.files)}
                                  className="px-2 md:px-4 py-1 md:py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1 md:gap-2"
                                  title="Manual Search - Browse and select from available releases"
                                >
                                  <UserIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                                  <span className="hidden sm:inline">Manual</span>
                                </button>

                                <button
                                  onClick={() => handleAutomaticSearch(event.id, event.title, event.qualityProfileId || league?.qualityProfileId)}
                                  className="px-2 md:px-4 py-1 md:py-1.5 bg-red-600 hover:bg-red-700 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1 md:gap-2"
                                  title="Search for monitored event"
                                >
                                  <MagnifyingGlassIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                                  <span className="hidden sm:inline">Auto</span>
                                </button>
                              </>
                            )}
                          </div>

                          {/* Fight Card Parts (for fighting sports with multi-part episodes enabled) */}
                          {/* DWCS/Contender Series events don't have parts - eventHasMultiPart returns false for them */}
                          {config?.enableMultiPartEpisodes && isFightingSport(event.sport) && eventHasMultiPart(event) && (
                            <div className="mt-3 md:mt-4 ml-7 md:ml-10 space-y-2 md:space-y-3">
                              {getEventParts(event).map((part) => {
                                // monitoredParts values:
                                // - null/undefined = ALL parts monitored (default)
                                // - '' (empty string) = NO parts monitored
                                // - 'Part1,Part2' = specific parts monitored
                                // Use event's setting if set, otherwise fall back to league's setting
                                const monitoredParts = event.monitoredParts !== null && event.monitoredParts !== undefined
                                  ? event.monitoredParts
                                  : (league?.monitoredParts ?? null);
                                // Only null/undefined means all parts - empty string means NONE
                                const isAllPartsMonitored = monitoredParts === null || monitoredParts === undefined;
                                const partsArray = monitoredParts ? monitoredParts.split(',').map((p: string) => p.trim()).filter(Boolean) : [];

                                // Check if league has any monitored teams (for fighting sports)
                                const hasMonitoredTeams = league?.monitoredTeams?.some(mt => mt.monitored) ?? false;

                                // Parts are monitored if:
                                // 1. The event is individually monitored (user manually monitored it), OR
                                // 2. The league has monitored teams (normal case)
                                // AND the part is in the monitored parts list (or all parts are monitored)
                                const eventOrLeagueMonitored = event.monitored || hasMonitoredTeams;
                                const isPartMonitored = eventOrLeagueMonitored && (isAllPartsMonitored || partsArray.includes(part.name));

                                // Find if this part has a downloaded file
                                // First try partStatuses (pre-computed by backend with proper file info)
                                // Fall back to searching event.files by partName for backwards compatibility
                                const partStatus = event.partStatuses?.find(ps => ps.partName === part.name);
                                const partFile = partStatus?.file ?? event.files?.find(f => f.partName === part.name && f.exists);

                                return (
                                  <div key={part.name} className="flex flex-wrap items-center gap-2 md:gap-3">
                                    {/* Part Monitor Toggle */}
                                    <button
                                      onClick={() => {
                                        let newParts: string[];
                                        const eventParts = getEventParts(event);
                                        if (isPartMonitored) {
                                          // Unmonitoring a part
                                          if (isAllPartsMonitored) {
                                            // Currently all parts are monitored (null) - need to explicitly list the OTHER parts
                                            newParts = eventParts.map(p => p.name).filter(name => name !== part.name);
                                          } else {
                                            // Remove this part from the existing list
                                            newParts = partsArray.filter((p: string) => p !== part.name);
                                          }
                                        } else {
                                          // Monitoring a part - add it to the list
                                          newParts = [...partsArray, part.name];
                                        }
                                        // When all parts are selected, send null (means "all parts")
                                        // When no parts are selected, send '' (empty string means "no parts")
                                        // When some parts selected, send comma-separated list
                                        const allPartNames = eventParts.map(p => p.name);
                                        const allPartsSelected = newParts.length === allPartNames.length &&
                                          allPartNames.every(name => newParts.includes(name));

                                        updateEventPartsMutation.mutate({
                                          eventId: event.id,
                                          monitoredParts: allPartsSelected ? null : (newParts.length > 0 ? newParts.join(',') : '')
                                        });
                                      }}
                                      className="focus:outline-none focus:ring-2 focus:ring-red-500 rounded flex-shrink-0"
                                      disabled={updateEventPartsMutation.isPending}
                                      title={`${isPartMonitored ? 'Unmonitor' : 'Monitor'} ${part.label}`}
                                    >
                                      {isPartMonitored ? (
                                        <CheckCircleIcon className="w-4 h-4 md:w-5 md:h-5 text-green-500" />
                                      ) : (
                                        <XCircleIcon className="w-4 h-4 md:w-5 md:h-5 text-gray-600" />
                                      )}
                                    </button>

                                    {/* Part Name and File/Status Display */}
                                    <div className="flex-1 flex flex-wrap items-center gap-1 md:gap-2 min-w-0">
                                      <span className={`text-xs md:text-sm font-medium ${isPartMonitored ? 'text-white' : 'text-gray-500'}`}>
                                        {part.label}
                                      </span>
                                      {/* Show file info if downloaded, otherwise show status badge for search/download progress */}
                                      {partFile ? (
                                        <span className="text-xs text-gray-400 flex items-center gap-1 md:gap-1.5">
                                          <FilmIcon className="w-3 h-3 md:w-3.5 md:h-3.5 text-green-500" />
                                          {partFile.quality && <span className="text-blue-400 hidden sm:inline">{partFile.quality}</span>}
                                          <span className="hidden sm:inline">({formatFileSize(partFile.size)})</span>
                                          {partFile.customFormatScore !== undefined && partFile.customFormatScore !== 0 && (
                                            <span className={`px-1 md:px-1.5 py-0.5 rounded text-xs font-medium hidden md:inline ${
                                              partFile.customFormatScore > 0
                                                ? 'bg-green-900/40 text-green-400'
                                                : 'bg-red-900/40 text-red-400'
                                            }`}>
                                              CF: {partFile.customFormatScore > 0 ? '+' : ''}{partFile.customFormatScore}
                                            </span>
                                          )}
                                        </span>
                                      ) : (
                                        <EventStatusBadge
                                          eventId={event.id}
                                          part={part.name}
                                          searchQueue={searchQueue}
                                          downloadQueue={downloadQueue}
                                        />
                                      )}
                                    </div>

                                    {/* Delete Part File Button (if file exists) */}
                                    {partFile && (
                                      <button
                                        onClick={() => {
                                          if (confirm(`Delete the downloaded file for ${part.label}? This cannot be undone.`)) {
                                            deleteEventFileMutation.mutate({
                                              eventId: event.id,
                                              fileId: partFile.id
                                            });
                                          }
                                        }}
                                        className="p-1 md:p-1.5 text-gray-400 hover:text-red-400 hover:bg-red-600/10 rounded transition-colors"
                                        disabled={deleteEventFileMutation.isPending}
                                        title={`Delete ${part.label} file`}
                                      >
                                        <TrashIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                                      </button>
                                    )}

                                    {/* Part Manual Search */}
                                    <button
                                      onClick={() => handleManualSearch(event.id, event.title, part.name, event.files)}
                                      className="px-2 md:px-4 py-1 md:py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1 md:gap-2"
                                      title={`Manual Search - Browse and select ${part.label} releases`}
                                    >
                                      <UserIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                                      <span className="hidden sm:inline">Manual</span>
                                    </button>

                                    {/* Part Auto Search */}
                                    <button
                                      onClick={() => handleAutomaticSearch(event.id, event.title, event.qualityProfileId || league?.qualityProfileId, part.name)}
                                      className="px-2 md:px-4 py-1 md:py-1.5 bg-red-600 hover:bg-red-700 text-white text-xs md:text-sm font-medium rounded transition-colors flex items-center gap-1 md:gap-2"
                                      title={`Search for monitored ${part.label}`}
                                    >
                                      <MagnifyingGlassIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                                      <span className="hidden sm:inline">Auto</span>
                                    </button>
                                  </div>
                                );
                              })}

                              {/* Part Mismatch Warning */}
                              {(() => {
                                const warnings = getPartMismatchWarnings(event.files);
                                if (warnings.length === 0) return null;
                                return (
                                  <div className="mt-3 p-3 bg-yellow-900/20 border border-yellow-600/30 rounded-lg">
                                    <div className="flex items-start gap-2">
                                      <ExclamationTriangleIcon className="w-5 h-5 text-yellow-500 flex-shrink-0 mt-0.5" />
                                      <div>
                                        <p className="text-yellow-400 text-sm font-medium mb-1">
                                          Part files may not play back-to-back correctly in Plex
                                        </p>
                                        <ul className="text-yellow-300/80 text-xs space-y-0.5">
                                          {warnings.map((warning, idx) => (
                                            <li key={idx}>• {warning}</li>
                                          ))}
                                        </ul>
                                        <p className="text-yellow-300/60 text-xs mt-2">
                                          For seamless playback, all parts should have the same quality, codec, and source.
                                        </p>
                                      </div>
                                    </div>
                                  </div>
                                );
                              })()}
                            </div>
                          )}
                      </div>
                    </div>
                );
              })}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Manual Search Modal */}
      <ManualSearchModal
        isOpen={manualSearchModal.isOpen}
        onClose={() => setManualSearchModal({ ...manualSearchModal, isOpen: false })}
        eventId={manualSearchModal.eventId}
        eventTitle={manualSearchModal.eventTitle}
        part={manualSearchModal.part}
        existingFiles={manualSearchModal.existingFiles}
      />

      {/* Event File Detail Modal */}
      <EventFileDetailModal
        isOpen={fileDetailModal.isOpen}
        onClose={() => setFileDetailModal({ ...fileDetailModal, isOpen: false })}
        eventId={fileDetailModal.eventId}
        eventTitle={fileDetailModal.eventTitle}
        files={fileDetailModal.files}
        leagueId={id}
        isFightingSport={fileDetailModal.isFightingSport}
      />

      {/* League Files Modal - View all files for league or season */}
      {league && (
        <LeagueFilesModal
          isOpen={leagueFilesModal.isOpen}
          onClose={() => setLeagueFilesModal({ isOpen: false })}
          leagueId={league.id}
          leagueName={league.name}
          season={leagueFilesModal.season}
        />
      )}

      {/* Season Search Modal */}
      {league && (
        <SeasonSearchModal
          isOpen={seasonSearchModal.isOpen}
          onClose={() => setSeasonSearchModal({ isOpen: false, season: '' })}
          leagueId={league.id}
          leagueName={league.name}
          season={seasonSearchModal.season}
          qualityProfileId={league.qualityProfileId}
        />
      )}

      {/* Edit Teams Modal - Always rendered, uses show prop for proper transition cleanup */}
      <AddLeagueModal
        league={editModalDataRef.current?.league || null}
        isOpen={isEditTeamsModalOpen}
        onClose={closeEditModal}
        onAdd={handleEditLeagueSettings}
        isAdding={updateLeagueSettingsMutation.isPending}
        editMode={true}
        leagueId={editModalDataRef.current?.leagueId || null}
      />

      {/* Delete Confirmation Modal - Always rendered, uses show prop for proper transition cleanup */}
      <ConfirmationModal
        isOpen={showDeleteConfirm}
        onClose={closeDeleteConfirm}
        onConfirm={() => {
          deleteLeagueMutation.mutate();
          closeDeleteConfirm();
        }}
        title={deleteModalDataRef.current ? "Delete League" : undefined}
        message={deleteModalDataRef.current ? `Are you sure you want to delete "${deleteModalDataRef.current.name}"? This will remove the league${
          deleteModalDataRef.current.eventCount > 0 ? ` and all ${deleteModalDataRef.current.eventCount} event${deleteModalDataRef.current.eventCount !== 1 ? 's' : ''}` : ''
        } from your library.` : undefined}
        confirmText="Delete League"
        confirmButtonClass="bg-red-600 hover:bg-red-700"
        isLoading={deleteLeagueMutation.isPending}
      />
    </div>
  );
}
