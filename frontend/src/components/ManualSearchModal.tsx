import { Fragment, useState } from 'react';
import { toast } from 'sonner';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  MagnifyingGlassIcon,
  ArrowDownTrayIcon,
} from '@heroicons/react/24/outline';
import { apiPost } from '../utils/api';

interface ManualSearchModalProps {
  isOpen: boolean;
  onClose: () => void;
  searchType: 'organization' | 'event' | 'fightcard';
  title: string;
  searchParams: {
    organizationName?: string;
    eventId?: number;
    fightCardId?: number;
  };
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
  size: number;
  publishDate: string;
  seeders: number | null;
  leechers: number | null;
  quality: string | null;
  score: number;
  approved: boolean;
  rejections: string[];
  matchedFormats: MatchedFormat[];
  qualityScore: number;
  customFormatScore: number;
}

export default function ManualSearchModal({
  isOpen,
  onClose,
  searchType,
  title,
  searchParams,
}: ManualSearchModalProps) {
  const [isSearching, setIsSearching] = useState(false);
  const [searchResults, setSearchResults] = useState<ReleaseSearchResult[]>([]);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [downloadingIndex, setDownloadingIndex] = useState<number | null>(null);

  const formatFileSize = (bytes?: number) => {
    if (!bytes) return 'N/A';
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    if (bytes === 0) return '0 Byte';
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return Math.round((bytes / Math.pow(1024, i)) * 100) / 100 + ' ' + sizes[i];
  };

  const handleSearch = async () => {
    setIsSearching(true);
    setSearchError(null);
    setSearchResults([]);

    try {
      let endpoint = '';
      if (searchType === 'event' && searchParams.eventId) {
        endpoint = `/api/event/${searchParams.eventId}/search`;
      } else if (searchType === 'fightcard' && searchParams.fightCardId) {
        endpoint = `/api/fightcard/${searchParams.fightCardId}/search`;
      } else if (searchType === 'organization' && searchParams.organizationName) {
        endpoint = `/api/organization/${encodeURIComponent(searchParams.organizationName)}/search`;
      }

      const response = await apiPost(endpoint, {});
      const results = await response.json();
      setSearchResults(results || []);
    } catch (error) {
      console.error('Search failed:', error);
      setSearchError('Failed to search indexers. Please try again.');
    } finally {
      setIsSearching(false);
    }
  };

  const handleDownload = async (release: ReleaseSearchResult, index: number) => {
    setDownloadingIndex(index);
    setSearchError(null);

    try {
      const response = await apiPost('/api/release/grab', {
        ...release,
        ...(searchParams.eventId && { eventId: searchParams.eventId }),
        ...(searchParams.fightCardId && { fightCardId: searchParams.fightCardId }),
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || 'Download failed');
      }

      const result = await response.json();
      console.log('Download started:', result);
      toast.success('Download Started', {
        description: `${release.title}\n\nThe release has been sent to your download client.`,
      });
    } catch (error) {
      console.error('Download failed:', error);
      const errorMessage = error instanceof Error ? error.message : 'Failed to start download. Please try again.';
      setSearchError(errorMessage);
    } finally {
      setDownloadingIndex(null);
    }
  };

  const getSearchTitle = () => {
    switch (searchType) {
      case 'organization':
        return `Search All Events in ${title}`;
      case 'event':
        return `Search Event: ${title}`;
      case 'fightcard':
        return `Search Fight Card: ${title}`;
    }
  };

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
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-4xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header */}
                <div className="relative bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900 border-b border-red-900/30 p-6">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-2xl font-bold text-white mb-1">{getSearchTitle()}</h2>
                      <p className="text-gray-400 text-sm">Manual search for releases</p>
                    </div>
                    <button
                      onClick={onClose}
                      className="p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors"
                    >
                      <XMarkIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>
                </div>

                {/* Content */}
                <div className="p-6 max-h-[70vh] overflow-y-auto">
                  <div className="space-y-4">
                    {/* Search Button */}
                    <div className="flex items-center justify-between">
                      <p className="text-gray-400 text-sm">
                        Search indexers for available releases
                      </p>
                      <button
                        onClick={handleSearch}
                        disabled={isSearching}
                        className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded-lg transition-colors flex items-center gap-2"
                      >
                        {isSearching ? (
                          <>
                            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                            <span>Searching...</span>
                          </>
                        ) : (
                          <>
                            <MagnifyingGlassIcon className="w-5 h-5" />
                            <span>Search Indexers</span>
                          </>
                        )}
                      </button>
                    </div>

                    {/* Error Message */}
                    {searchError && (
                      <div className="bg-red-900/20 border border-red-600/50 rounded-lg p-4">
                        <p className="text-red-400 text-sm">{searchError}</p>
                      </div>
                    )}

                    {/* Search Results */}
                    {isSearching ? (
                      <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                        <p className="text-gray-400">Searching indexers for releases...</p>
                      </div>
                    ) : searchResults.length > 0 ? (
                      <div className="space-y-2">
                        <p className="text-gray-400 text-sm mb-3">Found {searchResults.length} releases</p>
                        {searchResults.map((result, index) => (
                          <div
                            key={index}
                            className={`bg-gray-800/50 rounded-lg p-4 border ${
                              !result.approved ? 'border-yellow-600/30 opacity-60' : 'border-red-900/20'
                            } hover:border-red-600/50 transition-colors`}
                          >
                            <div className="flex items-start justify-between gap-4">
                              <div className="flex-1 min-w-0">
                                <div className="flex items-center gap-2 mb-1">
                                  <h4 className="text-white font-medium truncate">{result.title}</h4>
                                  {!result.approved && (
                                    <span className="px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded flex-shrink-0">
                                      REJECTED
                                    </span>
                                  )}
                                </div>
                                <div className="flex items-center gap-3 text-sm text-gray-400 mb-2">
                                  <span className="px-2 py-0.5 bg-red-900/30 text-red-400 rounded text-xs">
                                    {result.indexer}
                                  </span>
                                  {result.quality && (
                                    <span className="font-semibold text-blue-400">{result.quality}</span>
                                  )}
                                  <span>{formatFileSize(result.size)}</span>
                                  {result.seeders !== null && (
                                    <span className="text-green-400">↑ {result.seeders} seeds</span>
                                  )}
                                  {result.leechers !== null && (
                                    <span className="text-yellow-400">↓ {result.leechers} peers</span>
                                  )}
                                </div>
                                {/* Custom Format Scores */}
                                {result.matchedFormats && result.matchedFormats.length > 0 && (
                                  <div className="flex flex-wrap gap-2 mb-2">
                                    {result.matchedFormats.map((format, fIdx) => (
                                      <span
                                        key={fIdx}
                                        className={`px-2 py-0.5 text-xs rounded ${
                                          format.score > 0
                                            ? 'bg-green-900/30 text-green-400'
                                            : 'bg-red-900/30 text-red-400'
                                        }`}
                                      >
                                        {format.name} ({format.score > 0 ? '+' : ''}
                                        {format.score})
                                      </span>
                                    ))}
                                  </div>
                                )}
                                {/* Rejection Reasons */}
                                {result.rejections && result.rejections.length > 0 && (
                                  <div className="mt-2 space-y-1">
                                    {result.rejections.map((rejection, rIdx) => (
                                      <p key={rIdx} className="text-xs text-yellow-500 flex items-start gap-1">
                                        <span>⚠</span>
                                        <span>{rejection}</span>
                                      </p>
                                    ))}
                                  </div>
                                )}
                              </div>
                              <div className="flex flex-col items-end gap-2">
                                <div className="text-right text-xs">
                                  <div className="mb-1">
                                    <span className="text-gray-400">Score: </span>
                                    <span
                                      className={`font-bold text-lg ${
                                        result.score >= 1000
                                          ? 'text-green-400'
                                          : result.score >= 600
                                          ? 'text-blue-400'
                                          : result.score >= 400
                                          ? 'text-yellow-400'
                                          : 'text-gray-400'
                                      }`}
                                    >
                                      {result.score}
                                    </span>
                                  </div>
                                  {(result.qualityScore > 0 || result.customFormatScore !== 0) && (
                                    <div className="space-y-0.5 text-gray-500">
                                      <div>
                                        <span className="text-gray-400">Quality:</span>{' '}
                                        <span className="text-white">{result.qualityScore}</span>
                                      </div>
                                      {result.customFormatScore !== 0 && (
                                        <div>
                                          <span className="text-gray-400">Custom:</span>{' '}
                                          <span
                                            className={
                                              result.customFormatScore > 0 ? 'text-green-400' : 'text-red-400'
                                            }
                                          >
                                            {result.customFormatScore > 0 ? '+' : ''}
                                            {result.customFormatScore}
                                          </span>
                                        </div>
                                      )}
                                    </div>
                                  )}
                                </div>
                                <button
                                  onClick={() => handleDownload(result, index)}
                                  disabled={downloadingIndex !== null || !result.approved}
                                  className="px-3 py-1 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 disabled:cursor-not-allowed text-white text-sm rounded transition-colors flex items-center gap-1"
                                  title={!result.approved ? 'Release rejected by quality profile' : ''}
                                >
                                  {downloadingIndex === index ? (
                                    <>
                                      <div className="animate-spin rounded-full h-3 w-3 border-b-2 border-white"></div>
                                      <span>Downloading...</span>
                                    </>
                                  ) : (
                                    <>
                                      <ArrowDownTrayIcon className="w-4 h-4" />
                                      <span>Download</span>
                                    </>
                                  )}
                                </button>
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    ) : searchResults.length === 0 && !isSearching ? (
                      <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                        <MagnifyingGlassIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                        <p className="text-gray-400 mb-2">No search performed yet</p>
                        <p className="text-gray-500 text-sm">
                          Click "Search Indexers" to manually search for releases
                        </p>
                      </div>
                    ) : null}
                  </div>
                </div>

                {/* Footer */}
                <div className="px-6 py-4 bg-gray-900/50 border-t border-red-900/30 flex justify-end">
                  <button
                    onClick={onClose}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Close
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
