import { Fragment, useState, useEffect, useMemo } from 'react';
import { toast } from 'sonner';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  MagnifyingGlassIcon,
  ArrowDownTrayIcon,
  ExclamationTriangleIcon,
  NoSymbolIcon,
  ChevronUpIcon,
  ChevronDownIcon,
  FunnelIcon,
  CheckCircleIcon,
  XCircleIcon,
  FolderIcon,
  InformationCircleIcon,
  CloudArrowDownIcon,
  TrashIcon,
} from '@heroicons/react/24/outline';
import { apiPost, apiGet, apiDelete } from '../utils/api';

interface SeasonSearchModalProps {
  isOpen: boolean;
  onClose: () => void;
  leagueId: number;
  leagueName: string;
  season: string;
  qualityProfileId?: number;
}

interface MatchedFormat {
  name: string;
  score: number;
}

interface SeasonEventMatch {
  eventId: number;
  eventTitle: string;
  eventDate: string;
  episodeNumber?: number;
  confidence: number;
  matchReasons: string[];
  detectedPart?: string;
  hasFile: boolean;
  monitored: boolean;
}

interface SeasonSearchRelease {
  title: string;
  guid: string;
  downloadUrl: string;
  infoUrl?: string;
  indexer: string;
  indexerFlags?: string;
  protocol: string;
  size: number;
  quality?: string;
  source?: string;
  codec?: string;
  language?: string;
  seeders?: number;
  leechers?: number;
  publishDate: string;
  score: number;
  qualityScore: number;
  customFormatScore?: number;
  matchedFormats: MatchedFormat[];
  approved: boolean;
  rejections: string[];
  torrentInfoHash?: string;
  isSeasonPack: boolean;
  matchedEventCount: number;
  bestConfidence: number;
  detectedPart?: string;
  matchedEvents: SeasonEventMatch[];
  isBlocklisted?: boolean;
  blocklistReason?: string;
}

interface SeasonSearchResults {
  leagueId: number;
  leagueName: string;
  season: string;
  eventCount: number;
  monitoredEventCount: number;
  downloadedEventCount: number;
  releases: SeasonSearchRelease[];
  events: Array<{
    id: number;
    title: string;
    eventDate: string;
    episodeNumber?: number;
    monitored: boolean;
    hasFile: boolean;
  }>;
}

interface HistoryItem {
  id: number;
  type: 'import' | 'grabbed' | 'completed' | 'failed' | 'warning' | 'blocklist' | 'deleted';
  sourcePath: string;
  destinationPath?: string;
  quality?: string;
  size?: number;
  decision: string;
  warnings: string[];
  errors: string[];
  date: string;
  indexer?: string;
  torrentHash?: string;
  part?: string;
  eventTitle?: string;
}

type SortDirection = 'asc' | 'desc';
type SortField = 'score' | 'quality' | 'source' | 'age' | 'title' | 'indexer' | 'size' | 'peers' | 'language' | 'events' | 'warnings';
type TabType = 'search' | 'history';

export default function SeasonSearchModal({
  isOpen,
  onClose,
  leagueId,
  leagueName,
  season,
  qualityProfileId,
}: SeasonSearchModalProps) {
  const [activeTab, setActiveTab] = useState<TabType>('search');
  const [isSearching, setIsSearching] = useState(false);
  const [searchResults, setSearchResults] = useState<SeasonSearchResults | null>(null);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [downloadingIndex, setDownloadingIndex] = useState<number | null>(null);
  const [sortField, setSortField] = useState<SortField>('events');
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');
  const [showFilters, setShowFilters] = useState(false);
  const [filterSeasonPacksOnly, setFilterSeasonPacksOnly] = useState(false);
  const [expandedReleases, setExpandedReleases] = useState<Set<string>>(new Set());
  const [blocklistConfirm, setBlocklistConfirm] = useState<{ index: number; result: SeasonSearchRelease } | null>(null);

  // History state
  const [history, setHistory] = useState<HistoryItem[]>([]);
  const [isLoadingHistory, setIsLoadingHistory] = useState(false);
  const [markFailedConfirm, setMarkFailedConfirm] = useState<HistoryItem | null>(null);

  // Clear search results when modal opens for a different season
  useEffect(() => {
    if (isOpen) {
      setSearchResults(null);
      setSearchError(null);
      setDownloadingIndex(null);
      setExpandedReleases(new Set());
      setActiveTab('search');
      setBlocklistConfirm(null);
      loadHistory();
    }
  }, [isOpen, leagueId, season]);

  const loadHistory = async () => {
    setIsLoadingHistory(true);
    try {
      const response = await apiGet(`/api/leagues/${leagueId}/seasons/${encodeURIComponent(season)}/history`);
      if (response.ok) {
        const data = await response.json();
        setHistory(data);
      }
    } catch (error) {
      console.error('Failed to load history:', error);
    } finally {
      setIsLoadingHistory(false);
    }
  };

  const formatFileSize = (bytes?: number) => {
    if (!bytes) return 'N/A';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(2)} GiB`;
    const mb = bytes / (1024 * 1024);
    return `${mb.toFixed(1)} MiB`;
  };

  const formatAge = (publishDate: string) => {
    const now = new Date();
    const published = new Date(publishDate);
    const diffMs = now.getTime() - published.getTime();
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return 'Today';
    if (diffDays === 1) return '1 day';
    return `${diffDays} days`;
  };

  const formatDateTime = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      hour12: true
    });
  };

  const handleSearch = async () => {
    setIsSearching(true);
    setSearchError(null);
    setSearchResults(null);

    try {
      const endpoint = `/api/leagues/${leagueId}/seasons/${encodeURIComponent(season)}/search`;
      const response = await apiPost(endpoint, { qualityProfileId });
      const results = await response.json();
      setSearchResults(results);
    } catch (error) {
      console.error('Season search failed:', error);
      setSearchError('Failed to search indexers. Please try again.');
    } finally {
      setIsSearching(false);
    }
  };

  const handleDownloadClick = (release: SeasonSearchRelease, index: number) => {
    if (release.isBlocklisted) {
      setBlocklistConfirm({ index, result: release });
      return;
    }
    handleDownload(release, index);
  };

  const handleDownload = async (release: SeasonSearchRelease, index: number) => {
    setBlocklistConfirm(null);
    setDownloadingIndex(index);
    setSearchError(null);

    try {
      // For season packs, we need to grab the release and associate it with matching events
      // The backend will handle mapping the release to specific events during import
      const response = await apiPost('/api/release/grab', {
        title: release.title,
        guid: release.guid,
        downloadUrl: release.downloadUrl,
        indexer: release.indexer,
        protocol: release.protocol,
        size: release.size,
        quality: release.quality,
        source: release.source,
        codec: release.codec,
        language: release.language,
        seeders: release.seeders,
        leechers: release.leechers,
        publishDate: release.publishDate,
        score: release.score,
        qualityScore: release.qualityScore,
        torrentInfoHash: release.torrentInfoHash,
        // Use the first matched event as the primary event
        eventId: release.matchedEvents[0]?.eventId,
        // Include all matched event IDs for season pack handling
        matchedEventIds: release.matchedEvents.map(e => e.eventId),
        isSeasonPack: release.isSeasonPack,
        overrideBlocklist: release.isBlocklisted,
      });

      if (response.ok) {
        toast.success('Release grabbed', {
          description: release.isSeasonPack
            ? `Season pack queued for download (${release.matchedEventCount} events)`
            : `Release queued for download`
        });
        onClose();
      } else {
        const data = await response.json();
        throw new Error(data.message || 'Failed to grab release');
      }
    } catch (error) {
      console.error('Download failed:', error);
      setSearchError(error instanceof Error ? error.message : 'Failed to grab release');
    } finally {
      setDownloadingIndex(null);
    }
  };

  const toggleReleaseExpanded = (guid: string) => {
    setExpandedReleases(prev => {
      const next = new Set(prev);
      if (next.has(guid)) {
        next.delete(guid);
      } else {
        next.add(guid);
      }
      return next;
    });
  };

  // Mark as failed - adds to blocklist and optionally searches for replacement
  const handleMarkAsFailed = async (item: HistoryItem, searchForReplacement: boolean) => {
    try {
      const action = searchForReplacement ? 'blocklistAndSearch' : 'blocklistOnly';
      const response = await apiDelete(`/api/history/${item.id}?blocklistAction=${action}`);

      if (response.ok) {
        toast.success('Marked as Failed', {
          description: searchForReplacement
            ? 'Release blocklisted and searching for replacement...'
            : 'Release added to blocklist.',
        });
        loadHistory();
      } else {
        toast.error('Failed', { description: 'Could not mark release as failed.' });
      }
    } catch (error) {
      console.error('Mark as failed error:', error);
      toast.error('Error', { description: 'Failed to mark release as failed.' });
    } finally {
      setMarkFailedConfirm(null);
    }
  };

  // Get resolution rank for sorting
  const getResolutionRank = (quality: string | null | undefined): number => {
    if (!quality) return 0;
    const q = quality.toLowerCase();
    if (q.includes('2160p') || q.includes('4k')) return 4;
    if (q.includes('1080p')) return 3;
    if (q.includes('720p')) return 2;
    if (q.includes('480p')) return 1;
    return 0;
  };

  // Get source rank for sorting
  const getSourceRank = (source: string | null | undefined): number => {
    if (!source) return 0;
    const s = source.toLowerCase().replace(/-/g, '').replace(/ /g, '');
    if (s.includes('remux')) return 7;
    if (s.includes('bluray') || s.includes('bray')) return 6;
    if (s.includes('webdl')) return 5;
    if (s.includes('webrip')) return 4;
    if (s.includes('web')) return 3;
    if (s.includes('hdtv')) return 2;
    if (s.includes('dvd')) return 1;
    return 0;
  };

  // Parse age from publishDate for sorting
  const getAgeInDays = (publishDate: string | null | undefined): number => {
    if (!publishDate) return Infinity;
    const date = new Date(publishDate);
    const now = new Date();
    return Math.floor((now.getTime() - date.getTime()) / (1000 * 60 * 60 * 24));
  };

  // Get warning count for a release
  const getWarningCount = (release: SeasonSearchRelease): number => {
    let count = release.rejections?.length ?? 0;
    if (release.isBlocklisted) count++;
    return count;
  };

  // Filter out "Not X" language formats
  const getFilteredFormats = (formats: MatchedFormat[] | undefined) => {
    if (!formats) return [];
    return formats.filter(f => {
      const nameLower = f.name.toLowerCase();
      return !nameLower.startsWith('not ') && !nameLower.startsWith('not-');
    });
  };

  const getAllRejections = (result: SeasonSearchRelease): string[] => {
    const rejections = [...(result.rejections || [])];
    const cfScore = result.customFormatScore ?? 0;
    if (cfScore < 0) {
      const negativeFormats = result.matchedFormats
        ?.filter(f => f.score < 0 && !f.name.toLowerCase().startsWith('not '))
        .map(f => f.name)
        .join(', ');
      if (negativeFormats) {
        rejections.push(`Custom Formats ${negativeFormats} have score ${cfScore} below minimum`);
      }
    }
    return rejections;
  };

  const sortedResults = useMemo(() => {
    if (!searchResults?.releases) return [];

    let releases = [...searchResults.releases];

    // Apply filter
    if (filterSeasonPacksOnly) {
      releases = releases.filter(r => r.isSeasonPack);
    }

    return releases.sort((a, b) => {
      let comparison = 0;

      switch (sortField) {
        case 'events':
          comparison = a.matchedEventCount - b.matchedEventCount;
          break;
        case 'score': {
          const cfScoreA = a.customFormatScore ?? 0;
          const cfScoreB = b.customFormatScore ?? 0;
          comparison = cfScoreA - cfScoreB;
          break;
        }
        case 'quality': {
          const qualScoreA = a.qualityScore ?? getResolutionRank(a.quality);
          const qualScoreB = b.qualityScore ?? getResolutionRank(b.quality);
          if (qualScoreA !== qualScoreB) {
            comparison = qualScoreA - qualScoreB;
          } else {
            comparison = getSourceRank(a.source) - getSourceRank(b.source);
          }
          break;
        }
        case 'source':
          comparison = getSourceRank(a.source) - getSourceRank(b.source);
          break;
        case 'age':
          comparison = getAgeInDays(a.publishDate) - getAgeInDays(b.publishDate);
          break;
        case 'title':
          comparison = (a.title || '').localeCompare(b.title || '');
          break;
        case 'indexer':
          comparison = (a.indexer || '').localeCompare(b.indexer || '');
          break;
        case 'size':
          comparison = (a.size ?? 0) - (b.size ?? 0);
          break;
        case 'peers':
          const peersA = (a.seeders ?? 0) + (a.leechers ?? 0);
          const peersB = (b.seeders ?? 0) + (b.leechers ?? 0);
          comparison = peersA - peersB;
          break;
        case 'language':
          comparison = (a.language || 'English').localeCompare(b.language || 'English');
          break;
        case 'warnings':
          comparison = getWarningCount(a) - getWarningCount(b);
          break;
      }

      return sortDirection === 'desc' ? -comparison : comparison;
    });
  }, [searchResults, sortField, sortDirection, filterSeasonPacksOnly]);

  const handleSort = (field: SortField) => {
    if (sortField === field) {
      setSortDirection(prev => prev === 'desc' ? 'asc' : 'desc');
    } else {
      setSortField(field);
      setSortDirection('desc');
    }
  };

  const getProtocol = (release: SeasonSearchRelease): 'torrent' | 'usenet' => {
    if (release.protocol) {
      const proto = release.protocol.toLowerCase();
      if (proto === 'torrent' || proto.includes('torrent')) return 'torrent';
      if (proto === 'usenet' || proto.includes('usenet') || proto === 'nzb') return 'usenet';
    }
    if (release.seeders !== null || release.leechers !== null) return 'torrent';
    if (release.indexer?.toLowerCase().includes('nzb')) return 'usenet';
    return 'usenet';
  };

  // Get icon for history item type (matching Sonarr's conventions)
  const getHistoryIcon = (type: string) => {
    switch (type) {
      case 'grabbed':
        return <CloudArrowDownIcon className="w-4 h-4 text-blue-400" title="Grabbed" />;
      case 'import':
      case 'completed':
        return <ArrowDownTrayIcon className="w-4 h-4 text-green-400" title="Imported" />;
      case 'failed':
        return <XMarkIcon className="w-4 h-4 text-red-400" title="Failed" />;
      case 'warning':
        return <ExclamationTriangleIcon className="w-4 h-4 text-yellow-400" title="Warning" />;
      case 'blocklist':
        return <NoSymbolIcon className="w-4 h-4 text-orange-400" title="Blocklisted" />;
      case 'deleted':
        return <TrashIcon className="w-4 h-4 text-gray-400" title="Deleted" />;
      default:
        return <InformationCircleIcon className="w-4 h-4 text-gray-400" />;
    }
  };

  return (
    <Transition
      appear
      show={isOpen}
      as={Fragment}
      unmount={true}
      afterLeave={() => {
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
          <div className="flex min-h-full items-center justify-center p-2">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-[98vw] max-w-none mx-2 md:mx-4 transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header with Tabs */}
                <div className="relative bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900 border-b border-red-900/30">
                  <div className="px-3 md:px-6 py-3 md:py-4 flex items-center justify-between">
                    <div className="min-w-0 flex-1 mr-2">
                      <h2 className="text-base md:text-xl font-bold text-white truncate">
                        {leagueName} - Season {season}
                      </h2>
                      {searchResults && (
                        <p className="text-xs md:text-sm text-gray-400 mt-1">
                          {searchResults.eventCount} events
                          ({searchResults.monitoredEventCount} monitored, {searchResults.downloadedEventCount} downloaded)
                        </p>
                      )}
                    </div>
                    <button
                      onClick={onClose}
                      className="p-1.5 md:p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors flex-shrink-0"
                    >
                      <XMarkIcon className="w-5 h-5 md:w-6 md:h-6 text-white" />
                    </button>
                  </div>

                  {/* Tabs */}
                  <div className="px-3 md:px-6 flex gap-1">
                    <button
                      onClick={() => setActiveTab('search')}
                      className={`px-3 md:px-4 py-1.5 md:py-2 text-xs md:text-sm font-medium rounded-t-lg transition-colors ${
                        activeTab === 'search'
                          ? 'bg-gray-800 text-white border-t border-l border-r border-gray-700'
                          : 'text-gray-400 hover:text-white hover:bg-gray-800/50'
                      }`}
                    >
                      Search
                    </button>
                    <button
                      onClick={() => setActiveTab('history')}
                      className={`px-3 md:px-4 py-1.5 md:py-2 text-xs md:text-sm font-medium rounded-t-lg transition-colors ${
                        activeTab === 'history'
                          ? 'bg-gray-800 text-white border-t border-l border-r border-gray-700'
                          : 'text-gray-400 hover:text-white hover:bg-gray-800/50'
                      }`}
                    >
                      History
                    </button>
                  </div>
                </div>

                {/* Search Tab Content */}
                {activeTab === 'search' && (
                  <>
                    {/* Search Controls */}
                    <div className="px-3 md:px-6 py-2 md:py-3 border-b border-gray-800 flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                      <p className="text-gray-400 text-xs md:text-sm hidden sm:block">
                        Search for season packs and releases matching events in this season
                      </p>
                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => setShowFilters(!showFilters)}
                          className={`px-2 md:px-3 py-1 md:py-1.5 ${showFilters ? 'bg-red-600' : 'bg-gray-800'} hover:bg-gray-700 text-gray-300 rounded transition-colors flex items-center gap-1 md:gap-1.5 text-xs md:text-sm`}
                        >
                          <FunnelIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                          Filter
                        </button>
                        <button
                          onClick={handleSearch}
                          disabled={isSearching}
                          className="px-3 md:px-4 py-1 md:py-1.5 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded transition-colors flex items-center gap-1.5 md:gap-2 text-xs md:text-sm"
                        >
                          {isSearching ? (
                            <>
                              <div className="animate-spin rounded-full h-3.5 w-3.5 md:h-4 md:w-4 border-b-2 border-white"></div>
                              <span className="hidden sm:inline">Searching...</span>
                            </>
                          ) : (
                            <>
                              <MagnifyingGlassIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                              <span className="hidden sm:inline">Search Indexers</span>
                              <span className="sm:hidden">Search</span>
                            </>
                          )}
                        </button>
                      </div>
                    </div>

                    {/* Filter Panel */}
                    {showFilters && (
                      <div className="px-3 md:px-6 py-2 border-b border-gray-800 bg-gray-900/50">
                        <label className="flex items-center gap-2 text-xs md:text-sm text-gray-300">
                          <input
                            type="checkbox"
                            checked={filterSeasonPacksOnly}
                            onChange={(e) => setFilterSeasonPacksOnly(e.target.checked)}
                            className="rounded bg-gray-700 border-gray-600 text-red-600 focus:ring-red-600"
                          />
                          Show season packs only (releases matching multiple events)
                        </label>
                      </div>
                    )}

                    {/* Error Message */}
                    {searchError && (
                      <div className="mx-3 md:mx-6 mt-3 bg-red-900/20 border border-red-600/50 rounded-lg p-3">
                        <p className="text-red-400 text-sm">{searchError}</p>
                      </div>
                    )}

                    {/* Results Count */}
                    {searchResults && searchResults.releases.length > 0 && (
                      <div className="px-3 md:px-6 py-2 text-gray-400 text-sm flex items-center gap-4">
                        <span>Found {searchResults.releases.length} releases</span>
                        {filterSeasonPacksOnly && sortedResults.length !== searchResults.releases.length && (
                          <span className="text-yellow-500">
                            (showing {sortedResults.length} season packs)
                          </span>
                        )}
                      </div>
                    )}

                    {/* Content - Table Layout matching ManualSearchModal */}
                    <div className="max-h-[65vh] overflow-y-auto">
                      {isSearching ? (
                        <div className="p-8 text-center">
                          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                          <p className="text-gray-400">Searching indexers for season releases...</p>
                        </div>
                      ) : sortedResults.length > 0 ? (
                        <table className="w-full text-xs">
                          <thead className="bg-gray-900/80 sticky top-0 z-10">
                            <tr className="border-b border-gray-800">
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium w-[52px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('source')}
                                title="Sort by source type"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Source</span>
                                  {sortField === 'source' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium w-[60px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('events')}
                                title="Sort by matched events"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Events</span>
                                  {sortField === 'events' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium w-[60px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('age')}
                                title="Sort by age"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Age</span>
                                  {sortField === 'age' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium min-w-[150px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('title')}
                                title="Sort by title"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Title</span>
                                  {sortField === 'title' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium w-[100px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('indexer')}
                                title="Sort by indexer"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Indexer</span>
                                  {sortField === 'indexer' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium w-[60px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('size')}
                                title="Sort by size"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Size</span>
                                  {sortField === 'size' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium w-[70px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('peers')}
                                title="Sort by peers"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Peers</span>
                                  {sortField === 'peers' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 pl-4 pr-2 text-gray-400 font-medium w-[80px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('language')}
                                title="Sort by language"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Language</span>
                                  {sortField === 'language' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-left py-1.5 px-2 text-gray-400 font-medium w-[120px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('quality')}
                                title="Sort by quality"
                              >
                                <div className="flex items-center gap-0.5">
                                  <span>Quality</span>
                                  {sortField === 'quality' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-center py-1.5 px-2 text-gray-400 font-medium w-[50px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('score')}
                                title="Sort by Custom Format score"
                              >
                                <div className="flex items-center justify-center gap-0.5">
                                  <span>CF</span>
                                  {sortField === 'score' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                                </div>
                              </th>
                              <th
                                className="text-center py-1.5 px-2 text-gray-400 font-medium w-[24px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={() => handleSort('warnings')}
                                title="Sort by warnings"
                              >
                                {sortField === 'warnings' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                              </th>
                              <th className="text-right py-1.5 px-2 text-gray-400 font-medium w-[70px]">Actions</th>
                            </tr>
                          </thead>
                          <tbody>
                            {sortedResults.map((result, index) => {
                              const protocol = getProtocol(result);
                              const isExpanded = expandedReleases.has(result.guid);
                              const rejections = getAllRejections(result);
                              const hasWarnings = rejections.length > 0 || result.isBlocklisted;
                              const cfScore = result.customFormatScore ?? 0;

                              return (
                                <Fragment key={result.guid}>
                                  <tr
                                    className={`border-b border-gray-800/50 hover:bg-gray-800/30 transition-colors ${
                                      result.isBlocklisted ? 'bg-orange-900/10' : ''
                                    }`}
                                  >
                                    {/* Source Column */}
                                    <td className="py-1 px-2">
                                      <span className={`px-1 py-0.5 text-[10px] font-semibold rounded ${
                                        protocol === 'torrent'
                                          ? 'bg-green-900/50 text-green-400'
                                          : 'bg-blue-900/50 text-blue-400'
                                      }`}>
                                        {protocol === 'torrent' ? 'torrent' : 'nzb'}
                                      </span>
                                    </td>

                                    {/* Events Column */}
                                    <td className="py-1 px-2">
                                      <button
                                        onClick={() => toggleReleaseExpanded(result.guid)}
                                        className="flex items-center gap-1 text-white hover:text-red-400 transition-colors"
                                        title={`Click to see ${result.matchedEventCount} matched events`}
                                      >
                                        {result.isSeasonPack ? (
                                          <FolderIcon className="w-3.5 h-3.5 text-yellow-500" />
                                        ) : null}
                                        <span className={result.isSeasonPack ? 'text-yellow-400 font-medium' : ''}>
                                          {result.matchedEventCount}
                                        </span>
                                        {isExpanded ? (
                                          <ChevronUpIcon className="w-3 h-3" />
                                        ) : (
                                          <ChevronDownIcon className="w-3 h-3" />
                                        )}
                                      </button>
                                    </td>

                                    {/* Age Column */}
                                    <td className="py-1 px-2 text-gray-400 whitespace-nowrap">
                                      {formatAge(result.publishDate)}
                                    </td>

                                    {/* Title Column */}
                                    <td className="py-1 px-2" style={{ maxWidth: '300px' }}>
                                      <div className="flex items-start gap-1">
                                        {result.isBlocklisted && (
                                          <NoSymbolIcon className="w-3 h-3 text-orange-400 flex-shrink-0 mt-0.5" />
                                        )}
                                        <div className="min-w-0">
                                          <span
                                            className={`truncate block ${result.isBlocklisted ? 'text-orange-300' : 'text-white'}`}
                                            title={result.title}
                                          >
                                            {result.title}
                                          </span>
                                          {result.isSeasonPack && (
                                            <span className="text-yellow-500 text-[10px]">Season Pack</span>
                                          )}
                                        </div>
                                      </div>
                                    </td>

                                    {/* Indexer Column */}
                                    <td className="py-1 px-2 overflow-hidden">
                                      <span className="text-gray-300 truncate block" title={result.indexer}>
                                        {result.indexer}
                                      </span>
                                    </td>

                                    {/* Size Column */}
                                    <td className="py-1 px-2 text-gray-400 whitespace-nowrap">
                                      {formatFileSize(result.size)}
                                    </td>

                                    {/* Peers Column */}
                                    <td className="py-1 px-2">
                                      {protocol === 'torrent' && result.seeders !== undefined ? (
                                        <span className="whitespace-nowrap">
                                          <span className="text-green-400">↑{result.seeders ?? 0}</span>
                                          <span className="text-red-400 ml-1">↓{result.leechers ?? 0}</span>
                                        </span>
                                      ) : (
                                        <span className="text-gray-600">-</span>
                                      )}
                                    </td>

                                    {/* Language Column */}
                                    <td className="py-1 pl-4 pr-2">
                                      <span className="px-1 py-0.5 bg-gray-700 text-gray-300 text-[10px] rounded whitespace-nowrap">
                                        {result.language || 'English'}
                                      </span>
                                    </td>

                                    {/* Quality Column */}
                                    <td className="py-1 px-2">
                                      <span className="px-1 py-0.5 bg-blue-900/50 text-blue-400 text-[10px] rounded inline-block w-fit whitespace-nowrap">
                                        {result.quality || 'Unknown'}
                                      </span>
                                    </td>

                                    {/* CF Score Column */}
                                    <td className="py-1 px-2 text-center">
                                      <div className="relative group">
                                        <span
                                          className={`font-bold text-xs cursor-help ${
                                            cfScore > 0 ? 'text-green-400' :
                                            cfScore < 0 ? 'text-red-400' :
                                            'text-gray-400'
                                          }`}
                                        >
                                          {cfScore > 0 ? '+' : ''}{cfScore}
                                        </span>
                                        {getFilteredFormats(result.matchedFormats).length > 0 && (
                                          <div className="absolute right-0 top-5 z-50 hidden group-hover:block p-1.5 bg-gray-900 border border-gray-700 rounded-lg shadow-xl">
                                            <div className="flex flex-wrap gap-0.5 max-w-[200px]">
                                              {getFilteredFormats(result.matchedFormats).map((format, fIdx) => (
                                                <span
                                                  key={fIdx}
                                                  className={`px-1 py-0.5 text-[9px] rounded whitespace-nowrap ${
                                                    format.score > 0
                                                      ? 'bg-green-900/50 text-green-400'
                                                      : format.score < 0
                                                      ? 'bg-red-900/50 text-red-400'
                                                      : 'bg-gray-700 text-gray-300'
                                                  }`}
                                                >
                                                  {format.name}
                                                </span>
                                              ))}
                                            </div>
                                          </div>
                                        )}
                                      </div>
                                    </td>

                                    {/* Warnings Column */}
                                    <td className="py-1 px-2 text-center">
                                      {hasWarnings ? (
                                        <div className="relative group">
                                          <ExclamationTriangleIcon
                                            className={`w-3.5 h-3.5 mx-auto cursor-help ${
                                              result.isBlocklisted ? 'text-orange-400' : 'text-red-400'
                                            }`}
                                          />
                                          <div className="absolute right-0 top-5 z-50 hidden group-hover:block w-64 p-2 bg-gray-900 border border-gray-700 rounded-lg shadow-xl text-left">
                                            {result.isBlocklisted && (
                                              <div className="mb-1.5">
                                                <p className="text-orange-400 text-[10px] font-semibold">Blocklisted</p>
                                                {result.blocklistReason && (
                                                  <p className="text-gray-400 text-[10px]">{result.blocklistReason}</p>
                                                )}
                                              </div>
                                            )}
                                            {rejections.length > 0 && (
                                              <div>
                                                <p className="text-red-400 text-[10px] font-semibold mb-0.5">Rejections:</p>
                                                {rejections.map((r, i) => (
                                                  <p key={i} className="text-gray-400 text-[10px]">• {r}</p>
                                                ))}
                                              </div>
                                            )}
                                          </div>
                                        </div>
                                      ) : (
                                        <span className="text-gray-700">-</span>
                                      )}
                                    </td>

                                    {/* Actions Column */}
                                    <td className="py-1 px-2">
                                      <div className="flex items-center justify-end gap-0.5">
                                        <button
                                          onClick={() => handleDownloadClick(result, index)}
                                          disabled={downloadingIndex !== null}
                                          className="p-1 bg-gray-700 hover:bg-gray-600 disabled:bg-gray-800 disabled:cursor-not-allowed text-white rounded transition-colors"
                                          title={result.isSeasonPack ? `Download season pack (${result.matchedEventCount} events)` : 'Download'}
                                        >
                                          {downloadingIndex === index ? (
                                            <div className="animate-spin rounded-full h-3.5 w-3.5 border-b-2 border-white"></div>
                                          ) : (
                                            <ArrowDownTrayIcon className="w-3.5 h-3.5" />
                                          )}
                                        </button>
                                      </div>
                                    </td>
                                  </tr>

                                  {/* Expanded row showing matched events */}
                                  {isExpanded && (
                                    <tr className="bg-gray-900/50">
                                      <td colSpan={12} className="py-2 px-4">
                                        <div className="text-xs">
                                          <p className="text-gray-400 mb-2 font-medium">Matched Events:</p>
                                          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-2">
                                            {result.matchedEvents.map((event) => (
                                              <div
                                                key={event.eventId}
                                                className="flex items-center gap-2 px-2 py-1 bg-gray-800/50 rounded"
                                              >
                                                {event.hasFile ? (
                                                  <CheckCircleIcon className="w-4 h-4 text-green-500 flex-shrink-0" />
                                                ) : event.monitored ? (
                                                  <XCircleIcon className="w-4 h-4 text-red-500 flex-shrink-0" />
                                                ) : (
                                                  <div className="w-4 h-4 border border-gray-600 rounded flex-shrink-0" />
                                                )}
                                                <div className="min-w-0 flex-1">
                                                  <p className="text-white truncate" title={event.eventTitle}>
                                                    {event.eventTitle}
                                                  </p>
                                                  <p className="text-gray-500 text-[10px]">
                                                    {new Date(event.eventDate).toLocaleDateString()}
                                                    {event.detectedPart && (
                                                      <span className="ml-1 px-1 bg-purple-900/50 text-purple-400 rounded">
                                                        {event.detectedPart}
                                                      </span>
                                                    )}
                                                  </p>
                                                </div>
                                                <span className={`text-[10px] px-1 rounded ${
                                                  event.confidence >= 80 ? 'bg-green-900/50 text-green-400' :
                                                  event.confidence >= 50 ? 'bg-yellow-900/50 text-yellow-400' :
                                                  'bg-gray-700 text-gray-400'
                                                }`}>
                                                  {event.confidence}%
                                                </span>
                                              </div>
                                            ))}
                                          </div>
                                        </div>
                                      </td>
                                    </tr>
                                  )}
                                </Fragment>
                              );
                            })}
                          </tbody>
                        </table>
                      ) : searchResults ? (
                        <div className="p-8 text-center text-gray-400">
                          <p>No releases found matching this season.</p>
                          <p className="text-sm mt-2">Try adjusting your indexers or search later.</p>
                        </div>
                      ) : (
                        <div className="p-8 text-center">
                          <MagnifyingGlassIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                          <p className="text-gray-400 mb-2">No search performed yet</p>
                          <p className="text-gray-500 text-sm">
                            Click "Search Indexers" to find releases for this season
                          </p>
                        </div>
                      )}
                    </div>
                  </>
                )}

                {/* History Tab Content */}
                {activeTab === 'history' && (
                  <div className="max-h-[65vh] overflow-y-auto">
                    {isLoadingHistory ? (
                      <div className="p-8 text-center">
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                        <p className="text-gray-400">Loading history...</p>
                      </div>
                    ) : history.length > 0 ? (
                      <table className="w-full text-xs">
                        <thead className="bg-gray-900/80 sticky top-0 z-10">
                          <tr className="border-b border-gray-800">
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[28px]"></th>
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[120px]">Event</th>
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium min-w-[150px]">Source Title</th>
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[70px]">Language</th>
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[90px]">Quality</th>
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[140px]">Date</th>
                            <th className="text-right py-1.5 px-2 text-gray-400 font-medium w-[60px]">Actions</th>
                          </tr>
                        </thead>
                        <tbody>
                          {history.map((item) => (
                            <tr key={`${item.type}-${item.id}`} className="border-b border-gray-800/50 hover:bg-gray-800/30 transition-colors">
                              <td className="py-1 px-2">
                                {getHistoryIcon(item.type)}
                              </td>
                              <td className="py-1 px-2">
                                <span className="text-gray-300 truncate block" title={item.eventTitle}>
                                  {item.eventTitle || '-'}
                                </span>
                              </td>
                              <td className="py-1 px-2" style={{ maxWidth: '300px' }}>
                                <div className="flex flex-col">
                                  <span className="text-white truncate" title={item.sourcePath}>
                                    {item.sourcePath}
                                  </span>
                                  {item.destinationPath && (
                                    <span className="text-gray-500 text-[10px] truncate" title={item.destinationPath}>
                                      → {item.destinationPath}
                                    </span>
                                  )}
                                </div>
                              </td>
                              <td className="py-1 px-2">
                                <span className="px-1 py-0.5 bg-gray-700 text-gray-300 text-[10px] rounded whitespace-nowrap">
                                  English
                                </span>
                              </td>
                              <td className="py-1 px-2">
                                {item.quality ? (
                                  <span className="px-1 py-0.5 bg-blue-900/50 text-blue-400 text-[10px] rounded whitespace-nowrap">
                                    {item.quality}
                                  </span>
                                ) : (
                                  <span className="text-gray-600">-</span>
                                )}
                              </td>
                              <td className="py-1 px-2 text-gray-400 whitespace-nowrap">
                                {formatDateTime(item.date)}
                              </td>
                              <td className="py-1 px-2">
                                <div className="flex items-center justify-end gap-0.5">
                                  {(item.errors.length > 0 || item.warnings.length > 0) && (
                                    <div className="relative group">
                                      <InformationCircleIcon className="w-3.5 h-3.5 text-gray-500 cursor-help" />
                                      <div className="absolute right-0 top-5 z-50 hidden group-hover:block w-56 p-1.5 bg-gray-900 border border-gray-700 rounded-lg shadow-xl text-left">
                                        {item.errors.map((e, i) => (
                                          <p key={i} className="text-red-400 text-[10px]">• {e}</p>
                                        ))}
                                        {item.warnings.map((w, i) => (
                                          <p key={i} className="text-yellow-400 text-[10px]">• {w}</p>
                                        ))}
                                      </div>
                                    </div>
                                  )}
                                  {item.type === 'grabbed' && (
                                    <button
                                      onClick={() => setMarkFailedConfirm(item)}
                                      className="p-1 text-gray-500 hover:text-red-400 hover:bg-gray-800 rounded transition-colors"
                                      title="Mark as Failed"
                                    >
                                      <XMarkIcon className="w-3.5 h-3.5" />
                                    </button>
                                  )}
                                </div>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    ) : (
                      <div className="p-8 text-center">
                        <InformationCircleIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                        <p className="text-gray-400 mb-2">No history for this season</p>
                        <p className="text-gray-500 text-sm">
                          Download history will appear here after grabbing releases
                        </p>
                      </div>
                    )}
                  </div>
                )}

                {/* Footer */}
                <div className="px-6 py-3 bg-gray-900/50 border-t border-red-900/30 flex justify-end">
                  <button
                    onClick={onClose}
                    className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors text-sm"
                  >
                    Close
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>

      {/* Blocklist Override Confirmation Dialog */}
      {blocklistConfirm && (
        <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-[60] p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-orange-700 rounded-lg max-w-lg w-full p-6">
            <div className="flex items-start gap-3 mb-4">
              <NoSymbolIcon className="w-8 h-8 text-orange-400 flex-shrink-0" />
              <div>
                <h3 className="text-xl font-bold text-white">Download Blocklisted Release?</h3>
                <p className="text-orange-400 text-sm mt-1">This release has been blocklisted</p>
              </div>
            </div>

            <div className="bg-orange-900/20 border border-orange-600/30 rounded-lg p-4 mb-4">
              <p className="text-white font-medium text-sm truncate mb-2" title={blocklistConfirm.result.title}>
                {blocklistConfirm.result.title}
              </p>
              {blocklistConfirm.result.blocklistReason && (
                <p className="text-orange-300 text-sm">
                  <span className="text-gray-400">Reason: </span>
                  {blocklistConfirm.result.blocklistReason}
                </p>
              )}
            </div>

            <p className="text-gray-300 text-sm mb-6">
              This release was previously blocklisted. Are you sure you want to download it anyway?
            </p>

            <div className="flex justify-end gap-3">
              <button
                onClick={() => setBlocklistConfirm(null)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDownload(blocklistConfirm.result, blocklistConfirm.index)}
                className="px-4 py-2 bg-orange-600 hover:bg-orange-700 text-white rounded-lg transition-colors flex items-center gap-2"
              >
                <ArrowDownTrayIcon className="w-4 h-4" />
                Download Anyway
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Mark as Failed Confirmation Dialog */}
      {markFailedConfirm && (
        <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-[60] p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-lg w-full p-6">
            <div className="flex items-start gap-3 mb-4">
              <TrashIcon className="w-8 h-8 text-red-400 flex-shrink-0" />
              <div>
                <h3 className="text-xl font-bold text-white">Mark as Failed?</h3>
                <p className="text-red-400 text-sm mt-1">This will blocklist the release</p>
              </div>
            </div>

            <div className="bg-red-900/20 border border-red-600/30 rounded-lg p-4 mb-4">
              <p className="text-white font-medium text-sm truncate" title={markFailedConfirm.sourcePath}>
                {markFailedConfirm.sourcePath}
              </p>
            </div>

            <p className="text-gray-300 text-sm mb-6">
              This will add the release to the blocklist so it won't be downloaded again.
              Would you like to search for a replacement?
            </p>

            <div className="flex justify-end gap-3">
              <button
                onClick={() => setMarkFailedConfirm(null)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleMarkAsFailed(markFailedConfirm, false)}
                className="px-4 py-2 bg-gray-600 hover:bg-gray-500 text-white rounded-lg transition-colors"
              >
                Blocklist Only
              </button>
              <button
                onClick={() => handleMarkAsFailed(markFailedConfirm, true)}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors flex items-center gap-2"
              >
                <MagnifyingGlassIcon className="w-4 h-4" />
                Blocklist & Search
              </button>
            </div>
          </div>
        </div>
      )}
    </Transition>
  );
}
