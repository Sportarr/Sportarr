import { Fragment, useState, useEffect, useMemo } from 'react';
import { toast } from 'sonner';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  MagnifyingGlassIcon,
  ArrowDownTrayIcon,
  ExclamationTriangleIcon,
  NoSymbolIcon,
  ArrowPathRoundedSquareIcon,
  ChevronUpIcon,
  ChevronDownIcon,
  FunnelIcon,
  TrashIcon,
  InformationCircleIcon,
  CloudArrowDownIcon,
  CheckCircleIcon,
} from '@heroicons/react/24/outline';
import { apiPost, apiGet, apiDelete } from '../utils/api';

interface ExistingPartFile {
  partName?: string;
  quality?: string;
  codec?: string;
  source?: string;
}

interface ManualSearchModalProps {
  isOpen: boolean;
  onClose: () => void;
  eventId: number;
  eventTitle: string;
  part?: string;
  existingFiles?: ExistingPartFile[];
}

interface MatchedFormat {
  name: string;
  score: number;
}

interface ReleaseSearchResult {
  title: string;
  guid: string;
  downloadUrl: string;
  indexer: string;
  indexerFlags?: string[];
  size: number;
  publishDate: string;
  seeders: number | null;
  leechers: number | null;
  quality: string | null;
  codec?: string | null;
  source?: string | null;
  language?: string | null;
  score: number;
  approved: boolean;
  rejections: string[];
  matchedFormats: MatchedFormat[];
  qualityScore: number;
  customFormatScore: number;
  isBlocklisted?: boolean;
  blocklistReason?: string;
  protocol?: 'torrent' | 'usenet';
}

interface QueueItem {
  eventId: number;
  title: string;
  status: string;
}

interface HistoryItem {
  id: number;
  type: 'import' | 'grabbed' | 'completed' | 'failed' | 'warning' | 'blocklist';
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
}

type SortDirection = 'asc' | 'desc';
type TabType = 'search' | 'history';

export default function ManualSearchModal({
  isOpen,
  onClose,
  eventId,
  eventTitle,
  part,
  existingFiles,
}: ManualSearchModalProps) {
  const [activeTab, setActiveTab] = useState<TabType>('search');
  const [isSearching, setIsSearching] = useState(false);
  const [searchResults, setSearchResults] = useState<ReleaseSearchResult[]>([]);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [downloadingIndex, setDownloadingIndex] = useState<number | null>(null);
  const [blocklistConfirm, setBlocklistConfirm] = useState<{ index: number; result: ReleaseSearchResult } | null>(null);
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');
  const [hasExistingFile, setHasExistingFile] = useState(false);
  const [queueItems, setQueueItems] = useState<QueueItem[]>([]);
  const [showFilters, setShowFilters] = useState(false);

  // History state
  const [history, setHistory] = useState<HistoryItem[]>([]);
  const [isLoadingHistory, setIsLoadingHistory] = useState(false);
  const [markFailedConfirm, setMarkFailedConfirm] = useState<HistoryItem | null>(null);

  // Clear search results when event changes or modal opens for a different event
  useEffect(() => {
    if (isOpen) {
      setSearchResults([]);
      setSearchError(null);
      setDownloadingIndex(null);
      setBlocklistConfirm(null);
      setActiveTab('search');
      checkExistingFileAndQueue();
      loadHistory();
    }
  }, [isOpen, eventId, part]);

  // Check if there's an existing file or queue item for this event/part
  const checkExistingFileAndQueue = async () => {
    try {
      const hasFiles = existingFiles && existingFiles.length > 0;
      const hasCurrentPartFile = part
        ? existingFiles?.some(f => f.partName === part)
        : hasFiles;
      setHasExistingFile(!!hasCurrentPartFile);

      const queueResponse = await apiGet('/api/queue');
      if (queueResponse.ok) {
        const queue = await queueResponse.json();
        const relevantItems = queue.filter((item: QueueItem) => item.eventId === eventId);
        setQueueItems(relevantItems);
      }
    } catch (error) {
      console.error('Failed to check existing files/queue:', error);
    }
  };

  // Load history for this event (filtered by part if specified)
  const loadHistory = async () => {
    setIsLoadingHistory(true);
    try {
      // Include part parameter if searching for a specific part of a multi-part event
      const url = part
        ? `/api/event/${eventId}/history?part=${encodeURIComponent(part)}`
        : `/api/event/${eventId}/history`;
      const response = await apiGet(url);
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
    setSearchResults([]);

    try {
      const endpoint = `/api/event/${eventId}/search`;
      const response = await apiPost(endpoint, { part });
      const results = await response.json();
      setSearchResults(results || []);
    } catch (error) {
      console.error('Search failed:', error);
      setSearchError('Failed to search indexers. Please try again.');
    } finally {
      setIsSearching(false);
    }
  };

  const handleDownloadClick = (release: ReleaseSearchResult, index: number, isOverride: boolean = false) => {
    if (release.isBlocklisted) {
      setBlocklistConfirm({ index, result: release });
      return;
    }
    handleDownload(release, index, isOverride);
  };

  const handleDownload = async (release: ReleaseSearchResult, index: number, isOverride: boolean = false) => {
    setBlocklistConfirm(null);
    setDownloadingIndex(index);
    setSearchError(null);

    try {
      if (isOverride && queueItems.length > 0) {
        for (const item of queueItems) {
          try {
            await apiPost(`/api/queue/${item.eventId}/remove`, {});
          } catch (e) {
            console.warn('Failed to remove queue item:', e);
          }
        }
      }

      const response = await apiPost('/api/release/grab', {
        ...release,
        eventId: eventId,
        overrideBlocklist: release.isBlocklisted,
        replaceExisting: isOverride,
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || 'Download failed');
      }

      const result = await response.json();
      console.log('Download started:', result);
      toast.success(isOverride ? 'Override Started' : 'Download Started', {
        description: `${release.title}\n\nThe release has been sent to your download client.`,
      });

      onClose();
    } catch (error) {
      console.error('Download failed:', error);
      const errorMessage = error instanceof Error ? error.message : 'Failed to start download. Please try again.';
      setSearchError(errorMessage);
    } finally {
      setDownloadingIndex(null);
    }
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
        checkExistingFileAndQueue();
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

  const getSearchTitle = () => {
    return part ? `${eventTitle} (${part})` : eventTitle;
  };

  const extractResolution = (quality: string | undefined | null): string | null => {
    if (!quality) return null;
    const match = quality.match(/\b(2160p|1080p|720p|480p|360p)\b/i);
    return match ? match[1].toLowerCase() : null;
  };

  const getReleaseMismatchWarnings = (release: ReleaseSearchResult): string[] => {
    if (!part || !existingFiles || existingFiles.length === 0) return [];

    const otherPartFiles = existingFiles.filter(f => f.partName && f.partName !== part);
    if (otherPartFiles.length === 0) return [];

    const warnings: string[] = [];
    const referenceFile = otherPartFiles[0];

    const fileResolution = extractResolution(referenceFile.quality);
    const releaseResolution = release.quality?.toLowerCase();
    if (fileResolution && releaseResolution && fileResolution !== releaseResolution) {
      warnings.push(`Different resolution than ${referenceFile.partName}: ${fileResolution}`);
    }

    if (referenceFile.codec && release.codec && referenceFile.codec !== release.codec) {
      warnings.push(`Different codec than ${referenceFile.partName}: ${referenceFile.codec}`);
    }

    if (referenceFile.source && release.source && referenceFile.source !== release.source) {
      warnings.push(`Different source than ${referenceFile.partName}: ${referenceFile.source}`);
    }

    return warnings;
  };

  // Filter out "Not X" language formats - they're useless to show
  const getFilteredFormats = (formats: MatchedFormat[] | undefined) => {
    if (!formats) return [];
    return formats.filter(f => !f.name.toLowerCase().startsWith('not '));
  };

  const getAllRejections = (result: ReleaseSearchResult): string[] => {
    const rejections = [...(result.rejections || [])];

    if (result.customFormatScore < 0) {
      // Filter out "Not X" formats from rejection message
      const negativeFormats = result.matchedFormats
        ?.filter(f => f.score < 0 && !f.name.toLowerCase().startsWith('not '))
        .map(f => f.name)
        .join(', ');
      if (negativeFormats) {
        rejections.push(`Custom Formats ${negativeFormats} have score ${result.customFormatScore} below minimum`);
      }
    }

    return rejections;
  };

  const getProtocol = (result: ReleaseSearchResult): 'torrent' | 'usenet' => {
    // Check explicit protocol from backend (case-insensitive)
    if (result.protocol) {
      const proto = result.protocol.toLowerCase();
      if (proto === 'torrent' || proto.includes('torrent')) return 'torrent';
      if (proto === 'usenet' || proto.includes('usenet') || proto === 'nzb') return 'usenet';
    }
    // Fallback: If has seeders/leechers data, it's a torrent
    if (result.seeders !== null || result.leechers !== null) return 'torrent';
    // Fallback: Check indexer name
    if (result.indexer?.toLowerCase().includes('nzb')) return 'usenet';
    return 'usenet';
  };

  const sortedResults = useMemo(() => {
    return [...searchResults].sort((a, b) => {
      const scoreA = a.score;
      const scoreB = b.score;
      return sortDirection === 'desc' ? scoreB - scoreA : scoreA - scoreB;
    });
  }, [searchResults, sortDirection]);

  const toggleSort = () => {
    setSortDirection(prev => prev === 'desc' ? 'asc' : 'desc');
  };

  // Get icon for history item type (matching Sonarr's conventions)
  const getHistoryIcon = (type: string) => {
    switch (type) {
      case 'grabbed':
        // Cloud with down arrow - currently being downloaded/grabbed from indexer
        return <CloudArrowDownIcon className="w-4 h-4 text-blue-400" title="Grabbed" />;
      case 'import':
      case 'completed':
        // Download/import complete - file was successfully downloaded and imported
        return <ArrowDownTrayIcon className="w-4 h-4 text-green-400" title="Imported" />;
      case 'failed':
        return <XMarkIcon className="w-4 h-4 text-red-400" title="Failed" />;
      case 'warning':
        return <ExclamationTriangleIcon className="w-4 h-4 text-yellow-400" title="Warning" />;
      case 'blocklist':
        return <NoSymbolIcon className="w-4 h-4 text-orange-400" title="Blocklisted" />;
      case 'deleted':
        // Trash icon for deleted files
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
              <Dialog.Panel className="w-[98vw] max-w-none transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header with Tabs */}
                <div className="relative bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900 border-b border-red-900/30">
                  <div className="px-6 py-4 flex items-center justify-between">
                    <div>
                      <h2 className="text-xl font-bold text-white">{getSearchTitle()}</h2>
                    </div>
                    <button
                      onClick={onClose}
                      className="p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors"
                    >
                      <XMarkIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>

                  {/* Tabs */}
                  <div className="px-6 flex gap-1">
                    <button
                      onClick={() => setActiveTab('search')}
                      className={`px-4 py-2 text-sm font-medium rounded-t-lg transition-colors ${
                        activeTab === 'search'
                          ? 'bg-gray-800 text-white border-t border-l border-r border-gray-700'
                          : 'text-gray-400 hover:text-white hover:bg-gray-800/50'
                      }`}
                    >
                      Search
                    </button>
                    <button
                      onClick={() => setActiveTab('history')}
                      className={`px-4 py-2 text-sm font-medium rounded-t-lg transition-colors ${
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
                    <div className="px-6 py-3 border-b border-gray-800 flex items-center justify-between">
                      <p className="text-gray-400 text-sm">
                        Search indexers for available releases
                      </p>
                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => setShowFilters(!showFilters)}
                          className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-300 rounded transition-colors flex items-center gap-1.5 text-sm"
                        >
                          <FunnelIcon className="w-4 h-4" />
                          Filter
                        </button>
                        <button
                          onClick={handleSearch}
                          disabled={isSearching}
                          className="px-4 py-1.5 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded transition-colors flex items-center gap-2 text-sm"
                        >
                          {isSearching ? (
                            <>
                              <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                              <span>Searching...</span>
                            </>
                          ) : (
                            <>
                              <MagnifyingGlassIcon className="w-4 h-4" />
                              <span>Search Indexers</span>
                            </>
                          )}
                        </button>
                      </div>
                    </div>

                    {/* Error Message */}
                    {searchError && (
                      <div className="mx-6 mt-3 bg-red-900/20 border border-red-600/50 rounded-lg p-3">
                        <p className="text-red-400 text-sm">{searchError}</p>
                      </div>
                    )}

                    {/* Results Count */}
                    {searchResults.length > 0 && (
                      <div className="px-6 py-2 text-gray-400 text-sm">
                        Found {searchResults.length} releases
                      </div>
                    )}

                    {/* Content - Table Layout */}
                    <div className="max-h-[65vh] overflow-y-auto">
                      {isSearching ? (
                        <div className="p-8 text-center">
                          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                          <p className="text-gray-400">Searching indexers for releases...</p>
                        </div>
                      ) : sortedResults.length > 0 ? (
                        <table className="w-full text-xs table-fixed">
                          <thead className="bg-gray-900/80 sticky top-0 z-10">
                            <tr className="border-b border-gray-800">
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[52px]">Source</th>
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[60px]">Age</th>
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium">Title</th>
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[100px]">Indexer</th>
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[60px]">Size</th>
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[70px]">Peers</th>
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[70px]">Language</th>
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[120px]">Quality</th>
                              <th
                                className="text-center py-1.5 px-2 text-gray-400 font-medium w-[50px] cursor-pointer hover:text-white transition-colors select-none"
                                onClick={toggleSort}
                                title="Click to sort"
                              >
                                <div className="flex items-center justify-center gap-0.5">
                                  <span>Score</span>
                                  {sortDirection === 'desc' ? (
                                    <ChevronDownIcon className="w-3 h-3" />
                                  ) : (
                                    <ChevronUpIcon className="w-3 h-3" />
                                  )}
                                </div>
                              </th>
                              <th className="text-center py-1.5 px-2 text-gray-400 font-medium w-[24px]" title="Warnings"></th>
                              <th className="text-right py-1.5 px-2 text-gray-400 font-medium w-[70px]">Actions</th>
                            </tr>
                          </thead>
                          <tbody>
                            {sortedResults.map((result, index) => {
                              const protocol = getProtocol(result);
                              const rejections = getAllRejections(result);
                              const mismatchWarnings = getReleaseMismatchWarnings(result);
                              const hasWarnings = rejections.length > 0 || result.isBlocklisted;
                              const showOverride = hasExistingFile || queueItems.length > 0;

                              return (
                                <tr
                                  key={index}
                                  className={`border-b border-gray-800/50 hover:bg-gray-800/30 transition-colors ${
                                    result.isBlocklisted ? 'bg-orange-900/10' : ''
                                  }`}
                                >
                                  <td className="py-1 px-2">
                                    <span className={`px-1 py-0.5 text-[10px] font-semibold rounded ${
                                      protocol === 'torrent'
                                        ? 'bg-green-900/50 text-green-400'
                                        : 'bg-blue-900/50 text-blue-400'
                                    }`}>
                                      {protocol === 'torrent' ? 'torrent' : 'nzb'}
                                    </span>
                                  </td>
                                  <td className="py-1 px-2 text-gray-400 whitespace-nowrap">
                                    {formatAge(result.publishDate)}
                                  </td>
                                  <td className="py-1 px-2">
                                    <div className="flex items-center gap-1 min-w-0">
                                      {result.isBlocklisted && (
                                        <NoSymbolIcon className="w-3 h-3 text-orange-400 flex-shrink-0" />
                                      )}
                                      <span
                                        className={`truncate ${result.isBlocklisted ? 'text-orange-300' : 'text-white'}`}
                                        title={result.title}
                                      >
                                        {result.title}
                                      </span>
                                    </div>
                                  </td>
                                  <td className="py-1 px-2">
                                    <span className="text-gray-300 truncate block" title={result.indexer}>
                                      {result.indexer}
                                    </span>
                                  </td>
                                  <td className="py-1 px-2 text-gray-400 whitespace-nowrap">
                                    {formatFileSize(result.size)}
                                  </td>
                                  <td className="py-1 px-2">
                                    {protocol === 'torrent' && result.seeders !== null ? (
                                      <span className="whitespace-nowrap">
                                        <span className="text-green-400">↑{result.seeders}</span>
                                        {result.leechers !== null && (
                                          <span className="text-red-400 ml-1">↓{result.leechers}</span>
                                        )}
                                      </span>
                                    ) : (
                                      <span className="text-gray-600">-</span>
                                    )}
                                  </td>
                                  <td className="py-1 px-2">
                                    {result.language ? (
                                      <span className="px-1 py-0.5 bg-gray-700 text-gray-300 text-[10px] rounded whitespace-nowrap">
                                        {result.language}
                                      </span>
                                    ) : (
                                      <span className="text-gray-600">-</span>
                                    )}
                                  </td>
                                  <td className="py-1 px-2">
                                    <div className="flex flex-col">
                                      <span className="px-1 py-0.5 bg-blue-900/50 text-blue-400 text-[10px] rounded inline-block w-fit whitespace-nowrap">
                                        {result.quality || 'Unknown'}
                                      </span>
                                      {mismatchWarnings.length > 0 && (
                                        <div className="relative group flex items-center gap-0.5 mt-0.5">
                                          <ExclamationTriangleIcon className="w-3 h-3 text-orange-400 flex-shrink-0 cursor-help" />
                                          <span className="text-[9px] text-orange-400 truncate max-w-[90px]">
                                            {mismatchWarnings.length === 1 ? mismatchWarnings[0].split(':')[0] : `${mismatchWarnings.length} warnings`}
                                          </span>
                                          <div className="absolute left-0 top-4 z-50 hidden group-hover:block w-64 p-2 bg-gray-900 border border-gray-700 rounded-lg shadow-xl text-left">
                                            <p className="text-orange-400 text-[10px] font-semibold mb-1">Quality Mismatch:</p>
                                            {mismatchWarnings.map((w, i) => (
                                              <p key={i} className="text-gray-400 text-[10px]">• {w}</p>
                                            ))}
                                          </div>
                                        </div>
                                      )}
                                    </div>
                                  </td>
                                  <td className="py-1 px-2 text-center">
                                    <div className="relative group">
                                      <span
                                        className={`font-bold text-xs cursor-help ${
                                          result.customFormatScore > 0 ? 'text-green-400' :
                                          result.customFormatScore < 0 ? 'text-red-400' :
                                          'text-gray-400'
                                        }`}
                                      >
                                        {result.customFormatScore > 0 ? '+' : ''}{result.customFormatScore}
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
                                  <td className="py-1 px-2">
                                    <div className="flex items-center justify-end gap-0.5">
                                      <button
                                        onClick={() => handleDownloadClick(result, index, false)}
                                        disabled={downloadingIndex !== null}
                                        className="p-1 bg-gray-700 hover:bg-gray-600 disabled:bg-gray-800 disabled:cursor-not-allowed text-white rounded transition-colors"
                                        title="Download"
                                      >
                                        {downloadingIndex === index ? (
                                          <div className="animate-spin rounded-full h-3.5 w-3.5 border-b-2 border-white"></div>
                                        ) : (
                                          <ArrowDownTrayIcon className="w-3.5 h-3.5" />
                                        )}
                                      </button>
                                      {showOverride && (
                                        <button
                                          onClick={() => handleDownloadClick(result, index, true)}
                                          disabled={downloadingIndex !== null}
                                          className="p-1 bg-orange-700 hover:bg-orange-600 disabled:bg-gray-800 disabled:cursor-not-allowed text-white rounded transition-colors"
                                          title={queueItems.length > 0 ? "Replace queued download" : "Replace existing file"}
                                        >
                                          <ArrowPathRoundedSquareIcon className="w-3.5 h-3.5" />
                                        </button>
                                      )}
                                    </div>
                                  </td>
                                </tr>
                              );
                            })}
                          </tbody>
                        </table>
                      ) : (
                        <div className="p-8 text-center">
                          <MagnifyingGlassIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                          <p className="text-gray-400 mb-2">No search performed yet</p>
                          <p className="text-gray-500 text-sm">
                            Click "Search Indexers" to manually search for releases
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
                      <table className="w-full text-xs table-fixed">
                        <thead className="bg-gray-900/80 sticky top-0 z-10">
                          <tr className="border-b border-gray-800">
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[28px]"></th>
                            <th className="text-left py-1.5 px-2 text-gray-400 font-medium">Source Title</th>
                            {/* Show Part column when not filtered by part (viewing whole event history) */}
                            {!part && history.some(h => h.part) && (
                              <th className="text-left py-1.5 px-2 text-gray-400 font-medium w-[80px]">Part</th>
                            )}
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
                                <div className="flex flex-col min-w-0">
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
                              {/* Show Part column when not filtered by part */}
                              {!part && history.some(h => h.part) && (
                                <td className="py-1 px-2">
                                  {item.part ? (
                                    <span className="px-1 py-0.5 bg-purple-900/50 text-purple-400 text-[10px] rounded whitespace-nowrap">
                                      {item.part}
                                    </span>
                                  ) : (
                                    <span className="text-gray-600">-</span>
                                  )}
                                </td>
                              )}
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
                                  {/* Info tooltip */}
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
                                  {/* Mark as Failed (only for grabbed items - items still downloading) */}
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
                        <p className="text-gray-400 mb-2">No history for this event</p>
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
              This will override the blocklist for this download only.
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
