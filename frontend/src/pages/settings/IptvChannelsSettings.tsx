import { useState, useEffect, useMemo, useCallback, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import IptvCoveragePage from '../iptv/IptvCoveragePage';
import {
  PlusIcon,
  CheckCircleIcon,
  XCircleIcon,
  SignalIcon,
  XMarkIcon,
  ArrowPathIcon,
  PlayIcon,
  LinkIcon,
  FunnelIcon,
  BoltIcon,
  StarIcon as StarIconOutline,
  EyeSlashIcon,
  EyeIcon,
  GlobeAltIcon,
  ChevronDownIcon,
  WrenchScrewdriverIcon,
} from '@heroicons/react/24/outline';
import { StarIcon as StarIconSolid } from '@heroicons/react/24/solid';
import { Menu } from '@headlessui/react';
import { useMediaQuery } from '../../hooks/useMediaQuery';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import PageHeader from '../../components/PageHeader';
import PageShell from '../../components/PageShell';
import StreamPlayerModal from '../../components/StreamPlayerModal';

// Types
interface IptvChannel {
  id: number;
  sourceId: number;
  name: string;
  channelNumber?: number;
  streamUrl: string;
  logoUrl?: string;
  group?: string;
  tvgId?: string;
  isSportsChannel: boolean;
  status: 'Unknown' | 'Online' | 'Offline' | 'Error';
  lastChecked?: string;
  isEnabled: boolean;
  country?: string;
  language?: string;
  detectedQuality?: string;
  qualityScore: number;
  detectedNetwork?: string;
  mappedLeagueIds: number[];
  sourceName?: string;
  isFavorite: boolean;
  isHidden: boolean;
}

interface League {
  id: number;
  name: string;
  sport: string;
  logoUrl?: string;
}

interface LeagueMapping {
  id: number;
  channelId: number;
  leagueId: number;
  leagueName: string;
  leagueSport: string;
  isPreferred: boolean;
  priority: number;
  // Phase 1 fields — present on the API since the scored-mapping
  // migration. Older deployments without the new columns will hand
  // back undefined, which the UI tolerates.
  confidence?: number;
  isManual?: boolean;
  lastAutoMapped?: string | null;
}

const PAGE_SIZE = 100; // Load channels in pages for better performance

export default function IptvChannelsSettings() {
  // Which view is active: the channel-centric list, or the league-centric
  // coverage report (folded in from the old standalone /iptv/coverage page).
  const [searchParams] = useSearchParams();
  const [view, setView] = useState<'channels' | 'coverage'>(
    searchParams.get('view') === 'coverage' ? 'coverage' : 'channels'
  );
  // Under 640px the table becomes a card list (no data tables on phones).
  const isPhone = useMediaQuery('(max-width: 639px)');

  // State
  const [channels, setChannels] = useState<IptvChannel[]>([]);
  const [leagues, setLeagues] = useState<League[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [totalChannels, setTotalChannels] = useState(0);
  const [currentPage, setCurrentPage] = useState(0);
  const [hasMore, setHasMore] = useState(true);

  // Filters - default to sports only since this is a sports app
  const [searchQuery, setSearchQuery] = useState('');
  const [filterSportsOnly, setFilterSportsOnly] = useState(true);
  const [filterEnabledOnly, setFilterEnabledOnly] = useState(false);
  const [filterHasEpgOnly, setFilterHasEpgOnly] = useState(false);
  const [filterStatus, setFilterStatus] = useState<string>('all');
  const [filterFavoritesOnly, setFilterFavoritesOnly] = useState(false);
  const [showHidden, setShowHidden] = useState(false);
  const [selectedCountries, setSelectedCountries] = useState<Set<string>>(new Set());
  const [showCountryDropdown, setShowCountryDropdown] = useState(false);
  const [selectedGroups, setSelectedGroups] = useState<Set<string>>(new Set());
  const [showGroupDropdown, setShowGroupDropdown] = useState(false);

  // Available filter options loaded from API (all channels, not just loaded ones)
  const [availableCountries, setAvailableCountries] = useState<string[]>([]);
  const [availableGroups, setAvailableGroups] = useState<string[]>([]);

  // Selection state for bulk operations
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());

  // Mapping modal state
  const [mappingChannel, setMappingChannel] = useState<IptvChannel | null>(null);
  const [channelMappings, setChannelMappings] = useState<LeagueMapping[]>([]);
  const [selectedLeagues, setSelectedLeagues] = useState<number[]>([]);
  const [preferredLeagueId, setPreferredLeagueId] = useState<number | null>(null);

  // Testing state
  const [testingChannelIds, setTestingChannelIds] = useState<Set<number>>(new Set());
  const [bulkTesting, setBulkTesting] = useState(false);
  const [isAutoMapping, setIsAutoMapping] = useState(false);
  const [isAutoMappingEpg, setIsAutoMappingEpg] = useState(false);

  // Stream player state
  const [playerChannel, setPlayerChannel] = useState<IptvChannel | null>(null);

  // Load data on mount
  useEffect(() => {
    loadChannels(0, true);
    loadLeagues();
    loadFilterOptions();
  }, []);

  // Reload when filters change
  useEffect(() => {
    loadChannels(0, true);
  }, [filterSportsOnly, filterEnabledOnly, filterFavoritesOnly, filterHasEpgOnly, selectedGroups, selectedCountries]);

  const loadChannels = async (page: number = 0, reset: boolean = false) => {
    try {
      setIsLoading(true);
      const offset = page * PAGE_SIZE;
      const { data } = await apiClient.get<IptvChannel[]>('/iptv/channels', {
        params: {
          sportsOnly: filterSportsOnly ? true : undefined,
          enabledOnly: filterEnabledOnly ? true : undefined,
          favoritesOnly: filterFavoritesOnly ? true : undefined,
          hasEpgOnly: filterHasEpgOnly ? true : undefined,
          search: searchQuery || undefined,
          groups: selectedGroups.size > 0 ? Array.from(selectedGroups).join(',') : undefined,
          countries: selectedCountries.size > 0 ? Array.from(selectedCountries).join(',') : undefined,
          limit: PAGE_SIZE,
          offset,
        },
      });

      if (reset) {
        setChannels(Array.isArray(data) ? data : []);
      } else {
        setChannels(prev => [...prev, ...(Array.isArray(data) ? data : [])]);
      }

      setCurrentPage(page);
      setHasMore(data.length === PAGE_SIZE);
      if (page === 0) {
        setTotalChannels(data.length); // Will be updated as we load more
      } else {
        setTotalChannels(prev => reset ? data.length : prev + data.length);
      }
    } catch (err: any) {
      setError(err.message || 'Failed to load channels');
    } finally {
      setIsLoading(false);
    }
  };

  const loadLeagues = async () => {
    try {
      const { data } = await apiClient.get<League[]>('/leagues');
      setLeagues(Array.isArray(data) ? data : []);
    } catch (err: any) {
      console.error('Failed to load leagues:', err);
      setLeagues([]);
    }
  };

  // Load all available filter options (countries and groups) from API
  const loadFilterOptions = async () => {
    try {
      const [countriesRes, groupsRes] = await Promise.all([
        apiClient.get<string[]>('/iptv/countries'),
        apiClient.get<string[]>('/iptv/groups'),
      ]);
      setAvailableCountries(Array.isArray(countriesRes.data) ? countriesRes.data : []);
      setAvailableGroups(Array.isArray(groupsRes.data) ? groupsRes.data : []);
    } catch (err: any) {
      console.error('Failed to load filter options:', err);
      setAvailableCountries([]);
      setAvailableGroups([]);
    }
  };

  // Filter channels client-side for instant feedback
  const filteredChannels = useMemo(() => {
    return channels.filter((channel) => {
      // Hide hidden channels unless showHidden is enabled
      if (!showHidden && channel.isHidden) return false;
      if (filterSportsOnly && !channel.isSportsChannel) return false;
      if (filterEnabledOnly && !channel.isEnabled) return false;
      if (filterFavoritesOnly && !channel.isFavorite) return false;
      if (filterStatus !== 'all' && channel.status.toLowerCase() !== filterStatus) return false;
      // Country filter - if any countries are selected, channel must match one of them
      if (selectedCountries.size > 0) {
        const channelCountry = channel.country?.trim() || '';
        if (!selectedCountries.has(channelCountry)) return false;
      }
      // Group filter - if any groups are selected, channel must match one of them
      if (selectedGroups.size > 0) {
        const channelGroup = channel.group?.trim() || '';
        if (!selectedGroups.has(channelGroup)) return false;
      }
      if (searchQuery) {
        const query = searchQuery.toLowerCase();
        return (
          channel.name.toLowerCase().includes(query) ||
          (channel.group && channel.group.toLowerCase().includes(query))
        );
      }
      return true;
    });
  }, [channels, filterSportsOnly, filterEnabledOnly, filterFavoritesOnly, showHidden, filterStatus, searchQuery, selectedCountries, selectedGroups]);

  // Selection handlers
  const handleToggleSelect = (id: number) => {
    setSelectedIds((prev) => {
      const newSet = new Set(prev);
      if (newSet.has(id)) {
        newSet.delete(id);
      } else {
        newSet.add(id);
      }
      return newSet;
    });
  };

  const handleSelectAll = () => {
    if (selectedIds.size === filteredChannels.length && filteredChannels.length > 0) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(filteredChannels.map((c) => c.id)));
    }
  };

  // Channel operations
  const handleToggleChannel = async (channel: IptvChannel) => {
    try {
      const { data } = await apiClient.post<IptvChannel>(`/iptv/channels/${channel.id}/toggle`);
      setChannels((prev) => prev.map((c) => (c.id === channel.id ? data : c)));
      toast.success(data.isEnabled ? 'Channel Enabled' : 'Channel Disabled');
    } catch (err: any) {
      toast.error('Failed to toggle channel', { description: err.message });
    }
  };

  const handleTestChannel = async (channelId: number) => {
    try {
      setTestingChannelIds((prev) => new Set(prev).add(channelId));
      const { data } = await apiClient.post<{ success: boolean; error?: string; status?: string }>(
        `/iptv/channels/${channelId}/test`
      );
      // Update the channel status immediately in state
      setChannels((prev) =>
        prev.map((c) =>
          c.id === channelId
            ? { ...c, status: data.success ? 'Online' : 'Offline', lastChecked: new Date().toISOString() }
            : c
        )
      );
      if (data.success) {
        toast.success('Channel Online');
      } else {
        toast.error('Channel Offline', { description: data.error });
      }
    } catch (err: any) {
      toast.error('Failed to test channel', { description: err.message });
    } finally {
      setTestingChannelIds((prev) => {
        const newSet = new Set(prev);
        newSet.delete(channelId);
        return newSet;
      });
    }
  };

  const handleToggleSports = async (channel: IptvChannel) => {
    try {
      const { data } = await apiClient.post<IptvChannel>(`/iptv/channels/${channel.id}/sports`, {
        isSportsChannel: !channel.isSportsChannel,
      });
      setChannels((prev) => prev.map((c) => (c.id === channel.id ? data : c)));
      toast.success(data.isSportsChannel ? 'Marked as Sports Channel' : 'Unmarked as Sports Channel');
    } catch (err: any) {
      toast.error('Failed to update channel', { description: err.message });
    }
  };

  // Bulk operations
  const handleBulkEnable = async (enabled: boolean) => {
    try {
      const channelIds = Array.from(selectedIds);
      await apiClient.post('/iptv/channels/bulk/enable', { channelIds, enabled });
      // Update channels immediately in state
      setChannels((prev) =>
        prev.map((c) =>
          channelIds.includes(c.id) ? { ...c, isEnabled: enabled } : c
        )
      );
      setSelectedIds(new Set());
      toast.success(`${enabled ? 'Enabled' : 'Disabled'} ${channelIds.length} channels`);
    } catch (err: any) {
      toast.error('Bulk operation failed', { description: err.message });
    }
  };

  const handleBulkTest = async () => {
    try {
      setBulkTesting(true);
      const channelIds = Array.from(selectedIds);
      const { data } = await apiClient.post<{
        success: boolean;
        results: { channelId: number; success: boolean; error?: string }[];
      }>('/iptv/channels/bulk/test', { channelIds });

      const onlineCount = data.results.filter((r) => r.success).length;
      const offlineCount = data.results.filter((r) => !r.success).length;

      // Update channel statuses immediately in state
      const resultsMap = new Map(data.results.map((r) => [r.channelId, r.success]));
      setChannels((prev) =>
        prev.map((c) => {
          if (resultsMap.has(c.id)) {
            return {
              ...c,
              status: resultsMap.get(c.id) ? 'Online' : 'Offline',
              lastChecked: new Date().toISOString(),
            };
          }
          return c;
        })
      );
      toast.success(`Tested ${channelIds.length} channels`, {
        description: `${onlineCount} online, ${offlineCount} offline`,
      });
    } catch (err: any) {
      toast.error('Bulk test failed', { description: err.message });
    } finally {
      setBulkTesting(false);
    }
  };

  const handleAutoMap = async () => {
    try {
      setIsAutoMapping(true);
      const { data } = await apiClient.post<{
        success: boolean;
        channelsProcessed: number;
        mappingsCreated: number;
        errors: number;
        message: string;
      }>('/iptv/channels/auto-map');

      if (data.success) {
        await loadChannels(0, true);
        toast.success('Auto-mapping complete', {
          description: data.message,
        });
      } else {
        toast.error('Auto-mapping failed');
      }
    } catch (err: any) {
      toast.error('Auto-mapping failed', { description: err.message });
    } finally {
      setIsAutoMapping(false);
    }
  };

  const handleAutoMapEpg = async () => {
    try {
      setIsAutoMappingEpg(true);
      const { data } = await apiClient.post<{ mappedCount: number }>('/epg/auto-map');

      if (data.mappedCount > 0) {
        await loadChannels(0, true);
        toast.success('EPG auto-mapping complete', {
          description: `Mapped ${data.mappedCount} channels to EPG data`,
        });
      } else {
        toast.info('No new channels to map', {
          description: 'All channels are already mapped, or no matching EPG channels were found. If you have not yet, sync an EPG source first from the TV Guide page.',
        });
      }
    } catch (err: any) {
      toast.error('EPG auto-mapping failed', { description: err.message });
    } finally {
      setIsAutoMappingEpg(false);
    }
  };

  const handleUnmapEpg = async (channel: IptvChannel) => {
    try {
      await apiClient.delete(`/iptv/channels/${channel.id}/map-epg`);
      await loadChannels(0, true);
      toast.success('EPG mapping cleared', {
        description: `${channel.name} can now be auto-mapped again`,
      });
    } catch (err: any) {
      toast.error('Failed to clear EPG mapping', { description: err.message });
    }
  };

  // Manual EPG picker: search EPG channels by name and map one explicitly,
  // for the cases auto-map gets wrong or misses entirely.
  const [epgPickerChannel, setEpgPickerChannel] = useState<IptvChannel | null>(null);
  const [epgPickerSearch, setEpgPickerSearch] = useState('');
  const [epgPickerResults, setEpgPickerResults] = useState<Array<{
    id: number; channelId: string; displayName: string; iconUrl?: string;
  }>>([]);
  const [isEpgPickerSearching, setIsEpgPickerSearching] = useState(false);

  const openEpgPicker = (channel: IptvChannel) => {
    setEpgPickerChannel(channel);
    // Seed the search with the channel name so likely matches appear
    // immediately; the user can refine from there.
    setEpgPickerSearch(channel.name || '');
  };

  useEffect(() => {
    if (!epgPickerChannel) {
      return;
    }
    const handle = setTimeout(async () => {
      try {
        setIsEpgPickerSearching(true);
        const { data } = await apiClient.get<Array<{
          id: number; channelId: string; displayName: string; iconUrl?: string;
        }>>('/epg/channels', {
          params: { search: epgPickerSearch || undefined, limit: 50 },
        });
        setEpgPickerResults(Array.isArray(data) ? data : []);
      } catch {
        setEpgPickerResults([]);
      } finally {
        setIsEpgPickerSearching(false);
      }
    }, 300);
    return () => clearTimeout(handle);
  }, [epgPickerChannel, epgPickerSearch]);

  // Per-team DVR channel preference, shown inside the league mapping
  // modal: the resolver records a mapped team's events from this channel
  // even when the league prefers a different one.
  const [channelTeamMappings, setChannelTeamMappings] = useState<Array<{
    id: number; teamId: number; teamName?: string;
  }>>([]);
  const [teamPickerLeagueId, setTeamPickerLeagueId] = useState<number | ''>('');
  const [teamPickerTeams, setTeamPickerTeams] = useState<Array<{ id: number; name: string }>>([]);
  const [teamPickerTeamId, setTeamPickerTeamId] = useState<number | ''>('');

  useEffect(() => {
    if (!mappingChannel) {
      setChannelTeamMappings([]);
      setTeamPickerLeagueId('');
      setTeamPickerTeamId('');
      setTeamPickerTeams([]);
      return;
    }
    apiClient
      .get<Array<{ id: number; teamId: number; teamName?: string }>>(`/iptv/channels/${mappingChannel.id}/team-mappings`)
      .then(({ data }) => setChannelTeamMappings(Array.isArray(data) ? data : []))
      .catch(() => setChannelTeamMappings([]));
  }, [mappingChannel]);

  useEffect(() => {
    if (teamPickerLeagueId === '') {
      setTeamPickerTeams([]);
      setTeamPickerTeamId('');
      return;
    }
    apiClient
      .get<Array<{ id: number; name: string }>>('/teams', { params: { leagueId: teamPickerLeagueId } })
      .then(({ data }) => setTeamPickerTeams(Array.isArray(data) ? data : []))
      .catch(() => setTeamPickerTeams([]));
  }, [teamPickerLeagueId]);

  const handleAddTeamMapping = async () => {
    if (!mappingChannel || teamPickerTeamId === '') {
      return;
    }
    try {
      await apiClient.post(`/iptv/channels/${mappingChannel.id}/team-mappings/${teamPickerTeamId}`);
      const { data } = await apiClient.get<Array<{ id: number; teamId: number; teamName?: string }>>(
        `/iptv/channels/${mappingChannel.id}/team-mappings`);
      setChannelTeamMappings(Array.isArray(data) ? data : []);
      setTeamPickerTeamId('');
      toast.success('Team preference saved', {
        description: `This channel is now preferred for that team's recordings`,
      });
    } catch (err: any) {
      toast.error('Failed to save team preference', { description: err.message });
    }
  };

  const handleRemoveTeamMapping = async (teamId: number) => {
    if (!mappingChannel) {
      return;
    }
    try {
      await apiClient.delete(`/iptv/channels/${mappingChannel.id}/team-mappings/${teamId}`);
      setChannelTeamMappings((prev) => prev.filter((m) => m.teamId !== teamId));
    } catch (err: any) {
      toast.error('Failed to remove team preference', { description: err.message });
    }
  };

  const handleMapEpg = async (epgChannelId: string, epgDisplayName: string) => {
    if (!epgPickerChannel) {
      return;
    }
    try {
      await apiClient.post(
        `/iptv/channels/${epgPickerChannel.id}/map-epg?epgChannelId=${encodeURIComponent(epgChannelId)}`
      );
      toast.success('EPG channel mapped', {
        description: `${epgPickerChannel.name} -> ${epgDisplayName}`,
      });
      setEpgPickerChannel(null);
      await loadChannels(0, true);
    } catch (err: any) {
      toast.error('Failed to map EPG channel', { description: err.message });
    }
  };

  const handleUpdatePreferred = async () => {
    try {
      const { data } = await apiClient.post<{
        success: boolean;
        leaguesUpdated: number;
        message: string;
      }>('/iptv/leagues/update-preferred');

      if (data.success) {
        toast.success('Preferred channels updated', {
          description: data.message,
        });
      }
    } catch (err: any) {
      toast.error('Failed to update preferred channels', { description: err.message });
    }
  };

  // Favorite operations
  const handleToggleFavorite = async (channel: IptvChannel) => {
    try {
      const newStatus = !channel.isFavorite;
      await apiClient.post(`/iptv/channels/${channel.id}/favorite`, { isFavorite: newStatus });
      setChannels((prev) => prev.map((c) => (c.id === channel.id ? { ...c, isFavorite: newStatus } : c)));
      toast.success(newStatus ? 'Added to Favorites' : 'Removed from Favorites');
    } catch (err: any) {
      toast.error('Failed to update favorite status', { description: err.message });
    }
  };

  const handleBulkFavorite = async (isFavorite: boolean) => {
    try {
      const channelIds = Array.from(selectedIds);
      await apiClient.post('/iptv/channels/bulk/favorite', { channelIds, isFavorite });
      setChannels((prev) =>
        prev.map((c) => (channelIds.includes(c.id) ? { ...c, isFavorite } : c))
      );
      setSelectedIds(new Set());
      toast.success(`${isFavorite ? 'Added' : 'Removed'} ${channelIds.length} channels ${isFavorite ? 'to' : 'from'} favorites`);
    } catch (err: any) {
      toast.error('Bulk favorite operation failed', { description: err.message });
    }
  };

  // Hide operations
  const handleToggleHidden = async (channel: IptvChannel) => {
    try {
      const newStatus = !channel.isHidden;
      await apiClient.post(`/iptv/channels/${channel.id}/hidden`, { isHidden: newStatus });
      setChannels((prev) => prev.map((c) => (c.id === channel.id ? { ...c, isHidden: newStatus } : c)));
      toast.success(newStatus ? 'Channel Hidden' : 'Channel Visible');
    } catch (err: any) {
      toast.error('Failed to update hidden status', { description: err.message });
    }
  };

  const handleBulkHidden = async (isHidden: boolean) => {
    try {
      const channelIds = Array.from(selectedIds);
      await apiClient.post('/iptv/channels/bulk/hidden', { channelIds, isHidden });
      setChannels((prev) =>
        prev.map((c) => (channelIds.includes(c.id) ? { ...c, isHidden } : c))
      );
      setSelectedIds(new Set());
      toast.success(`${isHidden ? 'Hid' : 'Showed'} ${channelIds.length} channels`);
    } catch (err: any) {
      toast.error('Bulk hide operation failed', { description: err.message });
    }
  };

  const handleHideNonSports = async () => {
    try {
      const { data } = await apiClient.post<{ success: boolean; channelsHidden: number; message: string }>(
        '/iptv/channels/hide-non-sports'
      );
      if (data.success) {
        await loadChannels(0, true);
        toast.success('Non-sports channels hidden', {
          description: data.message,
        });
      }
    } catch (err: any) {
      toast.error('Failed to hide non-sports channels', { description: err.message });
    }
  };

  const handleUnhideAll = async () => {
    try {
      const { data } = await apiClient.post<{ success: boolean; channelsUnhidden: number; message: string }>(
        '/iptv/channels/unhide-all'
      );
      if (data.success) {
        await loadChannels(0, true);
        toast.success('All channels visible', {
          description: data.message,
        });
      }
    } catch (err: any) {
      toast.error('Failed to unhide channels', { description: err.message });
    }
  };

  // Mapping operations
  const openMappingModal = async (channel: IptvChannel) => {
    setMappingChannel(channel);
    try {
      const { data } = await apiClient.get<LeagueMapping[]>(`/iptv/channels/${channel.id}/mappings`);
      setChannelMappings(data);
      setSelectedLeagues(data.map((m) => m.leagueId));
      const preferred = data.find((m) => m.isPreferred);
      setPreferredLeagueId(preferred?.leagueId || null);
    } catch (err: any) {
      toast.error('Failed to load mappings', { description: err.message });
    }
  };

  const handleSaveMappings = async () => {
    if (!mappingChannel) return;

    try {
      await apiClient.post('/iptv/channels/map', {
        channelId: mappingChannel.id,
        leagueIds: selectedLeagues,
        preferredLeagueId: preferredLeagueId,
      });
      await loadChannels();
      setMappingChannel(null);
      toast.success('Mappings saved', {
        description: `Mapped to ${selectedLeagues.length} league(s)`,
      });
    } catch (err: any) {
      toast.error('Failed to save mappings', { description: err.message });
    }
  };

  const toggleLeagueSelection = (leagueId: number) => {
    setSelectedLeagues((prev) => {
      if (prev.includes(leagueId)) {
        // If removing the preferred league, clear preferred
        if (preferredLeagueId === leagueId) {
          setPreferredLeagueId(null);
        }
        return prev.filter((id) => id !== leagueId);
      } else {
        return [...prev, leagueId];
      }
    });
  };

  const getStatusColor = (status: string) => {
    switch (status.toLowerCase()) {
      case 'online':
        return 'bg-green-900/30 text-green-400';
      case 'offline':
        return 'bg-red-900/30 text-red-400';
      case 'error':
        return 'bg-yellow-900/30 text-yellow-400';
      default:
        return 'bg-gray-800 text-gray-400';
    }
  };

  const getQualityColor = (quality?: string) => {
    switch (quality?.toUpperCase()) {
      case '4K':
        return 'bg-purple-900/30 text-purple-400';
      case 'FHD':
        return 'bg-blue-900/30 text-blue-400';
      case 'HD':
        return 'bg-green-900/30 text-green-400';
      case 'SD':
        return 'bg-yellow-900/30 text-yellow-400';
      default:
        return 'bg-gray-800 text-gray-400';
    }
  };

  return (
    <PageShell className="pb-8">
      <PageHeader
        title="IPTV Channels"
        subtitle="Manage channels across all IPTV sources and map them to leagues"
        actions={view === 'channels' ? (
          /* Per-step tools live behind Advanced; the primary "Sync Now" on the
             Sources page runs the whole pipeline (sources -> EPG -> mapping)
             so most users never need these individually. */
          <Menu as="div" className="relative">
            <Menu.Button className="inline-flex items-center gap-2 rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-200 transition-colors hover:bg-gray-700">
              {(isAutoMapping || isAutoMappingEpg) ? (
                <ArrowPathIcon className="h-4 w-4 animate-spin" />
              ) : (
                <WrenchScrewdriverIcon className="h-4 w-4 text-gray-400" />
              )}
              Advanced
              <ChevronDownIcon className="h-4 w-4 text-gray-400" />
            </Menu.Button>
            <Menu.Items className="absolute right-0 z-40 mt-2 w-56 overflow-hidden rounded-lg border border-gray-700 bg-gray-900 shadow-xl shadow-black/50 focus:outline-none">
              <Menu.Item>
                {({ active }) => (
                  <button
                    onClick={handleAutoMap}
                    disabled={isAutoMapping}
                    className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm text-gray-200 disabled:opacity-50 ${active ? 'bg-gray-800' : ''}`}
                  >
                    <LinkIcon className="h-4 w-4 text-gray-400" />
                    {isAutoMapping ? 'Mapping leagues…' : 'Auto-Map Leagues'}
                  </button>
                )}
              </Menu.Item>
              <Menu.Item>
                {({ active }) => (
                  <button
                    onClick={handleAutoMapEpg}
                    disabled={isAutoMappingEpg}
                    className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm text-gray-200 disabled:opacity-50 ${active ? 'bg-gray-800' : ''}`}
                  >
                    <SignalIcon className="h-4 w-4 text-gray-400" />
                    {isAutoMappingEpg ? 'Mapping EPG…' : 'Auto-Map EPG'}
                  </button>
                )}
              </Menu.Item>
              <Menu.Item>
                {({ active }) => (
                  <button
                    onClick={handleUpdatePreferred}
                    className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm text-gray-200 ${active ? 'bg-gray-800' : ''}`}
                  >
                    <ArrowPathIcon className="h-4 w-4 text-gray-400" />
                    Update Preferred
                  </button>
                )}
              </Menu.Item>
            </Menu.Items>
          </Menu>
        ) : undefined}
      />

      {/* View tabs: channel-centric list vs league-centric coverage. Coverage
          was its own /iptv/coverage page; it is now a view of the same screen
          so mapping channels and checking what they cover live in one place. */}
      <div className="mb-6 flex gap-1 border-b border-gray-800">
        {([
          { key: 'channels', label: 'Channels' },
          { key: 'coverage', label: 'Coverage' },
        ] as const).map((t) => (
          <button
            key={t.key}
            onClick={() => setView(t.key)}
            className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors ${
              view === t.key
                ? 'border-red-500 text-white'
                : 'border-transparent text-gray-400 hover:text-gray-200'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {view === 'coverage' ? (
        <IptvCoveragePage embedded />
      ) : (
      <>

      {/* Error Alert */}
      {error && (
        <div className="mb-6 bg-red-950/30 border border-red-900/50 rounded-lg p-4 flex items-start">
          <XCircleIcon className="w-6 h-6 text-red-400 mr-3 flex-shrink-0 mt-0.5" />
          <div className="flex-1">
            <h3 className="text-lg font-semibold text-red-400 mb-1">Error</h3>
            <p className="text-sm text-gray-300">{error}</p>
          </div>
          <button onClick={() => setError(null)} className="text-gray-400 hover:text-white ml-4">
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>
      )}

        {/* Helper Card */}
        <div className="mb-8 rounded-lg border border-gray-800 bg-gray-900/70 p-5">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
            <SignalIcon className="h-5 w-5 flex-shrink-0 text-gray-400 sm:mt-0.5" />
            <div className="min-w-0">
              <p className="mb-2 text-sm font-semibold uppercase tracking-wide text-gray-200">
                Mapping Tips
              </p>
              <ul className="space-y-1 text-sm text-gray-300">
                <li>
                  <span className="mr-2 text-red-400">*</span>
                  Map channels to leagues to enable automatic DVR recording when events are scheduled
                </li>
                <li>
                  <span className="mr-2 text-red-400">*</span>
                  Use <strong>Auto-Map</strong> to detect networks like ESPN or Sky Sports and suggest league mappings
                </li>
                <li>
                  <span className="mr-2 text-red-400">*</span>
                  <strong>EPG mapping</strong> links channels to guide data for TV Guide. Sync an EPG source first from the TV Guide page (EPG Sources).
                </li>
                <li>
                  <span className="mr-2 text-red-400">*</span>
                  Preferred mappings choose the highest quality stream automatically for DVR recording
                </li>
              </ul>
            </div>
          </div>
        </div>

        {/* Filters and Bulk Actions */}
        <div className="mb-6 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-4">
          <div className="flex flex-wrap items-center gap-4">
            {/* Search */}
            <div className="flex-1 min-w-[200px]">
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                placeholder="Search channels..."
              />
            </div>

            {/* Filters */}
            <div className="flex items-center space-x-4 flex-wrap gap-2">
              <label className="flex items-center space-x-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={filterSportsOnly}
                  onChange={(e) => setFilterSportsOnly(e.target.checked)}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm text-gray-300">Sports Only</span>
              </label>

              <label className="flex items-center space-x-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={filterFavoritesOnly}
                  onChange={(e) => setFilterFavoritesOnly(e.target.checked)}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-yellow-600 focus:ring-yellow-600"
                />
                <span className="text-sm text-gray-300 flex items-center space-x-1">
                  <StarIconSolid className="w-4 h-4 text-yellow-400" />
                  <span>Favorites</span>
                </span>
              </label>

              <label className="flex items-center space-x-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={filterEnabledOnly}
                  onChange={(e) => setFilterEnabledOnly(e.target.checked)}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm text-gray-300">Enabled Only</span>
              </label>

              <label className="flex items-center space-x-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={filterHasEpgOnly}
                  onChange={(e) => setFilterHasEpgOnly(e.target.checked)}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm text-gray-300">Has EPG Only</span>
              </label>

              <label className="flex items-center space-x-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={showHidden}
                  onChange={(e) => setShowHidden(e.target.checked)}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-gray-600 focus:ring-gray-600"
                />
                <span className="text-sm text-gray-300 flex items-center space-x-1">
                  <EyeSlashIcon className="w-4 h-4 text-gray-400" />
                  <span>Show Hidden</span>
                </span>
              </label>

              <select
                value={filterStatus}
                onChange={(e) => setFilterStatus(e.target.value)}
                className="px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
              >
                <option value="all">All Status</option>
                <option value="online">Online</option>
                <option value="offline">Offline</option>
                <option value="unknown">Unknown</option>
              </select>

              {/* Country Multi-Select Filter */}
              <div className="relative">
                <button
                  onClick={() => setShowCountryDropdown(!showCountryDropdown)}
                  className={`px-3 py-2 bg-gray-800 border rounded-lg text-sm flex items-center space-x-2 transition-colors ${
                    selectedCountries.size > 0
                      ? 'border-red-600 text-white'
                      : 'border-gray-700 text-gray-300 hover:border-gray-600'
                  }`}
                >
                  <GlobeAltIcon className="w-4 h-4" />
                  <span>
                    {selectedCountries.size === 0
                      ? 'All Countries'
                      : `${selectedCountries.size} ${selectedCountries.size === 1 ? 'Country' : 'Countries'}`}
                  </span>
                  <ChevronDownIcon className={`w-4 h-4 transition-transform ${showCountryDropdown ? 'rotate-180' : ''}`} />
                </button>

                {showCountryDropdown && (
                  <>
                    {/* Backdrop */}
                    <div
                      className="fixed inset-0 z-10"
                      onClick={() => setShowCountryDropdown(false)}
                    />
                    {/* Dropdown */}
                    <div className="absolute top-full left-0 mt-1 w-64 max-h-80 overflow-y-auto bg-gray-900 border border-gray-700 rounded-lg shadow-xl z-20">
                      {/* Header */}
                      <div className="sticky top-0 bg-gray-900 border-b border-gray-700 p-2 flex items-center justify-between">
                        <span className="text-xs text-gray-400">{availableCountries.length} countries</span>
                        <div className="flex items-center space-x-2">
                          <button
                            onClick={() => setSelectedCountries(new Set(availableCountries))}
                            className="text-xs text-blue-400 hover:text-blue-300"
                          >
                            Select All
                          </button>
                          <span className="text-gray-600">|</span>
                          <button
                            onClick={() => setSelectedCountries(new Set())}
                            className="text-xs text-gray-400 hover:text-gray-300"
                          >
                            Clear
                          </button>
                        </div>
                      </div>
                      {/* Country list */}
                      <div className="p-1">
                        {availableCountries.length === 0 ? (
                          <div className="px-3 py-4 text-sm text-gray-500 text-center">
                            No country data available
                          </div>
                        ) : (
                          availableCountries.map((country) => (
                            <label
                              key={country}
                              className="flex items-center space-x-2 px-3 py-1.5 hover:bg-gray-800 rounded cursor-pointer"
                            >
                              <input
                                type="checkbox"
                                checked={selectedCountries.has(country)}
                                onChange={() => {
                                  setSelectedCountries((prev) => {
                                    const newSet = new Set(prev);
                                    if (newSet.has(country)) {
                                      newSet.delete(country);
                                    } else {
                                      newSet.add(country);
                                    }
                                    return newSet;
                                  });
                                }}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                              />
                              <span className="text-sm text-gray-300">{country}</span>
                            </label>
                          ))
                        )}
                      </div>
                    </div>
                  </>
                )}
              </div>

              {/* Group Multi-Select Filter */}
              <div className="relative">
                <button
                  onClick={() => setShowGroupDropdown(!showGroupDropdown)}
                  className={`px-3 py-2 bg-gray-800 border rounded-lg text-sm flex items-center space-x-2 transition-colors ${
                    selectedGroups.size > 0
                      ? 'border-red-600 text-white'
                      : 'border-gray-700 text-gray-300 hover:border-gray-600'
                  }`}
                >
                  <FunnelIcon className="w-4 h-4" />
                  <span>
                    {selectedGroups.size === 0
                      ? 'All Groups'
                      : `${selectedGroups.size} ${selectedGroups.size === 1 ? 'Group' : 'Groups'}`}
                  </span>
                  <ChevronDownIcon className={`w-4 h-4 transition-transform ${showGroupDropdown ? 'rotate-180' : ''}`} />
                </button>

                {showGroupDropdown && (
                  <>
                    {/* Backdrop */}
                    <div
                      className="fixed inset-0 z-10"
                      onClick={() => setShowGroupDropdown(false)}
                    />
                    {/* Dropdown */}
                    <div className="absolute top-full left-0 mt-1 w-64 max-h-80 overflow-y-auto bg-gray-900 border border-gray-700 rounded-lg shadow-xl z-20">
                      {/* Header */}
                      <div className="sticky top-0 bg-gray-900 border-b border-gray-700 p-2 flex items-center justify-between">
                        <span className="text-xs text-gray-400">{availableGroups.length} groups</span>
                        <div className="flex items-center space-x-2">
                          <button
                            onClick={() => setSelectedGroups(new Set(availableGroups))}
                            className="text-xs text-blue-400 hover:text-blue-300"
                          >
                            Select All
                          </button>
                          <span className="text-gray-600">|</span>
                          <button
                            onClick={() => setSelectedGroups(new Set())}
                            className="text-xs text-gray-400 hover:text-gray-300"
                          >
                            Clear
                          </button>
                        </div>
                      </div>
                      {/* Group list */}
                      <div className="p-1">
                        {availableGroups.length === 0 ? (
                          <div className="px-3 py-4 text-sm text-gray-500 text-center">
                            No group data available
                          </div>
                        ) : (
                          availableGroups.map((group) => (
                            <label
                              key={group}
                              className="flex items-center space-x-2 px-3 py-1.5 hover:bg-gray-800 rounded cursor-pointer"
                            >
                              <input
                                type="checkbox"
                                checked={selectedGroups.has(group)}
                                onChange={() => {
                                  setSelectedGroups((prev) => {
                                    const newSet = new Set(prev);
                                    if (newSet.has(group)) {
                                      newSet.delete(group);
                                    } else {
                                      newSet.add(group);
                                    }
                                    return newSet;
                                  });
                                }}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                              />
                              <span className="text-sm text-gray-300">{group}</span>
                            </label>
                          ))
                        )}
                      </div>
                    </div>
                  </>
                )}
              </div>
            </div>

            {/* Refresh */}
            <button
              onClick={() => loadChannels(0, true)}
              className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded-lg transition-colors"
              title="Refresh"
            >
              <ArrowPathIcon className="w-5 h-5" />
            </button>
          </div>

          {/* Bulk Actions */}
          {selectedIds.size > 0 && (
            <div className="mt-4 pt-4 border-t border-gray-800 flex items-center flex-wrap gap-2">
              <span className="text-sm text-gray-400">{selectedIds.size} selected</span>
              <button
                onClick={() => handleBulkEnable(true)}
                className="px-3 py-1.5 bg-green-900/30 hover:bg-green-900/50 text-green-400 rounded text-sm transition-colors"
              >
                Enable All
              </button>
              <button
                onClick={() => handleBulkEnable(false)}
                className="px-3 py-1.5 bg-red-900/30 hover:bg-red-900/50 text-red-400 rounded text-sm transition-colors"
              >
                Disable All
              </button>
              <button
                onClick={() => handleBulkFavorite(true)}
                className="px-3 py-1.5 bg-yellow-900/30 hover:bg-yellow-900/50 text-yellow-400 rounded text-sm transition-colors flex items-center space-x-1"
              >
                <StarIconSolid className="w-4 h-4" />
                <span>Favorite</span>
              </button>
              <button
                onClick={() => handleBulkFavorite(false)}
                className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-400 rounded text-sm transition-colors flex items-center space-x-1"
              >
                <StarIconOutline className="w-4 h-4" />
                <span>Unfavorite</span>
              </button>
              <button
                onClick={() => handleBulkHidden(true)}
                className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-400 rounded text-sm transition-colors flex items-center space-x-1"
              >
                <EyeSlashIcon className="w-4 h-4" />
                <span>Hide</span>
              </button>
              <button
                onClick={() => handleBulkHidden(false)}
                className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-400 rounded text-sm transition-colors flex items-center space-x-1"
              >
                <EyeIcon className="w-4 h-4" />
                <span>Unhide</span>
              </button>
              <button
                onClick={handleBulkTest}
                disabled={bulkTesting}
                className="px-3 py-1.5 bg-blue-900/30 hover:bg-blue-900/50 text-blue-400 rounded text-sm transition-colors disabled:opacity-50"
              >
                {bulkTesting ? 'Testing...' : 'Test All'}
              </button>
              <button
                onClick={() => setSelectedIds(new Set())}
                className="px-3 py-1.5 text-gray-400 hover:text-white text-sm"
              >
                Clear Selection
              </button>
            </div>
          )}

          {/* Quick Actions */}
          <div className="mt-4 pt-4 border-t border-gray-800 flex items-center space-x-4">
            <button
              onClick={handleHideNonSports}
              className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-300 rounded text-sm transition-colors flex items-center space-x-1"
            >
              <EyeSlashIcon className="w-4 h-4" />
              <span>Hide All Non-Sports</span>
            </button>
            <button
              onClick={handleUnhideAll}
              className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-300 rounded text-sm transition-colors flex items-center space-x-1"
            >
              <EyeIcon className="w-4 h-4" />
              <span>Unhide All</span>
            </button>
          </div>
        </div>

        {/* Channels: card list on phones, table on sm+ */}
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
          {isPhone ? (
            <div className="divide-y divide-gray-800">
              {filteredChannels.map((channel) => (
                <div
                  key={channel.id}
                  className={`p-4 ${selectedIds.has(channel.id) ? 'bg-red-950/20' : ''}`}
                >
                  <div className="flex items-start gap-3">
                    <input
                      type="checkbox"
                      checked={selectedIds.has(channel.id)}
                      onChange={() => handleToggleSelect(channel.id)}
                      className="mt-1.5 h-4 w-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                    />
                    {channel.logoUrl ? (
                      <img
                        src={channel.logoUrl}
                        alt={channel.name}
                        className="h-10 w-10 flex-none rounded bg-gray-800 object-contain"
                        onError={(e) => {
                          (e.target as HTMLImageElement).style.display = 'none';
                        }}
                      />
                    ) : (
                      <div className="flex h-10 w-10 flex-none items-center justify-center rounded bg-gray-800">
                        <SignalIcon className="h-5 w-5 text-gray-600" />
                      </div>
                    )}
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-1.5">
                        <span className={`truncate font-medium ${channel.isHidden ? 'text-gray-500' : 'text-white'}`}>
                          {channel.name}
                        </span>
                        {channel.isFavorite && <StarIconSolid className="h-3.5 w-3.5 flex-none text-yellow-400" />}
                        {channel.isHidden && <EyeSlashIcon className="h-3.5 w-3.5 flex-none text-gray-500" />}
                      </div>
                      <div className="mt-0.5 truncate text-xs text-gray-500">
                        {channel.channelNumber ? `Ch. ${channel.channelNumber} · ` : ''}
                        {channel.detectedNetwork || channel.group || 'No network'}
                      </div>
                      <div className="mt-2 flex flex-wrap items-center gap-1.5">
                        <span className={`rounded px-2 py-1 text-xs ${getQualityColor(channel.detectedQuality)}`}>
                          {channel.detectedQuality || 'HD'}
                        </span>
                        <span className={`rounded px-2 py-1 text-xs ${getStatusColor(channel.status)}`}>
                          {channel.status}
                        </span>
                        {channel.tvgId ? (
                          <button
                            onClick={() => {
                              if (window.confirm(`Clear the EPG mapping for "${channel.name}"? You can re-run auto-map or wait for it to pick a new match afterward.`)) {
                                handleUnmapEpg(channel);
                              }
                            }}
                            className="rounded bg-green-900/30 px-2 py-1 text-xs text-green-400"
                          >
                            EPG mapped
                          </button>
                        ) : (
                          <button
                            onClick={() => openEpgPicker(channel)}
                            className="rounded bg-gray-800 px-2 py-1 text-xs text-gray-400"
                          >
                            Map EPG…
                          </button>
                        )}
                        <button
                          onClick={() => handleToggleSports(channel)}
                          className={`rounded px-2 py-1 text-xs ${
                            channel.isSportsChannel
                              ? 'bg-green-900/30 text-green-400'
                              : 'bg-gray-800 text-gray-500'
                          }`}
                        >
                          Sports: {channel.isSportsChannel ? 'Yes' : 'No'}
                        </button>
                        <button
                          onClick={() => openMappingModal(channel)}
                          className="flex items-center gap-1 rounded bg-gray-800 px-2 py-1 text-xs text-gray-400"
                        >
                          <LinkIcon className="h-3 w-3" />
                          {channel.mappedLeagueIds?.length || 0} leagues
                        </button>
                      </div>
                    </div>
                  </div>
                  <div className="mt-2 flex items-center justify-end gap-1">
                    <button
                      onClick={() => handleToggleFavorite(channel)}
                      className="rounded p-2 transition-colors hover:bg-gray-800"
                      title={channel.isFavorite ? 'Remove from Favorites' : 'Add to Favorites'}
                    >
                      {channel.isFavorite ? (
                        <StarIconSolid className="h-5 w-5 text-yellow-400" />
                      ) : (
                        <StarIconOutline className="h-5 w-5 text-gray-400" />
                      )}
                    </button>
                    <button
                      onClick={() => handleToggleHidden(channel)}
                      className="rounded p-2 text-gray-400 transition-colors hover:bg-gray-800"
                      title={channel.isHidden ? 'Show Channel' : 'Hide Channel'}
                    >
                      {channel.isHidden ? (
                        <EyeSlashIcon className="h-5 w-5 text-gray-500" />
                      ) : (
                        <EyeIcon className="h-5 w-5" />
                      )}
                    </button>
                    <button
                      onClick={() => handleTestChannel(channel.id)}
                      disabled={testingChannelIds.has(channel.id)}
                      className={`rounded p-2 text-gray-400 transition-colors hover:bg-gray-800 ${
                        testingChannelIds.has(channel.id) ? 'animate-pulse' : ''
                      }`}
                      title="Test Connection"
                    >
                      <BoltIcon className="h-5 w-5" />
                    </button>
                    <button
                      onClick={() => setPlayerChannel(channel)}
                      className="rounded p-2 text-gray-400 transition-colors hover:bg-gray-800"
                      title="Play Stream"
                    >
                      <PlayIcon className="h-5 w-5" />
                    </button>
                    <button
                      onClick={() => handleToggleChannel(channel)}
                      className="rounded p-2 text-gray-400 transition-colors hover:bg-gray-800"
                      title={channel.isEnabled ? 'Disable' : 'Enable'}
                    >
                      {channel.isEnabled ? (
                        <CheckCircleIcon className="h-5 w-5 text-green-400" />
                      ) : (
                        <XCircleIcon className="h-5 w-5" />
                      )}
                    </button>
                  </div>
                </div>
              ))}
            </div>
          ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-black/50 border-b border-gray-800">
                <tr>
                  <th className="w-12 px-4 py-3">
                    <input
                      type="checkbox"
                      checked={selectedIds.size === filteredChannels.length && filteredChannels.length > 0}
                      onChange={handleSelectAll}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                    />
                  </th>
                  <th className="px-4 py-3 text-left text-sm font-medium text-gray-400">Channel</th>
                  <th className="px-4 py-3 text-left text-sm font-medium text-gray-400">Network</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-400">Quality</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-400">Status</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-400">EPG</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-400">Sports</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-400">Mappings</th>
                  <th className="px-4 py-3 text-center text-sm font-medium text-gray-400">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800">
                {filteredChannels.map((channel) => (
                  <tr
                    key={channel.id}
                    className={`hover:bg-gray-800/30 ${selectedIds.has(channel.id) ? 'bg-red-950/20' : ''}`}
                  >
                    <td className="px-4 py-3">
                      <input
                        type="checkbox"
                        checked={selectedIds.has(channel.id)}
                        onChange={() => handleToggleSelect(channel.id)}
                        className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                      />
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center space-x-3">
                        {channel.logoUrl ? (
                          <img
                            src={channel.logoUrl}
                            alt={channel.name}
                            className="w-8 h-8 rounded object-contain bg-gray-800"
                            onError={(e) => {
                              (e.target as HTMLImageElement).style.display = 'none';
                            }}
                          />
                        ) : (
                          <div className="w-8 h-8 rounded bg-gray-800 flex items-center justify-center">
                            <SignalIcon className="w-4 h-4 text-gray-600" />
                          </div>
                        )}
                        <div>
                          <div className="font-medium text-white flex items-center space-x-1">
                            <span className={channel.isHidden ? 'text-gray-500' : ''}>{channel.name}</span>
                            {channel.isFavorite && <StarIconSolid className="w-3 h-3 text-yellow-400" />}
                            {channel.isHidden && <EyeSlashIcon className="w-3 h-3 text-gray-500" />}
                          </div>
                          {channel.channelNumber && (
                            <div className="text-xs text-gray-500">Ch. {channel.channelNumber}</div>
                          )}
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <span className="text-sm text-gray-400">{channel.detectedNetwork || channel.group || '-'}</span>
                    </td>
                    <td className="px-4 py-3 text-center">
                      <span className={`px-2 py-0.5 text-xs rounded ${getQualityColor(channel.detectedQuality)}`}>
                        {channel.detectedQuality || 'HD'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-center">
                      <span className={`px-2 py-0.5 text-xs rounded ${getStatusColor(channel.status)}`}>
                        {channel.status}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-center">
                      {channel.tvgId ? (
                        <button
                          onClick={() => {
                            if (window.confirm(`Clear the EPG mapping for "${channel.name}"? You can re-run auto-map or wait for it to pick a new match afterward.`)) {
                              handleUnmapEpg(channel);
                            }
                          }}
                          className="px-2 py-0.5 text-xs rounded bg-green-900/30 text-green-400 hover:bg-red-900/40 hover:text-red-400 transition-colors"
                          title={`EPG ID: ${channel.tvgId} - click to unmap`}
                        >
                          Mapped
                        </button>
                      ) : (
                        <button
                          onClick={() => openEpgPicker(channel)}
                          className="px-2 py-0.5 text-xs rounded bg-gray-800 text-gray-400 hover:bg-blue-900/40 hover:text-blue-300 transition-colors"
                          title="Manually pick an EPG channel for this IPTV channel"
                        >
                          Map...
                        </button>
                      )}
                    </td>
                    <td className="px-4 py-3 text-center">
                      <button
                        onClick={() => handleToggleSports(channel)}
                        className={`px-2 py-0.5 text-xs rounded transition-colors ${
                          channel.isSportsChannel
                            ? 'bg-green-900/30 text-green-400 hover:bg-green-900/50'
                            : 'bg-gray-800 text-gray-500 hover:bg-gray-700'
                        }`}
                      >
                        {channel.isSportsChannel ? 'Yes' : 'No'}
                      </button>
                    </td>
                    <td className="px-4 py-3 text-center">
                      <button
                        onClick={() => openMappingModal(channel)}
                        className="flex items-center justify-center space-x-1 px-2 py-0.5 text-xs rounded bg-gray-800 text-gray-400 hover:bg-gray-700 hover:text-white transition-colors mx-auto"
                      >
                        <LinkIcon className="w-3 h-3" />
                        <span>{channel.mappedLeagueIds?.length || 0}</span>
                      </button>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-center space-x-1">
                        <button
                          onClick={() => handleToggleFavorite(channel)}
                          className="p-1.5 hover:bg-gray-800 rounded transition-colors"
                          title={channel.isFavorite ? 'Remove from Favorites' : 'Add to Favorites'}
                        >
                          {channel.isFavorite ? (
                            <StarIconSolid className="w-4 h-4 text-yellow-400" />
                          ) : (
                            <StarIconOutline className="w-4 h-4 text-gray-400 hover:text-yellow-400" />
                          )}
                        </button>
                        <button
                          onClick={() => handleToggleHidden(channel)}
                          className="p-1.5 text-gray-400 hover:bg-gray-800 rounded transition-colors"
                          title={channel.isHidden ? 'Show Channel' : 'Hide Channel'}
                        >
                          {channel.isHidden ? (
                            <EyeSlashIcon className="w-4 h-4 text-gray-500" />
                          ) : (
                            <EyeIcon className="w-4 h-4 hover:text-gray-300" />
                          )}
                        </button>
                        <button
                          onClick={() => handleTestChannel(channel.id)}
                          disabled={testingChannelIds.has(channel.id)}
                          className={`p-1.5 text-gray-400 hover:text-green-400 hover:bg-gray-800 rounded transition-colors ${
                            testingChannelIds.has(channel.id) ? 'animate-pulse' : ''
                          }`}
                          title="Test Connection"
                        >
                          <BoltIcon className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => setPlayerChannel(channel)}
                          className="p-1.5 text-gray-400 hover:text-blue-400 hover:bg-gray-800 rounded transition-colors"
                          title="Play Stream"
                        >
                          <PlayIcon className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleToggleChannel(channel)}
                          className="p-1.5 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                          title={channel.isEnabled ? 'Disable' : 'Enable'}
                        >
                          {channel.isEnabled ? (
                            <CheckCircleIcon className="w-4 h-4 text-green-400" />
                          ) : (
                            <XCircleIcon className="w-4 h-4" />
                          )}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          )}

          {isLoading && (
            <div className="text-center py-12">
              <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
              <p className="text-gray-500">Loading channels...</p>
            </div>
          )}

          {!isLoading && filteredChannels.length === 0 && (
            <div className="text-center py-12">
              <SignalIcon className="w-12 h-12 text-gray-700 mx-auto mb-4" />
              <p className="text-gray-500">No channels found</p>
              <p className="text-sm text-gray-600 mt-1">
                {channels.length > 0
                  ? 'Try adjusting your filters'
                  : 'Add IPTV sources in the IPTV Sources settings page'}
              </p>
            </div>
          )}

          {/* Channel count and Load More */}
          <div className="px-4 py-3 bg-black/30 border-t border-gray-800 flex items-center justify-between">
            <span className="text-sm text-gray-500">
              Showing {filteredChannels.length} of {channels.length} channels loaded
            </span>
            {hasMore && !isLoading && (
              <button
                onClick={() => loadChannels(currentPage + 1, false)}
                className="px-4 py-1.5 bg-red-900/30 hover:bg-red-900/50 text-red-400 rounded text-sm transition-colors"
              >
                Load More
              </button>
            )}
            {isLoading && currentPage > 0 && (
              <span className="text-sm text-gray-400">Loading more...</span>
            )}
          </div>
        </div>

        {/* Stream Player Modal */}
        <StreamPlayerModal
          isOpen={!!playerChannel}
          onClose={() => setPlayerChannel(null)}
          streamUrl={playerChannel?.streamUrl || null}
          channelId={playerChannel?.id}
          channelName={playerChannel?.name || ''}
        />

        {/* Manual EPG Picker Modal */}
        {epgPickerChannel && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full my-8">
              <div className="flex items-center justify-between mb-4">
                <div>
                  <h3 className="text-2xl font-bold text-white">Map EPG Channel</h3>
                  <p className="text-gray-400 mt-1">{epgPickerChannel.name}</p>
                </div>
                <button
                  onClick={() => setEpgPickerChannel(null)}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>
              <input
                type="text"
                value={epgPickerSearch}
                onChange={(e) => setEpgPickerSearch(e.target.value)}
                placeholder="Search EPG channels by name or ID..."
                autoFocus
                className="w-full px-4 py-2 mb-4 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
              <div className="max-h-96 overflow-y-auto space-y-1">
                {isEpgPickerSearching && (
                  <p className="text-sm text-gray-500 py-2">Searching...</p>
                )}
                {!isEpgPickerSearching && epgPickerResults.length === 0 && (
                  <p className="text-sm text-gray-500 py-2">
                    No EPG channels found. Sync an EPG source from the TV Guide page first, or broaden the search.
                  </p>
                )}
                {!isEpgPickerSearching && epgPickerResults.map((epg) => (
                  <button
                    key={epg.id}
                    onClick={() => handleMapEpg(epg.channelId, epg.displayName)}
                    className="w-full flex items-center gap-3 px-3 py-2 rounded-lg bg-gray-800/60 hover:bg-blue-900/40 text-left transition-colors"
                  >
                    {epg.iconUrl ? (
                      <img src={epg.iconUrl} alt="" className="w-8 h-8 rounded object-contain bg-gray-900" />
                    ) : (
                      <div className="w-8 h-8 rounded bg-gray-900" />
                    )}
                    <div className="min-w-0">
                      <p className="text-white text-sm truncate">{epg.displayName}</p>
                      <p className="text-gray-500 text-xs truncate">{epg.channelId}</p>
                    </div>
                  </button>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* League Mapping Modal */}
        {mappingChannel && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full my-8">
              <div className="flex items-center justify-between mb-6">
                <div>
                  <h3 className="text-2xl font-bold text-white">Map Channel to Leagues</h3>
                  <p className="text-gray-400 mt-1">{mappingChannel.name}</p>
                </div>
                <button
                  onClick={() => setMappingChannel(null)}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <div className="mb-6">
                <p className="text-sm text-gray-400 mb-2">
                  Select the leagues that this channel broadcasts. When events are scheduled for these leagues,
                  Sportarr can automatically record them from this channel.
                </p>
                <p className="text-xs text-gray-500 mb-4">
                  💡 For per-mapping actions (lock to prevent auto-mapper changes, see why a mapping was made, run a test resolve), visit the <span className="text-purple-300">IPTV → Coverage</span> page.
                </p>

                {/* League Selection */}
                <div className="max-h-80 overflow-y-auto space-y-2">
                  {leagues.map((league) => {
                    const isSelected = selectedLeagues.includes(league.id);
                    const isPreferred = preferredLeagueId === league.id;
                    // Surface the mapping's scored confidence + manual-lock
                    // state alongside each row so users see at a glance
                    // whether an auto-mapped league is high or low quality.
                    // Drill into the Coverage page for per-mapping actions
                    // (lock / unmap / explain) — this modal is intentionally
                    // the simple bulk-select path.
                    const existingMapping = channelMappings.find((m) => m.leagueId === league.id);

                    return (
                      <div
                        key={league.id}
                        className={`flex items-center justify-between p-3 rounded-lg border transition-colors cursor-pointer ${
                          isSelected
                            ? 'bg-red-950/30 border-red-900/50'
                            : 'bg-black/30 border-gray-800 hover:border-gray-700'
                        }`}
                        onClick={() => toggleLeagueSelection(league.id)}
                      >
                        <div className="flex items-center space-x-3 min-w-0">
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleLeagueSelection(league.id)}
                            className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                            onClick={(e) => e.stopPropagation()}
                          />
                          {league.logoUrl ? (
                            <img
                              src={league.logoUrl}
                              alt={league.name}
                              className="w-8 h-8 rounded object-contain bg-gray-800"
                            />
                          ) : (
                            <div className="w-8 h-8 rounded bg-gray-800"></div>
                          )}
                          <div className="min-w-0">
                            <div className="font-medium text-white truncate">{league.name}</div>
                            <div className="text-xs text-gray-500 flex items-center gap-1.5">
                              <span>{league.sport}</span>
                              {existingMapping && existingMapping.confidence !== undefined && (
                                <span
                                  className={`px-1.5 py-0.5 rounded text-[10px] ${
                                    existingMapping.confidence >= 85 ? 'bg-green-900/40 text-green-300' :
                                    existingMapping.confidence >= 65 ? 'bg-amber-900/40 text-amber-300' :
                                    'bg-red-900/40 text-red-300'
                                  }`}
                                  title="Auto-mapper confidence (0-100). High = strong signals; low = weak signals, consider locking manually."
                                >
                                  conf {existingMapping.confidence}
                                </span>
                              )}
                              {existingMapping?.isManual && (
                                <span
                                  className="px-1.5 py-0.5 rounded text-[10px] bg-blue-900/40 text-blue-300"
                                  title="Manual lock — auto-mapper won't touch this mapping on future re-runs."
                                >
                                  🔒 locked
                                </span>
                              )}
                            </div>
                          </div>
                        </div>
                        {isSelected && (
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              setPreferredLeagueId(isPreferred ? null : league.id);
                            }}
                            className={`px-2 py-1 text-xs rounded transition-colors flex-shrink-0 ${
                              isPreferred
                                ? 'bg-yellow-900/30 text-yellow-400'
                                : 'bg-gray-800 text-gray-500 hover:bg-gray-700'
                            }`}
                          >
                            {isPreferred ? 'Preferred' : 'Set Preferred'}
                          </button>
                        )}
                      </div>
                    );
                  })}
                </div>

                {leagues.length === 0 && (
                  <div className="text-center py-8 text-gray-500">
                    <p>No leagues available</p>
                    <p className="text-sm mt-1">Add leagues to your library first</p>
                  </div>
                )}
              </div>

              {/* Per-team preference */}
              <div className="mb-6 pt-4 border-t border-gray-800">
                <p className="text-sm font-medium text-white mb-1">Preferred for Teams</p>
                <p className="text-xs text-gray-500 mb-3">
                  A team mapped here records from this channel even when its league prefers a different one,
                  e.g. a regional channel for one team's games. Applied immediately; each team can prefer one channel.
                </p>
                {channelTeamMappings.length > 0 && (
                  <div className="flex flex-wrap gap-2 mb-3">
                    {channelTeamMappings.map((m) => (
                      <span key={m.id} className="flex items-center gap-1 px-2 py-1 rounded bg-yellow-900/30 text-yellow-300 text-xs">
                        {m.teamName || `Team #${m.teamId}`}
                        <button
                          onClick={() => handleRemoveTeamMapping(m.teamId)}
                          className="hover:text-red-400"
                          title="Remove team preference"
                        >
                          <XMarkIcon className="w-3.5 h-3.5" />
                        </button>
                      </span>
                    ))}
                  </div>
                )}
                <div className="flex items-center gap-2">
                  <select
                    value={teamPickerLeagueId}
                    onChange={(e) => setTeamPickerLeagueId(e.target.value === '' ? '' : Number(e.target.value))}
                    className="flex-1 px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                  >
                    <option value="">Select league...</option>
                    {leagues.map((l) => (
                      <option key={l.id} value={l.id}>{l.name}</option>
                    ))}
                  </select>
                  <select
                    value={teamPickerTeamId}
                    onChange={(e) => setTeamPickerTeamId(e.target.value === '' ? '' : Number(e.target.value))}
                    disabled={teamPickerLeagueId === ''}
                    className="flex-1 px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600 disabled:opacity-50"
                  >
                    <option value="">Select team...</option>
                    {teamPickerTeams.map((t) => (
                      <option key={t.id} value={t.id}>{t.name}</option>
                    ))}
                  </select>
                  <button
                    onClick={handleAddTeamMapping}
                    disabled={teamPickerTeamId === ''}
                    className="px-3 py-2 bg-yellow-700 hover:bg-yellow-600 text-white text-sm rounded-lg transition-colors disabled:opacity-50"
                  >
                    Add
                  </button>
                </div>
              </div>

              <div className="pt-6 border-t border-gray-800 flex items-center justify-between">
                <span className="text-sm text-gray-400">
                  {selectedLeagues.length} league(s) selected
                </span>
                <div className="flex items-center space-x-3">
                  <button
                    onClick={() => setMappingChannel(null)}
                    className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={handleSaveMappings}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Save Mappings
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
      </>
      )}
    </PageShell>
  );
}
